using OpenTail.Bullshark.Core.Storage;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Storage;

/// <summary>
/// Round 7 item 5: `cleanupPeriodDays` in the user's settings file, and the mark of a transcript near its deletion. The default of 30 days is
/// the one the data-usage docs give. The age is counted from the file's last write, an assumption recorded in the plan.
/// </summary>
public sealed class TranscriptRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-retention-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheDefaultIsThirtyDaysAndTheNearWindowIsThreeDays()
    {
        Assert.Equal(30, TranscriptRetention.DefaultDays);
        Assert.Equal(3, TranscriptRetention.NearDays);
    }

    [Fact]
    public void AWholeNumberOfDaysIsTheSetting()
    {
        var (days, problem) = TranscriptRetention.Parse("""{ "cleanupPeriodDays": 14, "theme": "dark" }""");

        Assert.Equal(14, days);
        Assert.Null(problem);
    }

    [Fact]
    public void AnAbsentKeyIsNoSettingAndNoProblem()
    {
        var (days, problem) = TranscriptRetention.Parse("""{ "theme": "dark" }""");
        var (listDays, listProblem) = TranscriptRetention.Parse("[]");

        Assert.Null(days);
        Assert.Null(problem);
        Assert.Null(listDays);
        Assert.Null(listProblem);
    }

    [Theory]
    [InlineData("""{ "cleanupPeriodDays": 0 }""")]
    [InlineData("""{ "cleanupPeriodDays": -3 }""")]
    [InlineData("""{ "cleanupPeriodDays": "30" }""")]
    [InlineData("""{ "cleanupPeriodDays": 2.5 }""")]
    [InlineData("""{ "cleanupPeriodDays": null }""")]
    public void AValueThatIsNotAPositiveWholeNumberIsAProblemAndNoSetting(string json)
    {
        var (days, problem) = TranscriptRetention.Parse(json);

        Assert.Null(days);
        Assert.Equal("cleanupPeriodDays is not a whole number of days", problem);
    }

    [Fact]
    public void TextThatIsNotJsonIsAProblem()
    {
        var (days, problem) = TranscriptRetention.Parse("{ cleanupPeriodDays: 30 ");

        Assert.Null(days);
        Assert.Equal("settings.json is not valid JSON", problem);
    }

    [Fact]
    public void AMissingSettingsFileIsNoSetting()
    {
        var (days, problem) = TranscriptRetention.ReadDays(Path.Combine(_dir, "settings.json"));

        Assert.Null(days);
        Assert.Null(problem);
    }

    [Fact]
    public void ASettingsFileIsReadForTheDays()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "cleanupPeriodDays": 45 }""");

        var (days, problem) = TranscriptRetention.ReadDays(path);

        Assert.Equal(45, days);
        Assert.Null(problem);
    }

    [Fact]
    public void ABrokenSettingsFileIsAProblemNotAnError()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        var (days, problem) = TranscriptRetention.ReadDays(path);

        Assert.Null(days);
        Assert.Equal("settings.json is not valid JSON", problem);
    }

    [Theory]
    [InlineData(26, 0, RetentionMark.None)]
    [InlineData(26, 23, RetentionMark.None)]
    [InlineData(27, 0, RetentionMark.Near)]
    [InlineData(29, 23, RetentionMark.Near)]
    [InlineData(30, 0, RetentionMark.Due)]
    [InlineData(45, 0, RetentionMark.Due)]
    public void AgeMarksTheTranscriptNearOrPastItsDeletion(int days, int hours, RetentionMark expected) =>
        Assert.Equal(expected, TranscriptRetention.MarkOf(TimeSpan.FromDays(days) + TimeSpan.FromHours(hours), 30));

    [Fact]
    public void TheNearWindowFollowsTheConfiguredDays()
    {
        // With 7 days, a 5-day-old transcript is within the three days before deletion; with 30 days it is not.
        Assert.Equal(RetentionMark.Near, TranscriptRetention.MarkOf(TimeSpan.FromDays(5), 7));
        Assert.Equal(RetentionMark.None, TranscriptRetention.MarkOf(TimeSpan.FromDays(5), 30));
    }

    [Theory]
    [InlineData(27.5, 3)]
    [InlineData(28, 2)]
    [InlineData(29.9, 1)]
    [InlineData(30, 1)]
    [InlineData(40, 1)]
    public void DaysLeftIsWholeDaysAtLeastOne(double ageDays, int expected) =>
        Assert.Equal(expected, TranscriptRetention.DaysLeft(TimeSpan.FromDays(ageDays), 30));

    [Fact]
    public void TheSentenceNamesTheDefaultOrTheSetting()
    {
        Assert.Equal("Claude deletes transcripts older than 30 days (Claude's default); archive to keep them.",
            TranscriptRetention.Sentence(30, fromSettings: false));
        Assert.Equal("Claude deletes transcripts older than 45 days (from ~/.claude/settings.json; other settings files may override it); archive to keep them.",
            TranscriptRetention.Sentence(45, fromSettings: true));
    }

    [Fact]
    public void TheSettingsFileIsBesideTheProjectsFolderWhateverItsTrailingSeparator()
    {
        // Round 8 item 11: a projects folder with a trailing separator must not make the settings file be looked for in the projects folder.
        var home = Path.Combine("C:\\", "Users", "x", ".claude");
        var expected = Path.Combine(home, "settings.json");

        Assert.Equal(expected, TranscriptRetention.SettingsBeside(Path.Combine(home, "projects")));
        Assert.Equal(expected, TranscriptRetention.SettingsBeside(Path.Combine(home, "projects") + Path.DirectorySeparatorChar));
        Assert.Null(TranscriptRetention.SettingsBeside("C:\\"));
    }

    [Fact]
    public void NoSettingsPathIsNoSetting()
    {
        Assert.Equal((null, null), TranscriptRetention.ReadDays((string?)null));
    }
}

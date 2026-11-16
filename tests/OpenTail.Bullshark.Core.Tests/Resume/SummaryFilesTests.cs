using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

public sealed class SummaryFilesTests : IDisposable
{
    private const string SessionId = "5bc2852c-1f0e-4a3b-9d7e-0c1a2b3c4d5e";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-summary-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheFileNameHasTheDateTheNameTheLevelAndTheShortIdInThatOrder()
    {
        var name = SummaryFiles.FileNameFor(new DateOnly(2026, 11, 16), "ovc-planner", SummaryLevel.Complete, SessionId);

        Assert.Equal("2026-11-16 ovc-planner complete 5bc2852c.md", name);
    }

    [Fact]
    public void ACharacterWindowsRefusesIsReplacedAndTheNameIsCut()
    {
        var wild = "Plan: the \"L4\" <tab> a/b|c?" + new string('x', 120);

        var name = SummaryFiles.FileNameFor(new DateOnly(2026, 11, 16), wild, SummaryLevel.Normal, SessionId);

        var invalid = Path.GetInvalidFileNameChars();
        Assert.True(name.All(c => !invalid.Contains(c)), name);
        var namePart = name["2026-11-16 ".Length..^" normal 5bc2852c.md".Length];
        Assert.StartsWith("Plan the L4 tab a b c", namePart);
        Assert.True(namePart.Length <= SummaryFiles.MaxNameLength, $"the name part is {namePart.Length} characters");
    }

    [Fact]
    public void AnEmptyOrDotsOnlyNameBecomesSession()
    {
        Assert.Equal("session", SummaryFiles.CleanName("   "));
        Assert.Equal("session", SummaryFiles.CleanName("..."));
    }

    [Fact]
    public void SaveWritesTheSummaryToANewFileAndReturnsItsPath()
    {
        // Noon UTC is the same calendar day in every time zone from -12 to +11, so the date in the name is stable.
        var madeAt = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        var path = SummaryFiles.Save(_dir, "Plan for L4", SummaryLevel.Minimal, SessionId, "  The goal is the library.  ", madeAt);

        Assert.Equal(Path.GetFullPath(_dir), Path.GetDirectoryName(Path.GetFullPath(path)));
        Assert.StartsWith(madeAt.LocalDateTime.ToString("yyyy-MM-dd"), Path.GetFileName(path));
        Assert.EndsWith(" minimal 5bc2852c.md", path);
        var text = File.ReadAllText(path);
        Assert.Contains("Summary of session " + SessionId, text);
        Assert.Contains("The goal is the library.", text);
        Assert.DoesNotContain("  The goal", text);
    }

    [Fact]
    public void ASecondSummaryTheSameDayIsNotOverwritten()
    {
        var madeAt = DateTimeOffset.Now;
        var first = SummaryFiles.Save(_dir, "Plan", SummaryLevel.Normal, SessionId, "first summary", madeAt);

        var second = SummaryFiles.Save(_dir, "Plan", SummaryLevel.Normal, SessionId, "second summary", madeAt);

        Assert.NotEqual(first, second);
        Assert.EndsWith(" (2).md", second);
        Assert.Contains("first summary", File.ReadAllText(first));
        Assert.Contains("second summary", File.ReadAllText(second));
    }

    [Fact]
    public void TheFolderIsTheSummariesFolderBesideTheStateFile()
    {
        var state = Path.Combine(_dir, "state.json");

        Assert.Equal(Path.Combine(Path.GetFullPath(_dir), "Summaries"), SummaryFiles.FolderBeside(state));
    }
}

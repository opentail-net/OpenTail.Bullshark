using System.Reflection;
using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Finding one session's rollout (note A). An id that is a UUIDv7 is looked for first in the dated folders of its start day and the days either
/// side; every other rollout is still found by the full search, and only the full search can say a session is missing. Synthetic fixtures only;
/// plain files run on both targets.
/// </summary>
public sealed class CodexRolloutLookupTests : IDisposable
{
    /// <summary>The start of the test id: noon UTC. The folder is still computed from the local day, as the code under test does.</summary>
    private static readonly DateTimeOffset StartedAt = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A UUIDv7 id that starts at <see cref="StartedAt"/>.</summary>
    private static readonly string Id = CodexHome.UuidV7(StartedAt.ToUnixTimeMilliseconds());

    private readonly CodexHome _home = new();

    public void Dispose() => _home.Dispose();

    private CodexTranscriptReader Reader() => new(_home.Paths());

    /// <summary>The local calendar day of the start: the dated folder Codex writes the rollout into.</summary>
    private static DateOnly StartDay() => DateOnly.FromDateTime(StartedAt.ToLocalTime().DateTime);

    /// <summary>A rollout with the id in its name, in the folder of the start day moved by <paramref name="days"/>.</summary>
    private string WriteOn(int days, string id) =>
        _home.WritePlainOn(StartDay().AddDays(days), CodexHome.NameFor(id), CodexHome.Standard(id));

    /// <summary>
    /// A decoy: a second rollout with the same id in its name, in a folder five days before the start day, which is outside the three folders
    /// searched first. The full search sorts by path and would return it; the id's own folders must return the real one instead.
    /// </summary>
    private string WriteDecoy() => _home.WritePlainOn(StartDay().AddDays(-5), CodexHome.NameFor(Id), CodexHome.Standard(Id));

    /// <summary>The internal FindRollout, reached by reflection (Core has no InternalsVisibleTo; see CodexHistorySourceTests).</summary>
    private static string? FindRollout(CodexTranscriptReader reader, string id) =>
        (string?)typeof(CodexTranscriptReader).GetMethod("FindRollout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(reader, new object[] { id });

    /// <summary>The whole archive in the full search's order (RolloutReader.Files), by reflection.</summary>
    private string[] FullSearchOrder()
    {
        var reader = typeof(CodexHistorySource).Assembly.GetType("OpenTail.Bullshark.Core.Codex.RolloutReader", throwOnError: true)!;
        return ((List<string>)reader.GetMethod("Files")!.Invoke(null, new object[] { _home.Sessions })!).ToArray();
    }

    [Fact]
    public async Task ARolloutInTheStartDayFolderIsFound()
    {
        var path = WriteOn(0, Id);

        Assert.Equal(path, FindRollout(Reader(), Id));
        var page = await Reader().ReadPageAsync(Id, 0, 1, CancellationToken.None);
        Assert.Contains(Id, Assert.Single(page.Lines));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public void ARolloutInTheStartDayOrANeighbourIsFoundBeforeTheRestOfTheArchive(int days)
    {
        // The decoy is the full search's first answer, so only a lookup of the window can return the rollout in the window.
        var decoy = WriteDecoy();
        var path = WriteOn(days, Id);
        Assert.Equal(decoy, FullSearchOrder()[0]);

        Assert.Equal(path, FindRollout(Reader(), Id));
    }

    [Fact]
    public void ARolloutFarFromItsIdsDayIsFoundByTheFullSearch()
    {
        var path = WriteOn(10, Id);

        Assert.Equal(path, FindRollout(Reader(), Id));
    }

    [Fact]
    public async Task ARolloutWhoseNameLacksTheIdIsFoundByItsSessionRecord()
    {
        // The window tests names only; a rollout whose name lacks the id is found by the full search's session-record test.
        _home.WritePlainOn(StartDay(), "rollout-no-uuid-in-name.jsonl", CodexHome.Standard(Id));

        var page = await Reader().ReadPageAsync(Id, 0, 1, CancellationToken.None);

        Assert.Contains(Id, Assert.Single(page.Lines));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void AMissingIdGivesNullAfterTheFullSearch(int otherDay)
    {
        // Another rollout sits in the start day's folder, or in a far folder with no folder for the start day. Neither has the id.
        _home.WritePlainOn(StartDay().AddDays(otherDay), CodexHome.NameFor(CodexHome.SecondId), CodexHome.Standard(CodexHome.SecondId));

        Assert.Null(FindRollout(Reader(), Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task AMissingIdThrowsFileNotFoundFromTheReaders(int otherDay)
    {
        _home.WritePlainOn(StartDay().AddDays(otherDay), CodexHome.NameFor(CodexHome.SecondId), CodexHome.Standard(CodexHome.SecondId));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Reader().ReadPageAsync(Id, 0, 1, CancellationToken.None));
        Assert.Throws<FileNotFoundException>(() => CodexTurns.ReadSession(_home.Paths(), Id, CancellationToken.None));
    }

    [Fact]
    public void AVersion4IdIsFoundByTheFullSearchFromAnyFolder()
    {
        // A version 4 id carries no time, so no day folder is tried for it; its rollout is found by the full search wherever it sits.
        const string v4 = "550e8400-e29b-41d4-a716-446655440000";
        var path = _home.WritePlainOn(StartDay().AddDays(10), CodexHome.NameFor(v4), CodexHome.Standard(v4));

        Assert.Equal(path, FindRollout(Reader(), v4));
    }

    [Fact]
    public void AnUpperCaseIdFindsItsRolloutInTheStartDay()
    {
        // The name test is case-insensitive, as it always was, and the window uses the same test.
        var path = WriteOn(0, Id);

        Assert.Equal(path, FindRollout(Reader(), Id.ToUpperInvariant()));
    }
}

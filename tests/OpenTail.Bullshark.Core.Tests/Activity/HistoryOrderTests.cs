using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>Subagent transcripts come last in the History lists (owner, 2026-11-18).</summary>
public class HistoryOrderTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static SessionDigest Digest(string id, int minutesAgo, bool subagent = false, string folder = "C--Git") =>
        new(id, id + ".jsonl", folder, subagent, null, Day.AddMinutes(-minutesAgo), 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);

    private static string[] Ids(IEnumerable<SessionDigest> digests) => digests.Select(d => d.SessionId).ToArray();

    [Fact]
    public void MainSessionsComeFirstThenSubagentsEachNewestFirst()
    {
        var sorted = HistoryOrder.Sorted([
            Digest("sub-new", 1, subagent: true), Digest("main-old", 50), Digest("sub-old", 20, subagent: true), Digest("main-new", 10)]);

        Assert.Equal(["main-new", "main-old", "sub-new", "sub-old"], Ids(sorted));
    }

    [Fact]
    public void ANewerSubagentNeverPassesAnOlderMainSession()
    {
        Assert.Equal(["main", "sub"], Ids(HistoryOrder.Sorted([Digest("sub", 0, subagent: true), Digest("main", 999)])));
    }

    [Fact]
    public void ASessionWithNoActivityTimeSortsLastInItsPart()
    {
        var none = Digest("none", 0) with { LastActivity = null };

        Assert.Equal(["main", "none"], Ids(HistoryOrder.Sorted([none, Digest("main", 5)])));
    }

    [Fact]
    public void EmptyInputGivesNothing() => Assert.Empty(HistoryOrder.Sorted([]));

    [Fact]
    public void AFolderIsPlacedByItsNewestMainSessionNotItsNewestSubagent()
    {
        var folder = new[] { Digest("main", 100), Digest("sub", 1, subagent: true) };

        Assert.Equal(Day.AddMinutes(-100), HistoryOrder.FolderRecency(folder));
    }

    [Fact]
    public void AFolderOfOnlySubagentsUsesThemButRanksAfterOtherFolders()
    {
        var onlySubs = new[] { Digest("sub", 1, subagent: true) };
        var withMain = new[] { Digest("main", 500) };

        Assert.Equal(Day.AddMinutes(-1), HistoryOrder.FolderRecency(onlySubs));
        Assert.Equal(1, HistoryOrder.FolderRank(onlySubs));
        Assert.Equal(0, HistoryOrder.FolderRank(withMain));
    }
}

using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The Overlap reason in the attention list (gems item 1): one item per session per shared file, and the "gained" rule that
/// announces each new shared file once while the rest of the list keeps its old behaviour.
/// </summary>
public sealed class OverlapAttentionTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private const string CoderA = "coder-a";
    private const string CoderB = "coder-b";
    private const string IdA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string IdB = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string FileX = @"C:\work\X.cs";
    private const string FileY = @"C:\work\Y.cs";

    private static SessionRecord Record(string name, string sessionId, DateTimeOffset startedAt) =>
        new(SessionRole.Coder, sessionId, name, startedAt, 0, [], null, null);

    private static AgentInfo Agent(string sessionId, string state) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: "x", Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    /// <summary>A clash of coder A and coder B over one file. The last writes are set so the detail has them.</summary>
    private static FileClash Clash(string file) => new(file,
    [
        new ClashSession(CoderA, IdA, Start.AddMinutes(4)),
        new ClashSession(CoderB, IdB, Start.AddMinutes(9)),
    ]);

    private static AttentionItem Overlap(string name, string file, DateTimeOffset startedAt) =>
        new(name, name == CoderA ? IdA : IdB, AttentionReason.Overlap, "detail", startedAt, file);

    private static AttentionItem Blocked(string name, DateTimeOffset startedAt) =>
        new(name, name == CoderA ? IdA : IdB, AttentionReason.Blocked, "waiting for input", startedAt);

    [Fact]
    public void EachSessionThatWroteASharedFileGetsAnOverlapItemNamingTheFile()
    {
        var records = new[] { Record(CoderA, IdA, Start), Record(CoderB, IdB, Start.AddMinutes(1)) };

        var items = AttentionList.From(records, [Agent(IdA, "working"), Agent(IdB, "working")], [Clash(FileX)]);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(AttentionReason.Overlap, item.Reason));
        Assert.Equal(FileX, items[0].FilePath);
        Assert.Equal(CoderA, items[0].Name);
        Assert.Equal(IdA, items[0].SessionId);
        Assert.Contains(FileX, items[0].Detail);
        Assert.Contains(CoderB, items[0].Detail);
    }

    [Fact]
    public void AnOverlapOfASessionWithNoRecordIsLeftOut()
    {
        var items = AttentionList.From([Record(CoderA, IdA, Start)], [Agent(IdA, "working")], [Clash(FileX)]);

        var item = Assert.Single(items);
        Assert.Equal(CoderA, item.Name);
    }

    [Fact]
    public void NoClashesMeansTheListIsWhatItWasBefore()
    {
        var records = new[] { Record(CoderA, IdA, Start) };

        Assert.Empty(AttentionList.From(records, [Agent(IdA, "working")], []));
        Assert.Empty(AttentionList.From(records, [Agent(IdA, "working")]));
    }

    [Fact]
    public void OverlapItemsSortWithTheRestOldestFirst()
    {
        var records = new[] { Record(CoderA, IdA, Start.AddHours(1)), Record(CoderB, IdB, Start) };
        var agents = new[] { Agent(IdA, "blocked") };

        var items = AttentionList.From(records, agents, [Clash(FileX)]);

        // Coder B (started first) has an overlap; coder A is blocked and has one too, so it comes second with both items.
        Assert.Equal([CoderB, CoderA, CoderA], items.Select(i => i.Name));
        Assert.Equal([AttentionReason.Overlap, AttentionReason.Blocked, AttentionReason.Overlap], items.Select(i => i.Reason));
    }

    [Fact]
    public void AnAlreadyAnnouncedOverlapIsNotNewAgain()
    {
        var previous = new[] { Overlap(CoderA, FileX, Start) };
        var current = new[] { Overlap(CoderA, FileX, Start) };

        Assert.Empty(AttentionList.Gained(previous, current));
    }

    [Fact]
    public void ASecondSharedFileOfAWaitingSessionIsAnnounced()
    {
        var previous = new[] { Overlap(CoderA, FileX, Start) };
        var current = new[] { Overlap(CoderA, FileX, Start), Overlap(CoderA, FileY, Start) };

        var gained = Assert.Single(AttentionList.Gained(previous, current));
        Assert.Equal(FileY, gained.FilePath);
    }

    [Fact]
    public void AnOverlapOfASessionThatIsAlreadyBlockedIsAnnounced()
    {
        var previous = new[] { Blocked(CoderA, Start) };
        var current = new[] { Blocked(CoderA, Start), Overlap(CoderA, FileX, Start) };

        var gained = Assert.Single(AttentionList.Gained(previous, current));
        Assert.Equal(AttentionReason.Overlap, gained.Reason);
    }

    [Fact]
    public void AWaitingSessionThatChangesFromOverlapToBlockedIsNotNew()
    {
        // Changing the reason while it waits is not new, as it was before overlaps existed.
        var previous = new[] { Overlap(CoderA, FileX, Start) };
        var current = new[] { Blocked(CoderA, Start) };

        Assert.Empty(AttentionList.Gained(previous, current));
    }

    [Fact]
    public void ASessionThatStoppedWaitingAndWaitsAgainForAFileIsNewAgain()
    {
        var previous = new[] { Overlap(CoderA, FileX, Start) };
        var cleared = Array.Empty<AttentionItem>();
        var again = new[] { Overlap(CoderA, FileX, Start) };

        Assert.Empty(AttentionList.Gained(previous, cleared));
        Assert.Single(AttentionList.Gained(cleared, again));
    }
}

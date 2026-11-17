using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The Conflict reason (gems item 2): a coder whose committed work conflicts with the default branch. The item names the files and
/// the branch, and it is announced once when it joins the list, like the overlap notices.
/// </summary>
public sealed class ConflictAttentionTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private const string Coder = "coder-1";
    private const string Id = "cccccccc-0000-0000-0000-000000000001";
    private const string Target = "refs/remotes/origin/main";

    private static SessionRecord Record(string name, string sessionId) =>
        new(SessionRole.Coder, sessionId, name, Start, 0, [], null, null);

    private static AttentionItem Blocked(string name, string sessionId) =>
        new(name, sessionId, AttentionReason.Blocked, "waiting for input", Start);

    private static AttentionItem Conflict(string name, string sessionId) =>
        new(name, sessionId, AttentionReason.Conflict, "conflict", Start);

    [Fact]
    public void AConflictNamesTheFilesAndTheBranchWithoutItsRefsPrefix()
    {
        var items = AttentionList.From([Record(Coder, Id)], [], conflicts: [new SessionConflict(Coder, ["a.cs", "b.cs"], Target)]);

        var item = Assert.Single(items);
        Assert.Equal(AttentionReason.Conflict, item.Reason);
        Assert.Equal(Coder, item.Name);
        Assert.Equal(Id, item.SessionId);
        Assert.Equal("2 files conflict with origin/main: a.cs, b.cs", item.Detail);
    }

    [Fact]
    public void MoreThanFiveFilesAreCountedNotListed()
    {
        var files = new[] { "a.cs", "b.cs", "c.cs", "d.cs", "e.cs", "f.cs", "g.cs" };

        var item = Assert.Single(AttentionList.From([Record(Coder, Id)], [], conflicts: [new SessionConflict(Coder, files, Target)]));

        Assert.Equal("7 files conflict with origin/main: a.cs, b.cs, c.cs, d.cs, e.cs and 2 more", item.Detail);
    }

    [Fact]
    public void ASingleFileIsSaidInTheSingular()
    {
        var item = Assert.Single(AttentionList.From([Record(Coder, Id)], [], conflicts: [new SessionConflict(Coder, ["only.cs"], "main")]));

        Assert.Equal("1 file conflict with main: only.cs", item.Detail);
    }

    [Fact]
    public void AConflictOfASessionWithNoRecordIsLeftOut()
    {
        var items = AttentionList.From([Record(Coder, Id)], [], conflicts: [new SessionConflict("someone-else", ["a.cs"], Target)]);

        Assert.Empty(items);
    }

    [Fact]
    public void AConflictIsAnnouncedWhenItJoinsTheListEvenIfTheSessionAlreadyWaits()
    {
        var previous = new[] { Blocked(Coder, Id) };
        var current = new[] { Blocked(Coder, Id), Conflict(Coder, Id) };

        var gained = Assert.Single(AttentionList.Gained(previous, current));
        Assert.Equal(AttentionReason.Conflict, gained.Reason);
    }

    [Fact]
    public void AConflictAlreadyAnnouncedIsNotAnnouncedAgain()
    {
        var previous = new[] { Conflict(Coder, Id) };
        var current = new[] { Conflict(Coder, Id) };

        Assert.Empty(AttentionList.Gained(previous, current));
    }

    [Fact]
    public void AWaitingSessionThatChangesFromConflictToBlockedIsNotNew()
    {
        var previous = new[] { Conflict(Coder, Id) };
        var current = new[] { Blocked(Coder, Id) };

        Assert.Empty(AttentionList.Gained(previous, current));
    }
}

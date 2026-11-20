using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The usage-limit notice in Needs you (limit wake, 2026-11-20): it shows the text the caller built for a session that stopped at the limit, it is
/// a notice (never re-alerts, never announced), it yields to every other reason, and it goes when its text does. Synthetic records only.
/// </summary>
public sealed class UsageLimitAttentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private const string SessionA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string SessionB = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string Text = "Limit resets 12:20 · wakes after";

    private static SessionRecord Record(string name, string sessionId, string? lastAlert = null) =>
        new(SessionRole.Coder, sessionId, name, Now.AddHours(-1), 0, [], null, lastAlert);

    private static AgentInfo Agent(string sessionId, string state) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: "x", Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    private static IReadOnlyDictionary<string, string> Limits(string sessionId, string text = Text) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [sessionId] = text };

    [Fact]
    public void ASessionWithAStopShowsTheUsageLimitNoticeWithItsText()
    {
        var items = AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")], usageLimits: Limits(SessionA));

        var item = Assert.Single(items);
        Assert.Equal(AttentionReason.UsageLimit, item.Reason);
        Assert.Equal(Text, item.Detail);
        Assert.Equal("coder", item.Name);
    }

    [Fact]
    public void ASessionWithNoTextGetsNoNotice()
    {
        Assert.Empty(AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")], usageLimits: null));
        Assert.Empty(AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")], usageLimits: Limits(SessionA, text: " ")));
    }

    [Fact]
    public void ABlockedSessionKeepsItsOwnReasonNotTheLimitNotice()
    {
        var items = AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "blocked")], usageLimits: Limits(SessionA));

        Assert.Equal(AttentionReason.Blocked, Assert.Single(items).Reason);
    }

    [Fact]
    public void AStoppedSessionKeepsItsOwnReasonNotTheLimitNotice()
    {
        var items = AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "stopped")], usageLimits: Limits(SessionA));

        Assert.Equal(AttentionReason.Stopped, Assert.Single(items).Reason);
    }

    [Fact]
    public void AnUnlistedSessionWithAStopGetsTheNoticeToo()
    {
        var items = AttentionList.From([Record("coder", SessionA)], [], usageLimits: Limits(SessionA));

        Assert.Equal(AttentionReason.UsageLimit, Assert.Single(items).Reason);
    }

    [Fact]
    public void ARecordWithNoSessionIdGetsNoNotice()
    {
        var startFailed = Record("coder", string.Empty);

        var items = AttentionList.From([startFailed], [], usageLimits: Limits(string.Empty));

        Assert.Equal(AttentionReason.StartFailed, Assert.Single(items).Reason);
    }

    [Fact]
    public void TheNoticeIsNotAWaitSoAnotherReasonOfTheSameSessionIsStillAnnounced()
    {
        var before = AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")], usageLimits: Limits(SessionA));
        var after = AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "blocked")], usageLimits: Limits(SessionA));

        var gained = AttentionList.Gained(before, after);

        Assert.Equal(AttentionReason.Blocked, Assert.Single(gained).Reason);
    }

    [Fact]
    public void TheNoticeIsNeverAnnounced()
    {
        var gained = AttentionList.Gained(
            [],
            AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")], usageLimits: Limits(SessionA)));

        Assert.Empty(gained);
    }

    [Fact]
    public void TheNoticeNeverEscalatesAndIsRememberedAsANoticeKey()
    {
        var item = Assert.Single(AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")], usageLimits: Limits(SessionA)));

        Assert.False(AttentionEscalation.Escalates(item));
        Assert.Contains(AttentionList.NoticeKeys([item]), key => key.Length > 0);
    }

    [Fact]
    public void TheNoticeGoesWhenItsTextGoes()
    {
        var items = AttentionList.From([Record("coder", SessionA)], [Agent(SessionA, "done")],
            usageLimits: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(items);
    }

    [Fact]
    public void TheNoticeIsNotOnTheOtherSession()
    {
        var items = AttentionList.From(
            [Record("first", SessionA), Record("second", SessionB)],
            [Agent(SessionA, "done"), Agent(SessionB, "done")],
            usageLimits: Limits(SessionA));

        Assert.Equal("first", Assert.Single(items).Name);
    }
}

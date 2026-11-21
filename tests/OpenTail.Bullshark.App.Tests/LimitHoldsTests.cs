using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The usage-limit holds the window hands to the supervisor after a probe (review item 1, 2026-11-21): a supervised session with a recent stop whose reset
/// is still ahead, keyed by its name. Pure: no window, no UI thread.
/// </summary>
public sealed class LimitHoldsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-2222-3333-4444-555555555555";

    private static SessionRecord Record(string sessionId, string name) =>
        new(SessionRole.Coder, sessionId, name, Now.AddHours(-5), 0, [], "working", null);

    private static LimitStop Stop(DateTimeOffset hitAt, DateTimeOffset resetsAt) => new(hitAt, resetsAt, "five_hour");

    [Fact]
    public void ARecentStopWithItsResetAheadHoldsTheSessionByName()
    {
        var records = new Dictionary<string, SessionRecord> { ["coder-a"] = Record(SessionId, "coder-a") };
        var stops = new Dictionary<string, LimitStop?> { [SessionId] = Stop(Now.AddMinutes(-30), Now.AddHours(3)) };

        var holds = SessionWindow.LimitHoldsOf(records, stops, Now);

        Assert.Equal(Now.AddHours(3), Assert.Single(holds, pair => pair.Key == "coder-a").Value);
        Assert.Single(holds);
    }

    [Fact]
    public void ASessionWhoseResetHasPassedHasNoHold()
    {
        var records = new Dictionary<string, SessionRecord> { ["coder-a"] = Record(SessionId, "coder-a") };
        var stops = new Dictionary<string, LimitStop?> { [SessionId] = Stop(Now.AddHours(-2), Now.AddMinutes(-1)) };

        Assert.Empty(SessionWindow.LimitHoldsOf(records, stops, Now));
    }

    [Fact]
    public void AnOldStopHasNoHold()
    {
        var records = new Dictionary<string, SessionRecord> { ["coder-a"] = Record(SessionId, "coder-a") };
        var stops = new Dictionary<string, LimitStop?> { [SessionId] = Stop(Now.AddDays(-30), Now.AddDays(-29)) };

        Assert.Empty(SessionWindow.LimitHoldsOf(records, stops, Now));
    }

    [Fact]
    public void ASessionWithNoStopOrNoIdHasNoHold()
    {
        var records = new Dictionary<string, SessionRecord>
        {
            ["coder-a"] = Record(SessionId, "coder-a"),
            ["coder-b"] = Record("aaaaaaaa-0000-0000-0000-000000000000", "coder-b"),
            ["coder-c"] = Record(string.Empty, "coder-c"),
        };
        var stops = new Dictionary<string, LimitStop?> { ["aaaaaaaa-0000-0000-0000-000000000000"] = null };

        Assert.Empty(SessionWindow.LimitHoldsOf(records, stops, Now));
    }
}

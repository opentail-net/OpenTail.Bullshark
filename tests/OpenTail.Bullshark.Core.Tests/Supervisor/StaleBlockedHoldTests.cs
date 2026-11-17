using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Round 9 item 4 (R3): a stale-blocked alert holds a lost session from a resume for an hour from the alert, and no longer. A session still not
/// listed after that goes through the lost-session rules. No stale-blocked alert is raised while the respawn hold is on.
/// </summary>
public sealed class StaleBlockedHoldTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private static readonly DateTimeOffset Raised = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-stale-hold-" + Guid.NewGuid().ToString("N")[..8]);

    public StaleBlockedHoldTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly DaemonStatus NotRunning = DaemonStatus.Parse("not running\n  control.sock: unreachable (connect ENOENT)\n");

    /// <summary>A clock whose monotonic reading moves with <see cref="Now"/>, as the respawn hold reads it.</summary>
    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Now.UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    /// <summary>A record of the planner whose last alert is the stale-blocked one, raised at <paramref name="alertedAt"/> (null: no time).</summary>
    private static SessionRecord StaleBlocked(DateTimeOffset? alertedAt, IReadOnlyList<DateTimeOffset>? restartTimes = null, string lastState = "blocked") =>
        new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, Raised.AddHours(-2), 0, restartTimes ?? [], lastState,
            AttentionList.StaleBlockedAlertPrefix, null, alertedAt);

    /// <summary>The policy for the planner with nothing listed: the session is lost.</summary>
    private static IReadOnlyList<SupervisorAction> Decide(SessionRecord record, DateTimeOffset now, RunBudget? budget = null) =>
        SupervisorPolicy.Decide(TestSpecs.Planner(@"C:\W", "t"), record, [], now, SupervisorRules.Default, budget);

    // ---- The policy, as a pure function ----

    [Fact]
    public void AStaleBlockedSessionNotListedWithinTheHourIsHeldFromAResume()
    {
        var actions = Decide(StaleBlocked(Raised), Raised.AddMinutes(30));

        Assert.Empty(actions);
    }

    [Fact]
    public void AfterTheHourAStaleBlockedSessionNotListedIsResumedUnderTheLostRules()
    {
        var actions = Decide(StaleBlocked(Raised), Raised.AddMinutes(61));

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
    }

    [Fact]
    public void TheHoldIsOverAtExactlyAnHour()
    {
        Assert.Empty(Decide(StaleBlocked(Raised), Raised.AddMinutes(59).AddSeconds(59)));
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(StaleBlocked(Raised), Raised.AddHours(1))));
        Assert.Equal(TimeSpan.FromHours(1), SupervisorPolicy.StaleBlockedHold);
    }

    [Fact]
    public void AnAlertWithNoTimeFromBeforeTheFieldIsNotHeld()
    {
        // A file written before this round has the alert but no time. It has no hour to hold to, so the lost-session rules apply.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(StaleBlocked(null), Raised.AddMinutes(1))));
    }

    [Fact]
    public void AnAlertTimedAheadOfNowIsNotHeldForever()
    {
        // The clock went back after the alert: the time is ahead of now. It is not held for longer than the hour.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(StaleBlocked(Raised.AddHours(5)), Raised)));
    }

    [Fact]
    public void AfterTheHourTheRestartLoopStillStopsALostSession()
    {
        var restarts = new[] { Raised.AddMinutes(10), Raised.AddMinutes(20), Raised.AddMinutes(30) };

        var actions = Decide(StaleBlocked(Raised, restartTimes: restarts), Raised.AddMinutes(61));

        Assert.Equal(AlertKind.RestartLoop, Assert.IsType<SupervisorAction.Alert>(Assert.Single(actions)).Kind);
    }

    [Fact]
    public void AfterTheHourTheRunsLimitStillStopsALostSession()
    {
        // At the limit nothing is resumed; the owner is told the run reached it (the limit notice, once).
        var actions = Decide(StaleBlocked(Raised), Raised.AddMinutes(61), new RunBudget(1.0));

        Assert.DoesNotContain(actions, a => a is SupervisorAction.Resume);
        Assert.Equal(AlertKind.LimitReached, Assert.IsType<SupervisorAction.Alert>(Assert.Single(actions)).Kind);
    }

    [Fact]
    public void AStoppedSessionIsNotResumedAfterTheHour()
    {
        // The owner stopped it after the stale alert: the stop guard keeps it, however old the alert is.
        Assert.Empty(Decide(StaleBlocked(Raised, lastState: "stopped"), Raised.AddHours(3)));
    }

    // ---- Through the runner, over two passes ----

    /// <summary>
    /// A stand-in for `claude`: the planner is listed in <see cref="Listed"/> (a state, or null for not listed). A resume is a launch.
    /// </summary>
    private sealed class FakeClaude
    {
        public string? Listed { get; set; }
        public FakeCliRunner Runner { get; }

        public FakeClaude(string? listedState)
        {
            Listed = listedState;
            Runner = new FakeCliRunner(args => args[0] switch
            {
                "agents" => FakeCliRunner.Ok(Listing()),
                "logs" => FakeCliRunner.Ok(string.Empty),
                "--bg" => FakeCliRunner.Ok("backgrounded · abcdef12 · test-planner\n"),
                _ => FakeCliRunner.Ok(),
            });
        }

        private string Listing() => Listed is null
            ? "[]"
            : $$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"{{Listed}}","cwd":"C:\\W"}]""";
    }

    private StateStore Store(SessionRecord? record)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        if (record is not null) store.Save(new Dictionary<string, SessionRecord> { [record.Name] = record });
        return store;
    }

    private static SupervisorRunner RunnerFor(FakeClaude claude, StateStore store, TimeProvider clock, RespawnHold? hold = null) =>
        new(new ClaudeCli(claude.Runner), store, clock, resolveTimeout: TimeSpan.FromSeconds(1)) { Daemon = NotRunning, Hold = hold };

    private static List<string> ResumeArgs(FakeClaude claude) =>
        claude.Runner.Calls.Where(c => c.Args.Contains("--resume")).SelectMany(c => c.Args).ToList();

    [Fact]
    public async Task ADroppedRowWithinTheHourIsNotResumedAndAfterItIsResumed()
    {
        var clock = new SettableClock(Raised);
        var claude = new FakeClaude(listedState: "blocked");
        var store = Store(new SessionRecord(SessionRole.Planner, SessionId, TestSpecs.PlannerName, Raised.AddHours(-2), 0, [], "working", null));
        var spec = TestSpecs.Planner(@"C:\W", "t");

        // The pass that sees it blocked while the daemon is not running: one stale-blocked alert, stamped with the time it was raised.
        var alerted = await RunnerFor(claude, store, clock).CheckOnceAsync([spec]);
        Assert.Equal(AttentionList.StaleBlockedDetail, Assert.IsType<SupervisorAction.Alert>(Assert.Single(alerted)).Detail);
        Assert.Equal(Raised, store.Load()[TestSpecs.PlannerName].StaleBlockedAt);

        // The row is dropped. Half an hour later the role is still held: nothing is resumed.
        claude.Listed = null;
        clock.Now = Raised.AddMinutes(30);
        Assert.Empty(await RunnerFor(claude, store, clock).CheckOnceAsync([spec]));
        Assert.Empty(ResumeArgs(claude));

        // An hour after the alert it goes through the lost-session rules, and is resumed under its id.
        clock.Now = Raised.AddMinutes(61);
        var resumed = await RunnerFor(claude, store, clock).CheckOnceAsync([spec]);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(resumed));
        Assert.Contains("--resume", ResumeArgs(claude));
    }

    [Fact]
    public async Task NoStaleBlockedAlertIsRaisedWhileTheRespawnHoldIsOn()
    {
        // The respawn is restarting the workers, and the listing shows the session blocked while the daemon is not running for a moment.
        var clock = new SettableClock(Raised);
        var hold = new RespawnHold(clock);
        var claude = new FakeClaude(listedState: "blocked");
        var store = Store(new SessionRecord(SessionRole.Planner, SessionId, TestSpecs.PlannerName, Raised.AddHours(-2), 0, [], "working", null));
        var spec = TestSpecs.Planner(@"C:\W", "t");
        hold.Begin();

        var during = await RunnerFor(claude, store, clock, hold).CheckOnceAsync([spec]);

        Assert.Empty(during);
        Assert.Null(store.Load()[TestSpecs.PlannerName].LastAlert);

        // The hold ends and the session is still blocked: the alert is raised then, once.
        hold.End();
        clock.Now = Raised.AddSeconds(21);
        var after = await RunnerFor(claude, store, clock, hold).CheckOnceAsync([spec]);
        Assert.Equal(AttentionList.StaleBlockedDetail, Assert.IsType<SupervisorAction.Alert>(Assert.Single(after)).Detail);
    }
}

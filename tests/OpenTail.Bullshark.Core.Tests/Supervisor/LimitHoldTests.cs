using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Limit wake, review item 1 (2026-11-21): a lost session whose run is held by a usage-limit stop is not resumed before the stop's reset. The hold is
/// silent (no alert, no restart time), it never resumes a session the owner stopped or that failed, and once the reset has passed the normal lost rules run.
/// </summary>
public sealed class LimitHoldTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly SessionSpec Planner = TestSpecs.Planner(@"C:\Work", "brief");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-limit-hold-" + Guid.NewGuid().ToString("N")[..8]);

    public LimitHoldTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SessionRecord Record(string? lastState = null, string? lastAlert = null, params DateTimeOffset[] restartTimes) =>
        new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, Now.AddHours(-5), restartTimes.Length, restartTimes, lastState, lastAlert);

    /// <summary>The planner as the listing shows it while the daemon is not running: a listed working session is stale, and it is lost.</summary>
    private static AgentInfo StaleWorking() =>
        new(Id: "abcdef12", SessionId: SessionId, Name: TestSpecs.PlannerName, Kind: "background", State: "working", Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    private static IReadOnlyList<SupervisorAction> Decide(SessionRecord? record, IReadOnlyList<AgentInfo> agents, DateTimeOffset? holdUntil) =>
        SupervisorPolicy.Decide(Planner, record, agents, Now, SupervisorRules.Default, limitHoldUntil: holdUntil);

    [Fact]
    public void ALostSessionIsNotResumedWhileItsLimitHolds()
    {
        var actions = Decide(Record(), [], Now.AddHours(2));

        Assert.Empty(actions);
    }

    [Fact]
    public void ALostSessionIsResumedFromTheResetOnWhenTheHoldHasPassed()
    {
        // At the reset itself the hold is over (the check is now < until), so the normal rules resume the session.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(Record(), [], Now)));
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(Record(), [], Now.AddMinutes(-1))));
    }

    [Fact]
    public void AStoppedOrFailedSessionIsNotResumedEvenAfterTheHold()
    {
        Assert.Empty(Decide(Record(lastState: "stopped"), [], Now.AddMinutes(-1)));
        Assert.Empty(Decide(Record(lastState: "failed"), [], Now.AddMinutes(-1)));
        Assert.Empty(Decide(Record(lastAlert: "Stopped: the session was stopped, so it is not restarted."), [], Now.AddMinutes(-1)));
    }

    [Fact]
    public void TheHoldAddsNoAlertAndNoRestartEvenWhereTheRestartLoopWouldAlert()
    {
        // Three restarts in the last hour would raise the restart-loop alert with no hold. With the hold, the pass is silent.
        var recent = Enumerable.Range(1, 3).Select(i => Now.AddMinutes(-10 * i)).ToArray();

        Assert.Empty(Decide(Record(restartTimes: recent), [], Now.AddHours(1)));
        Assert.Empty(Decide(Record(), [], Now.AddHours(1)));
    }

    [Fact]
    public void AStaleWorkingLostSessionIsHeldToo()
    {
        var stale = StaleWorking() with { State = DaemonStatus.StaleState, ListedState = "working" };

        Assert.Empty(Decide(Record(), [stale], Now.AddHours(1)));
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(Record(), [stale], Now.AddMinutes(-1))));
    }

    [Fact]
    public void WithoutAHoldTheLostSessionIsResumedAsBefore()
    {
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(Decide(Record(), [], holdUntil: null)));
    }

    // ---- The runner: LimitHolds reaches the policy, through a pass against the fake `claude` ----

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ARunnerPassHoldsALostSessionUntilTheLimitHoldIsOver()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, Now.AddHours(-5), 0, [], "working", null),
        });
        var clock = new SettableClock(Now);
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Ok("backgrounded · abcdef12 · planner\n"),
            _ => FakeCliRunner.Ok(),
        });
        var runner = new SupervisorRunner(new ClaudeCli(cli), store, clock, resolveTimeout: TimeSpan.FromSeconds(3))
        {
            LimitHolds = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { [TestSpecs.PlannerName] = Now.AddHours(2) },
        };

        var held = await runner.CheckOnceAsync([Planner]);

        Assert.Empty(held);
        Assert.DoesNotContain(cli.Calls, c => c.Args[0] == "--bg");
        var saved = store.Load()[TestSpecs.PlannerName];
        Assert.Empty(saved.RestartTimes);
        Assert.Null(saved.LastAlert);

        // The reset has passed: the same pass now resumes the session under the same id.
        clock.Now = Now.AddHours(2).AddMinutes(1);
        var resumed = await runner.CheckOnceAsync([Planner]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(resumed));
        Assert.Equal(SessionId, store.Load()[TestSpecs.PlannerName].SessionId);
    }
}

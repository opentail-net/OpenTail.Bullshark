using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Scenario replays (lifecycle batch L4). Each test is a script of passes through <see cref="SupervisorRunner.CheckOnceAsync(IReadOnlyList{SessionSpec}, CancellationToken)"/>:
/// before each pass the test sets the listing that `claude agents --json` would print, and the time. They lock the supervisor's behaviour against
/// regressions. Every id and name is synthetic, and the fake CLI answers every call: nothing starts a real `claude`.
/// </summary>
public sealed class SupervisorScenarioTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private const string OtherSessionId = "99999999-aaaa-bbbb-cccc-dddddddddddd";
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly SessionSpec Spec = TestSpecs.Planner(@"C:\W", "brief");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-scenario-" + Guid.NewGuid().ToString("N")[..8]);

    public SupervisorScenarioTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class SteppingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>The listing with one row of the test planner's name, as `claude agents --json` prints it.</summary>
    private static string Row(string state, string sessionId = SessionId) =>
        $$"""[{"id":"{{sessionId[..8]}}","kind":"background","sessionId":"{{sessionId}}","name":"{{TestSpecs.PlannerName}}","state":"{{state}}","cwd":"C:\\W"}]""";

    /// <summary>A record of the test planner that was started earlier, with its restart times.</summary>
    private static SessionRecord Known(string? lastState = "working", params DateTimeOffset[] restartTimes) =>
        new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, Start.AddHours(-3), restartTimes.Length, restartTimes, lastState, null);

    private StateStore StoreWith(SessionRecord? record)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        if (record is not null) store.Save(new Dictionary<string, SessionRecord> { [TestSpecs.PlannerName] = record });
        return store;
    }

    /// <summary>
    /// One scripted world: the store, the clock, the listing the fake `claude` prints, and the runner. A launch (`--bg`) brings the session back as
    /// working, as the real CLI does (F18); the test then sets the listing for the next pass.
    /// </summary>
    private sealed class World
    {
        private readonly FakeCliRunner _cli;
        private readonly SteppingClock _clock = new(Start);
        private readonly SupervisorRunner _runner;
        private string _listing = "[]";

        public World(StateStore store, DaemonStatus? daemon = null)
        {
            Store = store;
            _cli = new FakeCliRunner(args => args[0] switch
            {
                "agents" => FakeCliRunner.Ok(_listing),
                "--bg" => Launch(),
                _ => FakeCliRunner.Ok(),
            });
            _runner = new SupervisorRunner(new ClaudeCli(_cli), store, _clock, resolveTimeout: TimeSpan.FromSeconds(3)) { Daemon = daemon };
        }

        public StateStore Store { get; }

        /// <summary>The record of the test planner as the state file holds it now.</summary>
        public SessionRecord Saved => Store.Load()[TestSpecs.PlannerName];

        /// <summary>How many launches (starts and resumes) the fake `claude` was asked for.</summary>
        public int Launches => _cli.Calls.Count(c => c.Args[0] == "--bg");

        /// <summary>The CLI brings the session back as working, and prints the short id the start reads back.</summary>
        private CliResult Launch()
        {
            _listing = Row("working");
            return FakeCliRunner.Ok($"backgrounded · {SessionId[..8]} · {TestSpecs.PlannerName}\n");
        }

        /// <summary>Runs one pass at <paramref name="at"/> against <paramref name="listing"/> and returns what it did.</summary>
        public Task<IReadOnlyList<SupervisorAction>> Pass(DateTimeOffset at, string listing)
        {
            _clock.Now = at;
            _listing = listing;
            return _runner.CheckOnceAsync([Spec]);
        }
    }

    [Fact]
    public async Task AFirstStartIsMadeOnceAndTheListedSessionIsRemembered()
    {
        var world = new World(StoreWith(null));

        Assert.IsType<SupervisorAction.Start>(Assert.Single(await world.Pass(Start, "[]")));
        Assert.Equal(SessionId, world.Saved.SessionId);
        Assert.Equal(1, world.Launches);

        // The next pass sees the session working, and starts nothing.
        Assert.Empty(await world.Pass(Start.AddMinutes(1), Row("working")));
        Assert.Equal(1, world.Launches);
    }

    [Fact]
    public async Task ALostRecordIsAdoptedWhenItsNameIsStillListed()
    {
        // No record (it was lost), and the daemon still lists a working session of the name: it is adopted, not started a second time.
        var world = new World(StoreWith(null));

        var adopt = Assert.IsType<SupervisorAction.Adopt>(Assert.Single(await world.Pass(Start, Row("working"))));

        Assert.Equal(SessionId, adopt.SessionId);
        Assert.Equal(SessionId, world.Saved.SessionId);
        Assert.Equal(0, world.Launches);
        Assert.Empty(await world.Pass(Start.AddMinutes(1), Row("working")));
    }

    [Fact]
    public async Task AVanishingSessionIsResumedWithBackoffThenCappedThenResumedAfterTheHour()
    {
        var world = new World(StoreWith(Known()));

        // Minute 0: vanished. The first restart of the hour is immediate.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start, "[]")));

        // Thirty seconds on, it vanishes again: the one-minute gap after that restart holds, silently.
        Assert.Empty(await world.Pass(Start.AddSeconds(30), "[]"));

        // Minute 1: the gap has passed, so the second restart runs.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start.AddMinutes(1), "[]")));

        // Minute 2: the two-minute gap after the second restart holds.
        Assert.Empty(await world.Pass(Start.AddMinutes(2), "[]"));

        // Minute 3: the third restart, the last one the hour allows.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start.AddMinutes(3), "[]")));

        // Minute 4: three restarts in the hour. One restart-loop alert, and nothing more while it stands.
        var loop = Assert.IsType<SupervisorAction.Alert>(Assert.Single(await world.Pass(Start.AddMinutes(4), "[]")));
        Assert.Equal(AlertKind.RestartLoop, loop.Kind);
        Assert.Empty(await world.Pass(Start.AddMinutes(5), "[]"));
        Assert.Empty(await world.Pass(Start.AddMinutes(30), "[]"));
        Assert.Equal(3, world.Launches);

        // Minute 61: the restarts of minutes 0 and 1 are out of the hour (60 minutes is not within it), so one restart is left and the gap has
        // passed. The resume runs again, and clears the alert.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start.AddMinutes(61), "[]")));
        Assert.Null(world.Saved.LastAlert);
        Assert.Equal(4, world.Launches);
    }

    [Fact]
    public async Task AStaleBlockedSessionIsAlertedOnceAndNotResumedWithinTheHour()
    {
        // The daemon is not running, so a listed blocked row is stale: its prompt is lost with the daemon.
        var world = new World(StoreWith(Known()), DaemonStatus.Parse("not running\n"));

        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(await world.Pass(Start, Row("blocked"))));
        Assert.Equal(AlertKind.Blocked, alert.Kind);
        Assert.Equal(AttentionList.StaleBlockedDetail, alert.Detail);
        Assert.Empty(await world.Pass(Start.AddMinutes(1), Row("blocked")));

        // The row leaves the listing. The hold keeps the session from a resume for the hour after its alert.
        Assert.Empty(await world.Pass(Start.AddMinutes(30), "[]"));
        Assert.Equal(0, world.Launches);
        Assert.Equal(AttentionReason.Blocked, Assert.Single(AttentionList.From([world.Saved], [])).Reason);
    }

    [Fact]
    public async Task AStoppedSessionIsAlertedOnceAndNeverBroughtBack()
    {
        var world = new World(StoreWith(Known()));

        Assert.Equal(AlertKind.Stopped, Assert.IsType<SupervisorAction.Alert>(Assert.Single(await world.Pass(Start, Row("stopped")))).Kind);
        Assert.Empty(await world.Pass(Start.AddMinutes(1), Row("stopped")));

        // The owner removes it. The listing no longer shows it, and no later pass brings it back.
        foreach (var minute in new[] { 2, 10, 60 })
            Assert.Empty(await world.Pass(Start.AddMinutes(minute), "[]"));
        Assert.Equal(0, world.Launches);
        Assert.Equal("stopped", world.Saved.LastState);
    }

    [Fact]
    public async Task ANameClashIsAlertedThenTheResumeRunsWhenTheOtherSessionIsDone()
    {
        // The session is lost, and a live session of the same name is listed under another id.
        var world = new World(StoreWith(Known()));

        var clash = Assert.IsType<SupervisorAction.Alert>(Assert.Single(await world.Pass(Start, Row("working", OtherSessionId))));
        Assert.Equal(AlertKind.NameTaken, clash.Kind);
        Assert.Equal(0, world.Launches);

        // The owner sees the lost session with its reason, while the clash stands.
        Assert.Equal(AttentionReason.NameTaken,
            Assert.Single(AttentionList.From([world.Saved], AgentInfo.ParseList(Row("working", OtherSessionId)))).Reason);

        // The next pass does not repeat the alert.
        Assert.Empty(await world.Pass(Start.AddMinutes(1), Row("working", OtherSessionId)));

        // The other session is done: the name is free, and the lost session is resumed under its own id.
        var resume = Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start.AddMinutes(2), Row("done", OtherSessionId))));
        Assert.Equal(SessionId, resume.SessionId);
        Assert.Equal(1, world.Launches);
        Assert.Null(world.Saved.LastAlert);
    }

    [Fact]
    public async Task ADoneSessionIsLeftAloneOverManyPasses()
    {
        var world = new World(StoreWith(Known()));

        foreach (var minute in new[] { 0, 10, 20, 30, 40, 50 })
            Assert.Empty(await world.Pass(Start.AddMinutes(minute), Row("done")));

        Assert.Equal(0, world.Launches);
        Assert.Empty(world.Saved.RestartTimes);
    }

    [Fact]
    public async Task AnOldRestartDoesNotDelayTheFirstRestartOfTheHour()
    {
        // The session was restarted 70 minutes ago, so this hour has no restart yet: the next loss is resumed at once.
        var world = new World(StoreWith(Known("working", Start.AddMinutes(-70))));

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start, "[]")));

        // That restart starts the gap: thirty seconds later the loss waits, and a minute later the resume runs.
        Assert.Empty(await world.Pass(Start.AddSeconds(30), "[]"));
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start.AddMinutes(1), "[]")));
    }

    [Fact]
    public async Task ANameTakenAlertEndsWhenTheSessionIsListedAgainAndALaterLossIsJudgedAfresh()
    {
        var world = new World(StoreWith(Known()));

        Assert.Equal(AlertKind.NameTaken, Assert.IsType<SupervisorAction.Alert>(Assert.Single(await world.Pass(Start, Row("working", OtherSessionId)))).Kind);

        // The owner brings the session back by hand. It is listed, so the name-taken alert no longer stands.
        Assert.Empty(await world.Pass(Start.AddMinutes(1), Row("working")));
        Assert.Null(world.Saved.LastAlert);

        // It is lost again with no other session of the name: the loss is resumed, not held by a stale alert.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await world.Pass(Start.AddMinutes(2), "[]")));
        Assert.Equal(1, world.Launches);
    }
}

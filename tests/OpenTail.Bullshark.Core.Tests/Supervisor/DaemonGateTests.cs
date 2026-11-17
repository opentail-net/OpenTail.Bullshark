using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Round 7 item 1, revised by the planner: "not running" is the daemon's normal idle state (`claude daemon --help`: "the daemon runs on demand
/// and exits once no client is connected and no background session is running"). So it never stops a pass. Starts and resumes go ahead, and
/// `claude --bg` brings the daemon up. What changes: while it is not running, a listed session that claims working or blocked is stale, so it
/// raises no "blocked" alert and is not resumed on that basis alone. A healthy daemon, or one whose status is unknown, changes nothing.
/// </summary>
public sealed class DaemonGateTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-daemon-" + Guid.NewGuid().ToString("N")[..8]);

    public DaemonGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly DaemonStatus NotRunning = DaemonStatus.Parse("not running\n  control.sock: unreachable (connect ENOENT)\n");
    private static readonly DaemonStatus Healthy = DaemonStatus.Parse("running (pid 4312, version 2.1.326, uptime 2h 5m)\n  control.sock: listening\n");
    private static readonly DaemonStatus Garbage = DaemonStatus.Parse("something new");

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The planner listed under its name, in the given state, with the given id.</summary>
    private static string Listing(string? state) => state is null
        ? "[]"
        : $$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"{{state}}","cwd":"C:\\W"}]""";

    /// <summary>
    /// A stand-in for `claude` with a listing that a launch can change: after a `--bg` call the new session is listed, so a first start
    /// can read its id back.
    /// </summary>
    private sealed class FakeClaude
    {
        public string? Listed { get; set; }
        public FakeCliRunner Runner { get; }

        public FakeClaude(string? listedState)
        {
            Listed = Listing(listedState);
            Runner = new FakeCliRunner(args => args[0] switch
            {
                "agents" => FakeCliRunner.Ok(Listed!),
                "logs" => FakeCliRunner.Ok(string.Empty),
                "--bg" => Launched(),
                _ => FakeCliRunner.Ok(),
            });
        }

        private CliResult Launched()
        {
            Listed = """[{"id":"abcdef12","kind":"background","sessionId":"fresh-session","name":"test-planner","state":"working"}]""";
            return FakeCliRunner.Ok("backgrounded · abcdef12 · test-planner\n");
        }
    }

    private string StatePath => Path.Combine(_dir, "state.json");

    /// <summary>The state file as text, so "nothing changed" does not depend on record equality (the lists inside differ by instance).</summary>
    private string StateText() => File.Exists(StatePath) ? File.ReadAllText(StatePath) : string.Empty;

    private StateStore StoreWith(SessionRecord? record)
    {
        var store = new StateStore(StatePath);
        if (record is not null) store.Save(new Dictionary<string, SessionRecord> { [record.Name] = record });
        return store;
    }

    private static SessionRecord RecordOfPlanner(string lastState) =>
        new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], lastState, null);

    private static List<string> LaunchArgs(FakeCliRunner runner) =>
        runner.Calls.Where(c => c.Args[0] == "--bg").SelectMany(c => c.Args).ToList();

    private static SupervisorRunner RunnerFor(FakeClaude claude, StateStore store, DaemonStatus? daemon) =>
        new(new ClaudeCli(claude.Runner), store, new FixedClock(), resolveTimeout: TimeSpan.FromSeconds(1)) { Daemon = daemon };

    [Fact]
    public async Task ADeadDaemonStillStartsANewSession()
    {
        // `claude --bg` brings the daemon up on demand, so a start is not held back by the daemon being idle.
        var claude = new FakeClaude(listedState: null);

        var performed = await RunnerFor(claude, StoreWith(null), NotRunning).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Start>(Assert.Single(performed));
        Assert.Contains("--bg", LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task ADeadDaemonStillResumesAGoneSession()
    {
        var claude = new FakeClaude(listedState: null);

        var performed = await RunnerFor(claude, StoreWith(RecordOfPlanner("working")), NotRunning)
            .CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        Assert.Contains("--resume", LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task ADeadDaemonAlertsOnceForAStaleBlockedSessionAndResumesNothing()
    {
        // The listing says blocked, which a daemon that is not running cannot make true: it is stale. Its prompt is lost, so the owner
        // is told once (round 8 item 1), and nothing is resumed or started.
        var claude = new FakeClaude(listedState: "blocked");
        var store = StoreWith(RecordOfPlanner("working"));
        var spec = TestSpecs.Planner(@"C:\W", "t");

        var first = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([spec]);
        var second = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([spec]);

        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(first));
        Assert.Equal(AlertKind.Blocked, alert.Kind);
        Assert.Equal(AttentionList.StaleBlockedDetail, alert.Detail);
        Assert.Empty(second);
        Assert.Empty(LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task ADeadDaemonDoesNotResumeAStaleBlockedSessionWhenItsRowIsDropped()
    {
        // The roster later drops the row. The session is then "gone", but its alert says it was waiting for input: it is still not resumed.
        var claude = new FakeClaude(listedState: "blocked");
        var store = StoreWith(RecordOfPlanner("working"));
        var spec = TestSpecs.Planner(@"C:\W", "t");
        await RunnerFor(claude, store, NotRunning).CheckOnceAsync([spec]);

        claude.Listed = Listing(null);
        var performed = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([spec]);

        Assert.Empty(performed);
        Assert.DoesNotContain("--resume", LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task ADeadDaemonResumesAStaleWorkingSessionAsALostOne()
    {
        // A working row that the dead daemon left behind is lost: it is resumed under its id, as a session the roster dropped would be.
        var claude = new FakeClaude(listedState: "working");
        var store = StoreWith(RecordOfPlanner("working"));

        var performed = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        Assert.Contains("--resume", LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task ASessionListedLiveAgainClearsItsStaleAlertSoALaterStaleSpellAlertsAgain()
    {
        var claude = new FakeClaude(listedState: "blocked");
        var store = StoreWith(RecordOfPlanner("working"));
        var spec = TestSpecs.Planner(@"C:\W", "t");
        await RunnerFor(claude, store, NotRunning).CheckOnceAsync([spec]);

        claude.Listed = Listing("working");
        var back = await RunnerFor(claude, store, Healthy).CheckOnceAsync([spec]);
        Assert.Empty(back);
        Assert.Null(store.Load().Values.Single().LastAlert);

        claude.Listed = Listing("blocked");
        var again = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([spec]);
        Assert.Equal(AttentionList.StaleBlockedDetail, Assert.IsType<SupervisorAction.Alert>(Assert.Single(again)).Detail);
    }

    [Fact]
    public async Task AnUnreachableDaemonChangesNothing()
    {
        // "unreachable" without a "not running" line is Unknown: the listing is decided on as it is, so a blocked session gets the normal alert.
        var unreachable = DaemonStatus.Parse("daemon: unreachable (connect ENOENT)\n  control.sock: unreachable (connect ENOENT)\n");
        Assert.Equal(DaemonState.Unknown, unreachable.State);

        var withoutCheck = await BlockedPass(daemon: null);
        var pass = await BlockedPass(daemon: unreachable);

        Assert.Equal(withoutCheck.Performed.Select(a => a.GetType()), pass.Performed.Select(a => a.GetType()));
        Assert.Equal(AlertKind.Blocked, Assert.IsType<SupervisorAction.Alert>(Assert.Single(pass.Performed)).Kind);
        Assert.NotEqual(AttentionList.StaleBlockedDetail, Assert.IsType<SupervisorAction.Alert>(Assert.Single(pass.Performed)).Detail);
        Assert.Empty(pass.Args);
    }

    [Fact]
    public async Task AHealthyDaemonRaisesTheBlockedAlertAsItDid()
    {
        var claude = new FakeClaude(listedState: "blocked");

        var performed = await RunnerFor(claude, StoreWith(RecordOfPlanner("working")), Healthy)
            .CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(performed));
        Assert.Equal(AlertKind.Blocked, alert.Kind);
    }

    [Fact]
    public async Task ASessionThatWasStaleIsAlertedWhenTheDaemonReturnsAndItIsStillBlocked()
    {
        // Pass 1 with the daemon idle records the session as stale; pass 2 with the daemon up sees it blocked, which is a new state for the record.
        var claude = new FakeClaude(listedState: "blocked");
        var store = StoreWith(RecordOfPlanner("working"));

        var idle = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);
        var back = await RunnerFor(claude, store, Healthy).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Equal(AttentionList.StaleBlockedDetail, Assert.IsType<SupervisorAction.Alert>(Assert.Single(idle)).Detail);
        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(back));
        Assert.Equal(AlertKind.Blocked, alert.Kind);
    }

    [Fact]
    public async Task AHealthyDaemonResumesAGoneSessionAsItDidBefore()
    {
        var withoutCheck = await GoneSessionPass(daemon: null);
        var healthy = await GoneSessionPass(daemon: Healthy);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(withoutCheck.Performed));
        Assert.Equal(withoutCheck.Performed.Select(a => a.GetType()), healthy.Performed.Select(a => a.GetType()));
        Assert.Equal(withoutCheck.Args, healthy.Args);
    }

    [Fact]
    public async Task AnUnknownDaemonStatusChangesNothing()
    {
        var withoutCheck = await BlockedPass(daemon: null);
        var unknown = await BlockedPass(daemon: Garbage);

        Assert.Equal(withoutCheck.Performed.Select(a => a.GetType()), unknown.Performed.Select(a => a.GetType()));
        Assert.Equal(withoutCheck.Args, unknown.Args);
    }

    [Fact]
    public async Task TheLoopStillReadsSessionOutputWhileTheDaemonIsNotRunning()
    {
        // A listed working session is not stale, so the pass goes on to read its output as it does with the daemon up.
        var claude = new FakeClaude(listedState: "working");
        var store = StoreWith(RecordOfPlanner("working"));
        var loop = new SupervisorLoop(RunnerFor(claude, store, NotRunning), new ClaudeCli(claude.Runner), store, _ => { });

        await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Single(claude.Runner.Calls, c => c.Args[0] == "logs");
    }

    [Fact]
    public async Task TheLoopStartsANewSessionWhileTheDaemonIsNotRunning()
    {
        var claude = new FakeClaude(listedState: null);
        var store = StoreWith(null);
        var loop = new SupervisorLoop(RunnerFor(claude, store, NotRunning), new ClaudeCli(claude.Runner), store, _ => { });

        var count = await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Equal(1, count);
        Assert.Contains("--bg", LaunchArgs(claude.Runner));
    }

    /// <summary>A pass for a planner whose session is gone from the listing, under the given daemon status.</summary>
    private async Task<(IReadOnlyList<SupervisorAction> Performed, List<string> Args)> GoneSessionPass(DaemonStatus? daemon)
    {
        var claude = new FakeClaude(listedState: null);
        var performed = await RunnerFor(claude, StoreWith(RecordOfPlanner("working")), daemon).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);
        return (performed, LaunchArgs(claude.Runner));
    }

    /// <summary>A pass for a planner listed as blocked, under the given daemon status.</summary>
    private async Task<(IReadOnlyList<SupervisorAction> Performed, List<string> Args)> BlockedPass(DaemonStatus? daemon)
    {
        var claude = new FakeClaude(listedState: "blocked");
        var performed = await RunnerFor(claude, StoreWith(RecordOfPlanner("working")), daemon).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);
        return (performed, LaunchArgs(claude.Runner));
    }
}

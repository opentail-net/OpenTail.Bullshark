using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Round 7 item 1: when the background daemon is not running, or its control socket is unreachable, a pass makes no start, no resume and
/// no adoption, and records nothing from the listing. A healthy daemon, or one whose status is unknown, changes nothing.
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
    private static readonly DaemonStatus ControlDown = DaemonStatus.Parse("running (pid 4312)\n  control.sock: unreachable (connect ENOENT)\n");
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

    private static SupervisorRunner RunnerFor(FakeClaude claude, StateStore store, DaemonStatus? daemon)
    {
        var runner = new SupervisorRunner(new ClaudeCli(claude.Runner), store, new FixedClock(), resolveTimeout: TimeSpan.FromSeconds(1))
        {
            Daemon = daemon,
        };
        return runner;
    }

    [Fact]
    public async Task ADeadDaemonStartsNothingForAFirstStart()
    {
        var claude = new FakeClaude(listedState: null);
        var store = StoreWith(null);

        var performed = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Empty(performed);
        Assert.Empty(LaunchArgs(claude.Runner));
        Assert.Empty(store.Load());
    }

    [Fact]
    public async Task ADeadDaemonResumesNothingAGoneSession()
    {
        var claude = new FakeClaude(listedState: null);
        var store = StoreWith(RecordOfPlanner("working"));
        var before = StateText();

        var performed = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Empty(performed);
        Assert.DoesNotContain("--resume", LaunchArgs(claude.Runner));
        Assert.Empty(LaunchArgs(claude.Runner));
        Assert.Equal(before, StateText());
    }

    [Fact]
    public async Task ADeadDaemonRecordsNothingFromItsListing()
    {
        // The listing says blocked, which a dead daemon cannot be: no alert, and the remembered state is not changed.
        var claude = new FakeClaude(listedState: "blocked");
        var store = StoreWith(RecordOfPlanner("working"));
        var before = StateText();

        var performed = await RunnerFor(claude, store, NotRunning).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Empty(performed);
        Assert.Equal(before, StateText());
        Assert.DoesNotContain(claude.Runner.Calls, c => c.Args[0] == "logs");
    }

    [Fact]
    public async Task AnUnreachableControlSocketMeansTheSameAsADeadDaemon()
    {
        var claude = new FakeClaude(listedState: null);
        var store = StoreWith(RecordOfPlanner("working"));

        var performed = await RunnerFor(claude, store, ControlDown).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Empty(performed);
        Assert.Empty(LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task AHealthyDaemonResumesAGoneSessionAsItDidBefore()
    {
        var withoutCheck = await GoneSessionPass(daemon: null);
        var healthy = await GoneSessionPass(daemon: Healthy);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(withoutCheck.Performed));
        Assert.Equal(withoutCheck.Performed.Select(a => a.GetType()), healthy.Performed.Select(a => a.GetType()));
        Assert.Contains("--resume", withoutCheck.Args);
        Assert.Equal(withoutCheck.Args, healthy.Args);
    }

    [Fact]
    public async Task AHealthyDaemonStartsAFirstSessionAsItDidBefore()
    {
        var claude = new FakeClaude(listedState: null);
        var performed = await RunnerFor(claude, StoreWith(null), Healthy).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Start>(Assert.Single(performed));
        Assert.Contains("--bg", LaunchArgs(claude.Runner));
    }

    [Fact]
    public async Task AnUnknownDaemonStatusChangesNothing()
    {
        var withoutCheck = await GoneSessionPass(daemon: null);
        var unknown = await GoneSessionPass(daemon: Garbage);

        Assert.Equal(withoutCheck.Args, unknown.Args);
        Assert.Equal(withoutCheck.Performed.Select(a => a.GetType()), unknown.Performed.Select(a => a.GetType()));
    }

    [Fact]
    public async Task TheLoopReadsNoSessionOutputWhileTheDaemonIsDown()
    {
        var claude = new FakeClaude(listedState: "working");
        var store = StoreWith(RecordOfPlanner("working"));
        var runner = RunnerFor(claude, store, NotRunning);
        var loop = new SupervisorLoop(runner, new ClaudeCli(claude.Runner), store, _ => { });

        var count = await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Equal(0, count);
        Assert.DoesNotContain(claude.Runner.Calls, c => c.Args[0] == "logs");
    }

    [Fact]
    public async Task ADeadDaemonMakesThePassOnTheWindowsListingDecideNothing()
    {
        // The window passes the listing it already has; the same rule applies on that path.
        var claude = new FakeClaude(listedState: "blocked");
        var store = StoreWith(RecordOfPlanner("working"));
        var before = StateText();
        var runner = RunnerFor(claude, store, NotRunning);
        var loop = new SupervisorLoop(runner, new ClaudeCli(claude.Runner), store, _ => { });

        var count = await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")], AgentInfo.ParseList(Listing("blocked")));

        // Checked on the runner's own pass too: it decides nothing, so no alert is raised and the state is not written.
        var performed = await runner.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")], AgentInfo.ParseList(Listing("blocked")));
        Assert.Empty(performed);
        Assert.Equal(0, count);
        Assert.Empty(LaunchArgs(claude.Runner));
        Assert.Equal(before, StateText());
    }

    /// <summary>A pass for a planner whose session is gone from the listing, under the given daemon status.</summary>
    private async Task<(IReadOnlyList<SupervisorAction> Performed, List<string> Args)> GoneSessionPass(DaemonStatus? daemon)
    {
        var claude = new FakeClaude(listedState: null);
        var store = StoreWith(RecordOfPlanner("working"));
        var performed = await RunnerFor(claude, store, daemon).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);
        return (performed, LaunchArgs(claude.Runner).ToList());
    }
}

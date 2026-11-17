using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// A pass lists the agents once. Each listing is a new `claude` process, so a pass that lists twice pays for two. These tests
/// count the `agents` calls the fake CLI receives.
/// </summary>
public sealed class AgentsOncePerPassTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-once-" + Guid.NewGuid().ToString("N")[..8]);

    public AgentsOncePerPassTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The listing as `claude agents --json` prints it: the planner, in the given state.</summary>
    private static string Listing(string state) =>
        $$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"{{state}}","cwd":"C:\\W"}]""";

    /// <summary>A stand-in for `claude` that lists the planner in one state and prints no logs.</summary>
    private static FakeCliRunner Cli(string state) => new(args => args[0] switch
    {
        "agents" => FakeCliRunner.Ok(Listing(state)),
        "logs" => FakeCliRunner.Ok(string.Empty),
        _ => FakeCliRunner.Ok(),
    });

    private StateStore StoreWith(SessionRecord? record)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        if (record is not null) store.Save(new Dictionary<string, SessionRecord> { [record.Name] = record });
        return store;
    }

    private static SessionRecord RecordOfPlanner(string lastState) =>
        new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], lastState, null);

    private static int AgentsCalls(FakeCliRunner runner) => runner.Calls.Count(c => c.Args[0] == "agents");

    [Fact]
    public async Task AnAdoptionPassWithTheListingMakesNoAgentsCall()
    {
        var runner = Cli("working");
        var runnerForPass = new SupervisorRunner(new ClaudeCli(runner), StoreWith(null), new FixedClock());
        var listing = AgentInfo.ParseList(Listing("working"));

        var performed = await runnerForPass.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")], listing);

        // The planner is listed under its name with no record, so the pass adopts it. Nothing is listed again.
        Assert.IsType<SupervisorAction.Adopt>(Assert.Single(performed));
        Assert.Equal(0, AgentsCalls(runner));
    }

    [Fact]
    public async Task AnAlertPassWithTheListingMakesNoAgentsCall()
    {
        var runner = Cli("blocked");
        var runnerForPass = new SupervisorRunner(new ClaudeCli(runner), StoreWith(RecordOfPlanner("working")), new FixedClock());
        var listing = AgentInfo.ParseList(Listing("blocked"));

        var performed = await runnerForPass.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")], listing);

        Assert.Equal(AlertKind.Blocked, Assert.IsType<SupervisorAction.Alert>(Assert.Single(performed)).Kind);
        Assert.Equal(0, AgentsCalls(runner));
    }

    [Fact]
    public async Task TheOldPassSignatureListsExactlyOnce()
    {
        var runner = Cli("working");
        var runnerForPass = new SupervisorRunner(new ClaudeCli(runner), StoreWith(null), new FixedClock());

        var performed = await runnerForPass.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Adopt>(Assert.Single(performed));
        Assert.Equal(1, AgentsCalls(runner));
    }

    [Fact]
    public async Task ASupervisorLoopPassWithTheListingMakesNoAgentsCall()
    {
        var runner = Cli("working");
        var cli = new ClaudeCli(runner);
        var store = StoreWith(RecordOfPlanner("working"));
        var loop = new SupervisorLoop(new SupervisorRunner(cli, store, new FixedClock()), cli, store, _ => { });
        var listing = AgentInfo.ParseList(Listing("working"));

        var count = await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")], listing);

        // The loop still reads the session's logs for the login alert: that is one `logs` call, and no listing.
        Assert.Equal(0, count);
        Assert.Equal(0, AgentsCalls(runner));
        Assert.Single(runner.Calls, c => c.Args[0] == "logs");
    }

    [Fact]
    public async Task TheOldLoopSignatureListsExactlyOnce()
    {
        var runner = Cli("working");
        var cli = new ClaudeCli(runner);
        var store = StoreWith(RecordOfPlanner("working"));
        var loop = new SupervisorLoop(new SupervisorRunner(cli, store, new FixedClock()), cli, store, _ => { });

        await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Equal(1, AgentsCalls(runner));
    }
}

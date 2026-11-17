using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class SupervisorLoopTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-loop-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    }

    private static FakeCliRunner Cli(string logsText) => new(args => args[0] switch
    {
        "agents" => FakeCliRunner.Ok($$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"working"}]"""),
        "logs" => FakeCliRunner.Ok(logsText),
        _ => FakeCliRunner.Ok(),
    });

    private (SupervisorLoop Loop, StateStore Store, List<string> Logged) Build(FakeCliRunner runner)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        // A session already recorded, so the pass only checks it.
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], null, null),
        });
        var cli = new ClaudeCli(runner);
        var logged = new List<string>();
        var runnerForPass = new SupervisorRunner(cli, store, new FixedClock());
        return (new SupervisorLoop(runnerForPass, cli, store, logged.Add), store, logged);
    }

    [Fact]
    public async Task AnExpiringLoginRaisesALoginAlert()
    {
        var (loop, _, logged) = Build(Cli("Your login expires in 1 day · run /login to renew"));

        await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Single(logged, l => l.Contains("LoginExpiring") && l.Contains("/login"));
    }

    [Fact]
    public async Task AFarOffLoginIsNotAnAlert()
    {
        var (loop, _, logged) = Build(Cli("Your login expires in 5 days"));

        await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.DoesNotContain(logged, l => l.Contains("LoginExpiring"));
    }

    [Fact]
    public async Task ASessionWithNoLogsIsReportedNotFatal()
    {
        var runner = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok($$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"done"}]"""),
            "logs" => FakeCliRunner.Fail("job not found"),
            _ => FakeCliRunner.Ok(),
        });
        var (loop, _, logged) = Build(runner);

        var count = await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Equal(0, count);
        Assert.Single(logged, l => l.Contains("not readable"));
    }

    [Fact]
    public async Task ThePassListsWithTheCompletedSessions()
    {
        var runner = Cli(string.Empty);
        var (loop, _, _) = Build(runner);

        await loop.RunPassAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Contains("--all", runner.Calls.Single(c => c.Args[0] == "agents").Args);
    }

    [Fact]
    public void TheShortIdIsTheFirstEightCharacters()
    {
        Assert.Equal("4431d697", ClaudeCli.ShortIdOf("4431d697-aa2c-4f96-8982-159d6cf42f74"));
    }
}

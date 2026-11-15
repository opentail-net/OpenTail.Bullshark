using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// A failed start or resume is recorded and reported. It does not lose the other sessions' records, and it is not
/// retried on every pass.
/// </summary>
public sealed class SupervisorFailureTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-failure-" + Guid.NewGuid().ToString("N")[..8]);

    public SupervisorFailureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Agent(string sessionId, string name) =>
        $"{{\"id\":\"{sessionId[..8]}\",\"kind\":\"background\",\"sessionId\":\"{sessionId}\",\"name\":\"{name}\",\"state\":\"working\"}}";

    [Fact]
    public async Task ASecondStartThatFailsKeepsTheFirstRecordAndIsNotRetriedOnTheNextPass()
    {
        var path = Path.Combine(_dir, "state.json");
        var launched = new List<(string SessionId, string Name)>();
        var cli = new FakeCliRunner(args =>
        {
            switch (args[0])
            {
                case "agents":
                    return FakeCliRunner.Ok("[" + string.Join(",", launched.Select(l => Agent(l.SessionId, l.Name))) + "]");
                case "--bg":
                    var list = args.ToList();
                    var name = list[list.IndexOf("--name") + 1];
                    // The second coder's start is refused, as a model or effort the account does not accept would be.
                    if (name.EndsWith("-2", StringComparison.Ordinal))
                        return FakeCliRunner.Fail("model not available");
                    var sessionId = Guid.NewGuid().ToString();
                    launched.Add((sessionId, name));
                    return FakeCliRunner.Ok($"backgrounded · {sessionId[..8]} · {name}\n");
                default:
                    return FakeCliRunner.Ok();
            }
        });
        var specs = RunPlan.Build(PatternSpec.Find("plan-many")!, @"C:\W", "task", "ab12", coders: 2);
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), new FixedClock(),
            resolveTimeout: TimeSpan.FromSeconds(3));

        var first = await runner.CheckOnceAsync(specs);

        Assert.Contains(first, a => a is SupervisorAction.Alert { Kind: AlertKind.StartFailed });
        var saved = new StateStore(path).Load();
        Assert.Equal(3, saved.Count);
        Assert.NotEmpty(saved["bullshark-coder-ab12-1"].SessionId);
        Assert.Equal(string.Empty, saved["bullshark-coder-ab12-2"].SessionId);
        Assert.Contains("model not available", saved["bullshark-coder-ab12-2"].LastAlert);

        int startsBefore = cli.Calls.Count(c => c.Args[0] == "--bg");
        var second = await runner.CheckOnceAsync(specs);

        Assert.Empty(second);
        Assert.Equal(startsBefore, cli.Calls.Count(c => c.Args[0] == "--bg"));
    }

    [Fact]
    public async Task FailedResumesCountAsRestartsAndStopAtTheRestartLoop()
    {
        var path = Path.Combine(_dir, "state.json");
        const string planner = "11111111-2222-3333-4444-555555555555";
        bool listed = true, refuse = false;
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok(listed ? "[" + Agent(planner, "ovc-planner") + "]" : "[]"),
            "--bg" => refuse
                ? FakeCliRunner.Fail("login expired")
                : FakeCliRunner.Ok("backgrounded · 11111111 · ovc-planner\n"),
            _ => FakeCliRunner.Ok(),
        });
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), new FixedClock(),
            resolveTimeout: TimeSpan.FromSeconds(3));
        var spec = SessionSpec.DefaultPlanner(@"C:\W", "task");

        // The first pass starts the planner. Then it disappears, and every resume is refused.
        await runner.CheckOnceAsync([spec]);
        listed = false;
        refuse = true;

        for (int pass = 1; pass <= 3; pass++)
        {
            var actions = await runner.CheckOnceAsync([spec]);
            Assert.IsType<SupervisorAction.Alert>(Assert.Single(actions));
        }
        Assert.Equal(3, new StateStore(path).Load()[SessionSpec.PlannerName].Restarts);

        // The fourth pass does not try again: three failures in the hour raise the restart-loop alert.
        int startsBefore = cli.Calls.Count(c => c.Args[0] == "--bg");
        var stopped = await runner.CheckOnceAsync([spec]);

        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(stopped));
        Assert.Equal(AlertKind.RestartLoop, alert.Kind);
        Assert.Equal(startsBefore, cli.Calls.Count(c => c.Args[0] == "--bg"));
    }
}

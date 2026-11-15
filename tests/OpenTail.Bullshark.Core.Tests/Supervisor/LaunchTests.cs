using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The launch path from a run (S5), against the fake CLI: a plan &gt; code run starts two sessions with unique names, a
/// many-coder run starts one planner and N coders, and a restarted window reads the same specs back from runs.json.
/// The live window path is not run here (it would start real sessions); it is pending the owner.
/// </summary>
public sealed class LaunchTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-launch-" + Guid.NewGuid().ToString("N")[..8]);

    public LaunchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A fake `claude` that starts every `--bg` and lists every session it started.</summary>
    private static (FakeCliRunner Cli, List<(string SessionId, string Name)> Launched) Fleet()
    {
        var launched = new List<(string SessionId, string Name)>();
        var cli = new FakeCliRunner(args =>
        {
            switch (args[0])
            {
                case "agents":
                    var items = launched.Select(l =>
                        $"{{\"id\":\"{l.SessionId[..8]}\",\"kind\":\"background\",\"sessionId\":\"{l.SessionId}\",\"name\":\"{l.Name}\",\"state\":\"working\"}}");
                    return FakeCliRunner.Ok("[" + string.Join(",", items) + "]");
                case "--bg":
                    var list = args.ToList();
                    var name = list[list.IndexOf("--name") + 1];
                    var sessionId = Guid.NewGuid().ToString();
                    launched.Add((sessionId, name));
                    return FakeCliRunner.Ok($"backgrounded · {sessionId[..8]} · {name}\n");
                default:
                    return FakeCliRunner.Ok();
            }
        });
        return (cli, launched);
    }

    private static SetupRequest Request(string folder, string pattern, int coders) =>
        new(pattern, folder, "Build it", coders, "opus", "high", "haiku", "medium");

    [Fact]
    public async Task APlanAndCodeRunStartsTwoSessionsWithUniqueNames()
    {
        var run = SetupPlan.CreateRun(Request(_dir, "plan-code", 1), [], Now);
        var (cli, launched) = Fleet();
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(Path.Combine(_dir, "state.json")),
            new FixedClock(), resolveTimeout: TimeSpan.FromSeconds(3));

        var performed = await runner.CheckOnceAsync(run.Specs);

        Assert.Equal(2, performed.Count);
        Assert.All(performed, a => Assert.IsType<SupervisorAction.Start>(a));
        var names = launched.Select(l => l.Name).ToList();
        Assert.Equal(2, names.Distinct().Count());
        Assert.Equal(run.Specs.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal),
            names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AManyCoderRunStartsOnePlannerAndNCodersWithUniqueNames()
    {
        var run = SetupPlan.CreateRun(Request(_dir, "plan-many", 4), [], Now);
        var (cli, launched) = Fleet();
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(Path.Combine(_dir, "state.json")),
            new FixedClock(), resolveTimeout: TimeSpan.FromSeconds(3));

        var performed = await runner.CheckOnceAsync(run.Specs);

        Assert.Equal(5, performed.Count);
        Assert.Equal(5, launched.Select(l => l.Name).Distinct().Count());
        Assert.Equal(1, launched.Count(l => l.Name.StartsWith("bullshark-planner-", StringComparison.Ordinal)));
        Assert.Equal(4, launched.Count(l => l.Name.StartsWith("bullshark-coder-", StringComparison.Ordinal)));
    }

    [Fact]
    public void ARestartedWindowReadsTheSameSpecsBack()
    {
        var path = Path.Combine(_dir, "runs.json");
        var run = SetupPlan.CreateRun(Request(_dir, "plan-many", 2), [], Now);
        new RunStore(path).Save([run]);

        // A new window reads the file it finds. The specs must match the ones that were started.
        var restored = RunStore.SpecsOf(new RunStore(path).Load());

        Assert.Equal(run.Specs.Count, restored.Count);
        for (int i = 0; i < restored.Count; i++)
        {
            Assert.Equal(run.Specs[i].Name, restored[i].Name);
            Assert.Equal(run.Specs[i].Brief, restored[i].Brief);
            Assert.Equal(run.Specs[i].Model, restored[i].Model);
            Assert.Equal(run.Specs[i].PermissionMode, restored[i].PermissionMode);
        }
    }

    [Fact]
    public async Task ARestartedWindowDoesNotStartTheRunAgain()
    {
        var path = Path.Combine(_dir, "state.json");
        var run = SetupPlan.CreateRun(Request(_dir, "plan-code", 1), [], Now);
        var (cli, _) = Fleet();
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), new FixedClock(),
            resolveTimeout: TimeSpan.FromSeconds(3));
        await runner.CheckOnceAsync(run.Specs);

        // The window restarts: a new runner reads the same state file and the same runs.
        var (secondCli, _) = Fleet();
        var restarted = new SupervisorRunner(new ClaudeCli(secondCli), new StateStore(path), new FixedClock(),
            resolveTimeout: TimeSpan.FromSeconds(3));
        var performed = await restarted.CheckOnceAsync(run.Specs);

        // The fresh fake lists no session, so the records lead to resumes, never to a second start under the same names.
        Assert.DoesNotContain(performed, a => a is SupervisorAction.Start);
    }
}

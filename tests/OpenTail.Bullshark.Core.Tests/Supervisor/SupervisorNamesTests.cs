using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The name key against the real file shape: an existing pair is not orphaned, two coders start on their own
/// records, and the records of other runs survive a pass.
/// </summary>
public sealed class SupervisorNamesTests : IDisposable
{
    private const string PlannerSession = "11111111-aaaa-4bbb-8ccc-000000000001";
    private const string CoderSession = "22222222-aaaa-4bbb-8ccc-000000000002";
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-names-" + Guid.NewGuid().ToString("N")[..8]);

    public SupervisorNamesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Agent(string sessionId, string name, string state) =>
        $$"""{"id":"{{sessionId[..8]}}","kind":"background","sessionId":"{{sessionId}}","name":"{{name}}","state":"{{state}}"}""";

    /// <summary>The state file as it was before the name key: a JSON list with Role and Name in every record.</summary>
    private void WriteLegacyFile(string path) => File.WriteAllText(path, $$"""
        [
          {"Role":"Planner","SessionId":"{{PlannerSession}}","Name":"ovc-planner","StartedAt":"2026-11-14T18:20:47+00:00","Restarts":0,"RestartTimes":[],"LastState":"blocked","LastAlert":"Blocked: waiting"},
          {"Role":"Coder","SessionId":"{{CoderSession}}","Name":"ovc-coder","StartedAt":"2026-11-14T18:20:47+00:00","Restarts":0,"RestartTimes":[],"LastState":"done","LastAlert":null}
        ]
        """);

    [Fact]
    public async Task TheExistingPairIsNotOrphanedByTheNameKey()
    {
        var path = Path.Combine(_dir, "state.json");
        WriteLegacyFile(path);
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok($"[{Agent(PlannerSession, "ovc-planner", "blocked")},{Agent(CoderSession, "ovc-coder", "done")}]"),
            _ => FakeCliRunner.Ok(),
        });
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), new FixedClock());

        var performed = await runner.CheckOnceAsync(RunPlan.Legacy(@"C:\W", "task"));

        Assert.Empty(performed);
        Assert.DoesNotContain(cli.Calls, c => c.Args[0] == "--bg");
    }

    [Fact]
    public async Task TwoCodersOfOneRunStartWithTheirOwnRecords()
    {
        var path = Path.Combine(_dir, "state.json");
        var launched = new List<(string SessionId, string Name)>();
        var cli = new FakeCliRunner(args =>
        {
            switch (args[0])
            {
                case "agents":
                    var items = launched.Select(l => Agent(l.SessionId, l.Name, "working"));
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
        var specs = RunPlan.Build(PatternSpec.Find("plan-many")!, @"C:\W", "task", "ab12", coders: 2);
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), new FixedClock(),
            resolveTimeout: TimeSpan.FromSeconds(3));

        var performed = await runner.CheckOnceAsync(specs);

        Assert.Equal(3, performed.Count);
        var saved = new StateStore(path).Load();
        Assert.Equal(new[] { "bullshark-coder-ab12-1", "bullshark-coder-ab12-2", "bullshark-planner-ab12" }, saved.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(3, saved.Values.Select(r => r.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task RecordsOfOtherRunsSurviveAPass()
    {
        var path = Path.Combine(_dir, "state.json");
        WriteLegacyFile(path);
        var otherRun = new SessionRecord(SessionRole.Coder, "99999999-aaaa-4bbb-8ccc-000000000009", "bullshark-coder-zz99",
            Now.AddHours(-1), 0, [], "working", null);
        var store = new StateStore(path);
        store.Save(new Dictionary<string, SessionRecord>(store.Load(), StringComparer.Ordinal)
        {
            ["bullshark-coder-zz99"] = otherRun,
        });

        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok($"[{Agent(PlannerSession, "ovc-planner", "done")}]"),
            _ => FakeCliRunner.Ok(),
        });
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), new FixedClock());

        // This pass checks the planner only. The other run's coder is not in the spec list.
        await runner.CheckOnceAsync([SessionSpec.DefaultPlanner(@"C:\W", "task")]);

        var after = new StateStore(path).Load();
        Assert.True(after.ContainsKey("bullshark-coder-zz99"));
        Assert.Equal(otherRun.SessionId, after["bullshark-coder-zz99"].SessionId);
    }
}

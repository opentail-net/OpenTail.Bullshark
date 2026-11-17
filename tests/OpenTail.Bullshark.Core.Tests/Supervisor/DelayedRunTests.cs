using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// A run with a start time (gems item 5): a run that waits has no specs in a pass, a run whose time has come starts, starting it
/// now clears only its own time, and runs.json from before the field still loads.
/// </summary>
public sealed class DelayedRunTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-delayed-" + Guid.NewGuid().ToString("N")[..8]);

    public DelayedRunTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SessionSpec Coder(string runId) =>
        new(SessionRole.Coder, $"bullshark-coder-{runId}", @"C:\work", "haiku", "medium", "brief", "auto");

    private static RunRecord Run(string runId, DateTimeOffset? startAt = null) =>
        new(runId, "solo", @"C:\work", "auto", Now.AddHours(-1), [Coder(runId)], startAt);

    [Fact]
    public void ARunThatStartsLaterHasNoSpecsInThePassAndADueOneDoes()
    {
        var runs = new[]
        {
            Run("aaaa", startAt: Now.AddHours(2)),
            Run("bbbb", startAt: Now.AddMinutes(-5)),
            Run("cccc"),
        };

        var specs = RunStore.DueSpecsOf(runs, Now);

        Assert.DoesNotContain(specs, s => s.Name == "bullshark-coder-aaaa");
        Assert.Contains(specs, s => s.Name == "bullshark-coder-bbbb");
        Assert.Contains(specs, s => s.Name == "bullshark-coder-cccc");
        // The rail still shows the waiting run: it is in the full list of specs.
        Assert.Contains(RunStore.SpecsOf(runs), s => s.Name == "bullshark-coder-aaaa");
    }

    [Fact]
    public void AStartTimeThatIsExactlyNowStartsTheRun()
    {
        Assert.False(RunStore.IsWaiting(Run("aaaa", startAt: Now), Now));
        Assert.True(RunStore.IsWaiting(Run("aaaa", startAt: Now.AddSeconds(1)), Now));
    }

    [Fact]
    public async Task ASessionOfAWaitingRunIsNotStartedByThePass()
    {
        var path = Path.Combine(_dir, "state.json");
        var cli = new FakeCliRunner();
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(path), TimeProvider.System);
        var waiting = RunStore.DueSpecsOf([Run("aaaa", startAt: Now.AddHours(2))], Now);

        var actions = await runner.CheckOnceAsync(waiting, agents: []);

        Assert.Empty(actions);
        Assert.Empty(cli.Calls);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ASessionOfADueRunIsStarted()
    {
        var cli = new FakeCliRunner();
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(Path.Combine(_dir, "state.json")), TimeProvider.System,
            resolveTimeout: TimeSpan.FromMilliseconds(1));
        var due = RunStore.DueSpecsOf([Run("bbbb", startAt: Now.AddMinutes(-5))], Now);

        await runner.CheckOnceAsync(due, agents: []);

        // The launch was attempted, so the due run's session was asked to start.
        Assert.Contains(cli.Calls, call => call.Args.Contains("--bg") && call.Args.Contains("bullshark-coder-bbbb"));
    }

    [Fact]
    public void StartingNowClearsOnlyTheChosenRunsTime()
    {
        var runs = new[] { Run("aaaa", startAt: Now.AddHours(2)), Run("bbbb", startAt: Now.AddHours(3)) };

        var after = RunStore.StartingNow(runs, "aaaa");

        Assert.Null(after.Single(r => r.RunId == "aaaa").StartAt);
        Assert.Equal(Now.AddHours(3), after.Single(r => r.RunId == "bbbb").StartAt);
        // Only the started run is due; the other still waits three hours.
        Assert.Equal(["bullshark-coder-aaaa"], RunStore.DueSpecsOf(after, Now).Select(s => s.Name));
    }

    [Fact]
    public void ARunWithItsStartTimeSurvivesAFileRoundTrip()
    {
        var store = new RunStore(Path.Combine(_dir, "runs.json"));
        store.Save([Run("aaaa", startAt: Now.AddHours(2))]);

        var loaded = Assert.Single(store.Load());
        Assert.Equal(Now.AddHours(2), loaded.StartAt);
    }

    [Fact]
    public void RunsJsonFromBeforeTheStartTimeLoadsAsStartingAtOnce()
    {
        // The shape the file had before gems item 5: no StartAt property anywhere.
        var path = Path.Combine(_dir, "runs.json");
        File.WriteAllText(path, """
            [{"RunId":"abcd","PatternKey":"solo","WorkingDirectory":"C:\\work","PermissionMode":"auto",
              "CreatedAt":"2026-11-01T09:00:00+00:00",
              "Specs":[{"Role":"Planner","Name":"bullshark-planner-abcd","WorkingDirectory":"C:\\work","Model":"opus",
                        "Effort":"high","Brief":"b","PermissionMode":"auto","ResumePrompt":"r"}]}]
            """);

        var loaded = Assert.Single(new RunStore(path).Load());

        Assert.Null(loaded.StartAt);
        Assert.Single(loaded.Specs);
        Assert.False(RunStore.IsWaiting(loaded, Now));
    }
}

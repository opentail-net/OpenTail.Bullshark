using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Supervision passes against a scripted `claude`. The fake behaves like the real CLI: a new background session
/// only appears in `agents --json` after its launch (F18: the session id is assigned by `--bg`).
/// </summary>
public sealed class SupervisorRunnerTests : IDisposable
{
    private const string PlannerSession = "11111111-2222-3333-4444-555555555555";
    private const string ShortId = "abcdef12";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-core-" + Guid.NewGuid().ToString("N")[..8]);

    public SupervisorRunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Listing(string sessionId, string state) =>
        $$"""[{"id":"{{ShortId}}","kind":"background","sessionId":"{{sessionId}}","name":"planner","state":"{{state}}","cwd":"C:\\W"}]""";

    /// <summary>A launch makes the session appear in the listing, as the real CLI does.</summary>
    private static FakeCliRunner LaunchingCli(string sessionId, string state, bool appearsOnlyAfterLaunch = true)
    {
        var launched = !appearsOnlyAfterLaunch;
        return new FakeCliRunner(args =>
        {
            switch (args[0])
            {
                case "agents":
                    return FakeCliRunner.Ok(launched ? Listing(sessionId, state) : "[]");
                case "--bg":
                    launched = true;
                    return FakeCliRunner.Ok($"backgrounded · {ShortId} · planner\n");
                default:
                    return FakeCliRunner.Ok();
            }
        });
    }

    private SupervisorRunner Runner(ClaudeCli cli, StateStore store, FakeClock clock, Action<string>? log = null) =>
        new(cli, store, clock, resolveTimeout: TimeSpan.FromSeconds(3), log: log);

    [Fact]
    public async Task FirstPassStartsThePlannerAndRecordsTheSessionIdItWasGiven()
    {
        var runner = LaunchingCli(PlannerSession, "working");
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        var spec = TestSpecs.Planner(@"C:\W", "Read the README and write the first batch.");
        var performed = await Runner(new ClaudeCli(runner), store, clock).CheckOnceAsync([spec]);

        Assert.IsType<SupervisorAction.Start>(Assert.Single(performed));
        var launch = runner.Calls.Single(c => c.Args[0] == "--bg").Args;
        Assert.DoesNotContain("--session-id", launch);
        Assert.Equal(spec.Brief, launch[^1]);
        Assert.Contains("Read the README and write the first batch.", launch[^1]);
        Assert.Equal(@"C:\W", runner.Calls.Single(c => c.Args[0] == "--bg").WorkingDirectory);

        var saved = store.Load()[TestSpecs.PlannerName];
        Assert.Equal(PlannerSession, saved.SessionId);
        Assert.Equal(0, saved.Restarts);
    }

    [Fact]
    public async Task AMissingPlannerIsResumedAndTheRestartIsCounted()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var spec = TestSpecs.Planner(@"C:\W", "brief");

        await Runner(new ClaudeCli(LaunchingCli(PlannerSession, "working")), store, clock).CheckOnceAsync([spec]);

        // The daemon has lost the session: the listing is empty.
        clock.Now = clock.Now.AddMinutes(5);
        var lost = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Ok($"backgrounded · {ShortId} · planner\n"),
            _ => FakeCliRunner.Ok(),
        });
        var performed = await Runner(new ClaudeCli(lost), store, clock).CheckOnceAsync([spec]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        var resumeArgs = lost.Calls.Single(c => c.Args[0] == "--bg").Args;
        Assert.Equal(PlannerSession, resumeArgs[Array.IndexOf(resumeArgs.ToArray(), "--resume") + 1]);
        // A resume must carry a prompt, or the session sits idle and is reported as blocked (F19).
        Assert.Equal(spec.ResumePrompt, resumeArgs[^1]);

        var saved = store.Load()[TestSpecs.PlannerName];
        Assert.Equal(1, saved.Restarts);
        Assert.Equal(PlannerSession, saved.SessionId);
    }

    [Fact]
    public async Task ABlockedSessionIsReportedOnceAndTheStateIsRemembered()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var spec = TestSpecs.Planner(@"C:\W", "brief");
        var logged = new List<string>();

        await Runner(new ClaudeCli(LaunchingCli(PlannerSession, "working")), store, clock).CheckOnceAsync([spec]);

        var blocked = new FakeCliRunner(_ => FakeCliRunner.Ok(Listing(PlannerSession, "blocked")));
        var runner = Runner(new ClaudeCli(blocked), store, clock, logged.Add);

        var firstPass = await runner.CheckOnceAsync([spec]);
        var secondPass = await runner.CheckOnceAsync([spec]);

        Assert.Equal(AlertKind.Blocked, Assert.IsType<SupervisorAction.Alert>(Assert.Single(firstPass)).Kind);
        Assert.Empty(secondPass);
        Assert.Single(logged, l => l.Contains("ALERT"));
        Assert.Equal("blocked", store.Load()[TestSpecs.PlannerName].LastState);
    }

    [Fact]
    public async Task ASessionThatFinishedIsNotResumedBecauseThePassSeesItInTheCompletedListing()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, PlannerSession, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], null, null),
        });
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        // The active listing is empty, because the session has finished. Only the completed listing shows it, as done.
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok(args.Contains("--all") ? Listing(PlannerSession, "done") : "[]"),
            _ => FakeCliRunner.Ok(),
        });

        var performed = await Runner(new ClaudeCli(cli), store, clock).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "brief")]);

        Assert.Empty(performed);
        Assert.DoesNotContain(cli.Calls, c => c.Args[0] == "--bg");
        Assert.Contains("--all", cli.Calls.Single(c => c.Args[0] == "agents").Args);
    }

    [Fact]
    public async Task AStoppedSessionIsReportedOnceAndNotBroughtBack()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, PlannerSession, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], null, null),
        });
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        var logged = new List<string>();
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok(Listing(PlannerSession, "stopped")),
            _ => FakeCliRunner.Ok(),
        });
        var runner = Runner(new ClaudeCli(cli), store, clock, logged.Add);

        var firstPass = await runner.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "brief")]);
        var secondPass = await runner.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "brief")]);

        Assert.Equal(AlertKind.Stopped, Assert.IsType<SupervisorAction.Alert>(Assert.Single(firstPass)).Kind);
        Assert.Empty(secondPass);
        Assert.DoesNotContain(cli.Calls, c => c.Args[0] == "--bg");
        Assert.Single(logged, l => l.Contains("ALERT Stopped"));
        var saved = store.Load()[TestSpecs.PlannerName];
        Assert.StartsWith("Stopped: ", saved.LastAlert);
        Assert.Equal("stopped", saved.LastState);
    }

    [Fact]
    public async Task AStoppedSessionThatIsThenRemovedIsNeverResumed()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, PlannerSession, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], null, null),
        });
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        var listing = Listing(PlannerSession, "stopped");
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok(listing),
            _ => FakeCliRunner.Ok(),
        });
        var runner = Runner(new ClaudeCli(cli), store, clock);
        var spec = TestSpecs.Planner(@"C:\W", "brief");

        Assert.Equal(AlertKind.Stopped, Assert.IsType<SupervisorAction.Alert>(Assert.Single(await runner.CheckOnceAsync([spec]))).Kind);

        // The owner removes the session (claude rm). The listing no longer shows it, and no later pass brings it back.
        listing = "[]";
        for (var i = 0; i < 3; i++)
        {
            clock.Now = clock.Now.AddMinutes(5);
            Assert.Empty(await runner.CheckOnceAsync([spec]));
        }
        Assert.DoesNotContain(cli.Calls, c => c.Args[0] == "--bg");
        Assert.Equal("stopped", store.Load()[TestSpecs.PlannerName].LastState);
    }

    [Fact]
    public async Task AStopIsKeptWhenAnotherAlertReplacesTheLastAlertBeforeTheRemoval()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, PlannerSession, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], null, null),
        });
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        var listing = Listing(PlannerSession, "stopped");
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok(listing),
            _ => FakeCliRunner.Ok(),
        });
        var runner = Runner(new ClaudeCli(cli), store, clock);
        var spec = TestSpecs.Planner(@"C:\W", "brief");

        // The run is near its limit on the same pass that sees the stop, so the limit alert replaces the stop as the last alert.
        runner.Budgets = new Dictionary<string, RunBudget>(StringComparer.Ordinal) { [TestSpecs.PlannerName] = new RunBudget(0.9) };
        await runner.CheckOnceAsync([spec]);
        Assert.StartsWith("LimitNear: ", store.Load()[TestSpecs.PlannerName].LastAlert);

        listing = "[]";
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Empty(await runner.CheckOnceAsync([spec]));
        Assert.DoesNotContain(cli.Calls, c => c.Args[0] == "--bg");
    }

    [Fact]
    public async Task ARestartLoopIsAlertedOncePerLoopAndAgainAfterAResume()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var t0 = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        store.Save(new Dictionary<string, SessionRecord>
        {
            // Three restarts in the last hour is the limit. The session is lost, so every pass sees it over the limit.
            [TestSpecs.PlannerName] = new(SessionRole.Planner, PlannerSession, TestSpecs.PlannerName, t0.AddDays(-1), 3,
                [t0.AddMinutes(-30), t0.AddMinutes(-20), t0.AddMinutes(-10)], null, null),
        });
        var clock = new FakeClock(t0);
        var logged = new List<string>();
        var lost = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Ok($"backgrounded · {ShortId} · planner\n"),
            _ => FakeCliRunner.Ok(),
        });
        var runner = Runner(new ClaudeCli(lost), store, clock, logged.Add);
        var spec = TestSpecs.Planner(@"C:\W", "brief");

        // The first pass over the limit alerts. The next pass, a minute later, does not repeat the alert.
        Assert.Equal(AlertKind.RestartLoop, Assert.IsType<SupervisorAction.Alert>(Assert.Single(await runner.CheckOnceAsync([spec]))).Kind);
        clock.Now = t0.AddMinutes(1);
        Assert.Empty(await runner.CheckOnceAsync([spec]));

        // An hour on, the three restarts have aged out: the session is resumed, and the resume clears the alert.
        clock.Now = t0.AddMinutes(61);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await runner.CheckOnceAsync([spec])));
        Assert.Null(store.Load()[TestSpecs.PlannerName].LastAlert);

        // It is lost again. Two more restarts in the hour are under the limit, and the third reaches it: the alert comes back, once.
        clock.Now = t0.AddMinutes(62);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await runner.CheckOnceAsync([spec])));
        clock.Now = t0.AddMinutes(63);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(await runner.CheckOnceAsync([spec])));
        clock.Now = t0.AddMinutes(64);
        Assert.Equal(AlertKind.RestartLoop, Assert.IsType<SupervisorAction.Alert>(Assert.Single(await runner.CheckOnceAsync([spec]))).Kind);

        Assert.Equal(2, logged.Count(l => l.Contains("ALERT RestartLoop")));
    }

    [Fact]
    public async Task TheStateFileSurvivesARestartOfTheSupervisor()
    {
        var path = Path.Combine(_dir, "state.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var spec = TestSpecs.Planner(@"C:\W", "brief");

        await Runner(new ClaudeCli(LaunchingCli(PlannerSession, "working")), new StateStore(path), clock).CheckOnceAsync([spec]);

        // A new process reads the same file and sees the planner it started earlier.
        var next = new FakeCliRunner(_ => FakeCliRunner.Ok(Listing(PlannerSession, "done")));
        var performed = await Runner(new ClaudeCli(next), new StateStore(path), clock).CheckOnceAsync([spec]);

        Assert.Empty(performed);
        Assert.Single(next.Calls);
    }

    [Fact]
    public async Task AStartThatNeverShowsUpIsReportedAndRecordedWithoutASession()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var neverListed = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Ok($"backgrounded · {ShortId} · planner\n"),
            _ => FakeCliRunner.Ok(),
        });

        var performed = await Runner(new ClaudeCli(neverListed), store, clock).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "b")]);

        // The failure is reported, and it is recorded with no session, so the next pass does not start it again.
        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(performed));
        Assert.Equal(AlertKind.StartFailed, alert.Kind);
        Assert.Contains(ShortId, alert.Detail);
        Assert.Equal(string.Empty, store.Load()[TestSpecs.PlannerName].SessionId);
    }
}

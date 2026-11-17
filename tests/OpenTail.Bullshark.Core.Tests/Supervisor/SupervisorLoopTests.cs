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

    private static FakeCliRunner Cli(string logsText) => Cli(logsText,
        $$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"working"}]""");

    private static FakeCliRunner Cli(string logsText, string agentsJson) => new(args => args[0] switch
    {
        "agents" => FakeCliRunner.Ok(agentsJson),
        "logs" => FakeCliRunner.Ok(logsText),
        _ => FakeCliRunner.Ok(),
    });

    /// <summary>A clock the test moves on by hand, for the login alert's hour.</summary>
    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

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

    /// <summary>The loop over several recorded sessions (name, session id), with an optional clock for the login alert.</summary>
    private (SupervisorLoop Loop, List<string> Logged) BuildOver(FakeCliRunner runner, IReadOnlyList<(string Name, string Id)> records, TimeProvider? clock = null)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(records.ToDictionary(r => r.Name, r => new SessionRecord(SessionRole.Coder, r.Id, r.Name, DateTimeOffset.UnixEpoch, 0, [], null, null)));
        var cli = new ClaudeCli(runner);
        var logged = new List<string>();
        var runnerForPass = new SupervisorRunner(cli, store, new FixedClock());
        return (new SupervisorLoop(runnerForPass, cli, store, logged.Add, clock), logged);
    }

    private static string Listing(params (string Id, string State)[] sessions) =>
        "[" + string.Join(",", sessions.Select(s =>
            $$"""{"id":"{{s.Id[..8]}}","kind":"background","sessionId":"{{s.Id}}","name":"x-{{s.Id[..8]}}","state":"{{s.State}}"}""")) + "]";

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
        // A live session: a finished one is not read at all (see DoneStoppedAndFailedSessionsAreNotRead).
        var runner = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok($$"""[{"id":"11111111","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"working"}]"""),
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

    private const string DoneId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string StoppedId = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string FailedId = "cccccccc-0000-0000-0000-000000000003";
    private const string LiveId = "dddddddd-0000-0000-0000-000000000004";
    private const string SecondLiveId = "eeeeeeee-0000-0000-0000-000000000005";

    /// <summary>Round 12 item A5: done, stopped and failed sessions are not read; a live one is.</summary>
    [Fact]
    public async Task DoneStoppedAndFailedSessionsAreNotRead()
    {
        var runner = Cli(string.Empty, Listing((DoneId, "done"), (StoppedId, "stopped"), (FailedId, "failed"), (LiveId, "working")));
        var (loop, _) = BuildOver(runner, [("done-one", DoneId), ("stopped-one", StoppedId), ("failed-one", FailedId), ("live-one", LiveId)]);

        await loop.RunPassAsync([]);

        var read = runner.Calls.Where(c => c.Args[0] == "logs").Select(c => c.Args[1]).ToList();
        Assert.Equal([ClaudeCli.ShortIdOf(LiveId)], read);
    }

    [Fact]
    public async Task ARecordTheListingDoesNotShowIsNotRead()
    {
        var runner = Cli(string.Empty, Listing((LiveId, "working")));
        var (loop, _) = BuildOver(runner, [("live-one", LiveId), ("unlisted-one", SecondLiveId)]);

        await loop.RunPassAsync([]);

        Assert.Equal([ClaudeCli.ShortIdOf(LiveId)], runner.Calls.Where(c => c.Args[0] == "logs").Select(c => c.Args[1]));
    }

    /// <summary>Round 12 item A5: the first session that shows the expiry ends the reads, since the login is the account's.</summary>
    [Fact]
    public async Task TheReadsStopAtTheFirstSessionThatShowsTheExpiry()
    {
        var runner = Cli("Your login expires in 1 day · run /login to renew", Listing((LiveId, "working"), (SecondLiveId, "working")));
        var (loop, logged) = BuildOver(runner, [("live-one", LiveId), ("live-two", SecondLiveId)]);

        await loop.RunPassAsync([]);

        Assert.Single(runner.Calls, c => c.Args[0] == "logs");
        Assert.Single(logged, l => l.Contains("LoginExpiring"));
    }

    /// <summary>Round 12 item A5: the login alert is written at most once an hour, and again once an hour has passed.</summary>
    [Fact]
    public async Task TheLoginAlertIsWrittenAtMostOncePerHour()
    {
        var clock = new SteppedClock(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var runner = Cli("Your login expires in 1 day · run /login to renew", Listing((LiveId, "working")));
        var (loop, logged) = BuildOver(runner, [("live-one", LiveId)], clock);
        int Alerts() => logged.Count(l => l.Contains("LoginExpiring"));

        await loop.RunPassAsync([]);
        Assert.Equal(1, Alerts());

        clock.Now = clock.Now.AddMinutes(10);
        await loop.RunPassAsync([]);
        Assert.Equal(1, Alerts());

        clock.Now = clock.Now.AddMinutes(51);
        await loop.RunPassAsync([]);
        Assert.Equal(2, Alerts());
    }
}

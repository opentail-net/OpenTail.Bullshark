using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Round 8 item 3: while `claude respawn --all` runs, and for 20 seconds after it, a supervision pass makes no start or resume decision.
/// The pass still reads the listing and shows what it finds. The hold window is tested on its own, then through the runner.
/// </summary>
public sealed class RespawnHoldTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-hold-" + Guid.NewGuid().ToString("N")[..8]);

    public RespawnHoldTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Listing(string? state) => state is null
        ? "[]"
        : $$"""[{"id":"abcdef12","kind":"background","sessionId":"{{SessionId}}","name":"{{TestSpecs.PlannerName}}","state":"{{state}}","cwd":"C:\\W"}]""";

    /// <summary>A stand-in for `claude` with a fixed listing; a launch is recorded in the calls.</summary>
    private static FakeCliRunner Cli(string listing) => new(args => args[0] switch
    {
        "agents" => FakeCliRunner.Ok(listing),
        "logs" => FakeCliRunner.Ok(string.Empty),
        "--bg" => FakeCliRunner.Ok("backgrounded · abcdef12 · test-planner\n"),
        _ => FakeCliRunner.Ok(),
    });

    private static SessionRecord Record() =>
        new(SessionRole.Planner, SessionId, TestSpecs.PlannerName, DateTimeOffset.UnixEpoch, 0, [], "working", null);

    private StateStore Store(SessionRecord? record)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        if (record is not null) store.Save(new Dictionary<string, SessionRecord> { [record.Name] = record });
        return store;
    }

    private static List<string> LaunchArgs(FakeCliRunner runner) =>
        runner.Calls.Where(c => c.Args[0] == "--bg").SelectMany(c => c.Args).ToList();

    private static SupervisorRunner RunnerFor(FakeCliRunner cli, StateStore store, TimeProvider clock, RespawnHold? hold) =>
        new(new ClaudeCli(cli), store, clock, resolveTimeout: TimeSpan.FromSeconds(1)) { Hold = hold };

    [Fact]
    public void NothingIsHeldBeforeARespawn()
    {
        Assert.False(new RespawnHold(new SettableClock(Start)).IsHeld);
    }

    [Fact]
    public void TheHoldLastsWhileTheRespawnRunsWhateverTheTime()
    {
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);

        hold.Begin();
        clock.Now = Start.AddMinutes(10);

        Assert.True(hold.IsHeld);
    }

    [Fact]
    public void TheHoldLastsTwentySecondsAfterTheRespawnEnds()
    {
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        hold.Begin();
        hold.End();

        clock.Now = Start.AddMilliseconds(19_999);
        Assert.True(hold.IsHeld);

        // At exactly twenty seconds the hold is over: the window is open for less than 20 seconds.
        clock.Now = Start.AddSeconds(20);
        Assert.False(hold.IsHeld);
        Assert.Equal(TimeSpan.FromSeconds(20), RespawnHold.Window);
    }

    [Fact]
    public void ANewRespawnHoldsAgainUntilItEnds()
    {
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        hold.Begin();
        hold.End();
        clock.Now = Start.AddMinutes(1);
        Assert.False(hold.IsHeld);

        hold.Begin();

        Assert.True(hold.IsHeld);
        clock.Now = Start.AddMinutes(30);
        Assert.True(hold.IsHeld);
    }

    [Fact]
    public async Task DuringTheHoldAMissingSessionIsNotResumed()
    {
        // The respawn is restarting the workers: the listing is empty for now, so the session looks gone. It is not resumed.
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = Cli(Listing(null));
        hold.Begin();

        var performed = await RunnerFor(cli, Store(Record()), clock, hold).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Empty(performed);
        Assert.Empty(LaunchArgs(cli));
    }

    [Fact]
    public async Task DuringTheHoldAFirstStartIsNotCarriedOut()
    {
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = Cli(Listing(null));
        hold.Begin();

        var performed = await RunnerFor(cli, Store(null), clock, hold).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Empty(performed);
        Assert.Empty(LaunchArgs(cli));
    }

    [Fact]
    public async Task DuringTheHoldTheListingIsStillReadAndAnAlertStillShows()
    {
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = Cli(Listing("blocked"));
        hold.Begin();

        var performed = await RunnerFor(cli, Store(Record()), clock, hold).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.Contains(cli.Calls, c => c.Args[0] == "agents");
        Assert.Equal(AlertKind.Blocked, Assert.IsType<SupervisorAction.Alert>(Assert.Single(performed)).Kind);
    }

    [Fact]
    public async Task DuringTheHoldALiveSessionIsStillAdoptedBecauseNothingIsStarted()
    {
        // Adoption starts nothing: it writes the record of a session that is already listed, so the pass keeps it (round 8 item 3).
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = Cli(Listing("working"));
        hold.Begin();

        var performed = await RunnerFor(cli, Store(null), clock, hold).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Adopt>(Assert.Single(performed));
        Assert.Empty(LaunchArgs(cli));
    }

    [Fact]
    public async Task AfterTheHoldTheMissingSessionIsResumed()
    {
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = Cli(Listing(null));
        var store = Store(Record());
        hold.Begin();
        Assert.Empty(await RunnerFor(cli, store, clock, hold).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]));
        hold.End();
        clock.Now = Start.AddSeconds(21);

        var performed = await RunnerFor(cli, store, clock, hold).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        Assert.Contains("--resume", LaunchArgs(cli));
    }

    [Fact]
    public async Task WithoutAHoldAMissingSessionIsResumedAsBefore()
    {
        var clock = new SettableClock(Start);
        var cli = Cli(Listing(null));

        var performed = await RunnerFor(cli, Store(Record()), clock, hold: null).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
    }

    /// <summary>
    /// A stand-in for `claude` in a pass whose first launch begins the respawn, as the window's click does while a pass runs. The listing shows
    /// the planner once it has been launched, so its id reads back.
    /// </summary>
    private static FakeCliRunner RespawnBeginsAtFirstLaunch(RespawnHold hold, string listingBefore, string listingAfter)
    {
        var launched = false;
        return new FakeCliRunner(args =>
        {
            switch (args[0])
            {
                case "agents":
                    return FakeCliRunner.Ok(launched ? listingAfter : listingBefore);
                case "--bg":
                    launched = true;
                    hold.Begin();
                    return FakeCliRunner.Ok("backgrounded · abcdef12 · test-planner\n");
                default:
                    return FakeCliRunner.Ok();
            }
        });
    }

    [Fact]
    public async Task ARespawnThatBeginsMidPassHoldsTheStartsThatFollow()
    {
        // The planner is decided on first and launched; the launch begins the respawn. The coder was decided before the respawn, so its start is
        // dropped: the hold is read again just before each action is carried out (round 9 item 2).
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = RespawnBeginsAtFirstLaunch(hold, "[]", Listing("working"));
        var store = new StateStore(Path.Combine(_dir, "state.json"));

        var performed = await RunnerFor(cli, store, clock, hold)
            .CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t"), TestSpecs.Coder(@"C:\W")]);

        Assert.IsType<SupervisorAction.Start>(Assert.Single(performed));
        Assert.Equal(1, cli.Calls.Count(c => c.Args[0] == "--bg"));
        var saved = store.Load();
        Assert.True(saved.ContainsKey(TestSpecs.PlannerName));
        Assert.False(saved.ContainsKey(TestSpecs.CoderName));
    }

    [Fact]
    public async Task ARespawnThatBeginsMidPassHoldsAResumeThatFollows()
    {
        // The planner is started first and begins the respawn. The coder's record says it was lost, so its resume was decided before the respawn:
        // it is dropped, and its record is not changed (round 9 item 2).
        var clock = new SettableClock(Start);
        var hold = new RespawnHold(clock);
        var cli = RespawnBeginsAtFirstLaunch(hold, "[]", Listing("working"));
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var coder = new SessionRecord(SessionRole.Coder, "22222222-0000-0000-0000-000000000002", TestSpecs.CoderName, Start.AddHours(-1),
            0, [], "working", null);
        store.Save(new Dictionary<string, SessionRecord> { [coder.Name] = coder });

        var performed = await RunnerFor(cli, store, clock, hold)
            .CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t"), TestSpecs.Coder(@"C:\W")]);

        Assert.IsType<SupervisorAction.Start>(Assert.Single(performed));
        Assert.Equal(1, cli.Calls.Count(c => c.Args[0] == "--bg"));
        Assert.DoesNotContain(cli.Calls, c => c.Args.Contains("--resume"));
        Assert.Equal(0, store.Load()[TestSpecs.CoderName].Restarts);
    }

    [Fact]
    public void TheConfirmationSaysWhatHappensToBlockedAndStoppedSessions()
    {
        var text = RespawnHold.ConfirmationText("2.1.326");

        Assert.Contains("claude 2.1.326", text);
        Assert.Contains("Blocked sessions lose the prompt they are waiting on", text);
        Assert.Contains("Bullshark does not restart a stopped session", text);
        Assert.Contains("Starts and resumes wait for 20 seconds", text);
    }
}

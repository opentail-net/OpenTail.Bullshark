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

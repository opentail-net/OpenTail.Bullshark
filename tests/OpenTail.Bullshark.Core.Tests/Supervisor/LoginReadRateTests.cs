using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Reliability batch R4: the login is read with `claude logs` at most once an hour for the account, not once per live session per pass.
/// A fake CLI and a clock the test moves by hand; the state file is in a temporary folder.
/// </summary>
public sealed class LoginReadRateTests : IDisposable
{
    private const string LiveA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string LiveB = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string LiveC = "cccccccc-0000-0000-0000-000000000003";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-login-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A CLI that lists the three sessions as working, and answers `logs` with the given text or failure.</summary>
    private static FakeCliRunner Cli(Func<CliResult> logs) => new(args => args[0] switch
    {
        "agents" => FakeCliRunner.Ok(
            """[{"id":"aaaaaaaa","kind":"background","sessionId":"aaaaaaaa-0000-0000-0000-000000000001","name":"a","state":"working"},""" +
            """{"id":"bbbbbbbb","kind":"background","sessionId":"bbbbbbbb-0000-0000-0000-000000000002","name":"b","state":"working"},""" +
            """{"id":"cccccccc","kind":"background","sessionId":"cccccccc-0000-0000-0000-000000000003","name":"c","state":"working"}]"""),
        "logs" => logs(),
        _ => FakeCliRunner.Ok(),
    });

    private (SupervisorLoop Loop, SteppedClock Clock, List<string> Logged) Build(FakeCliRunner runner, DateTimeOffset start)
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            ["a"] = new(SessionRole.Coder, LiveA, "a", DateTimeOffset.UnixEpoch, 0, [], null, null),
            ["b"] = new(SessionRole.Coder, LiveB, "b", DateTimeOffset.UnixEpoch, 0, [], null, null),
            ["c"] = new(SessionRole.Coder, LiveC, "c", DateTimeOffset.UnixEpoch, 0, [], null, null),
        });
        var clock = new SteppedClock(start);
        var cli = new ClaudeCli(runner);
        var logged = new List<string>();
        var runnerForPass = new SupervisorRunner(cli, store, clock);
        return (new SupervisorLoop(runnerForPass, cli, store, logged.Add, clock), clock, logged);
    }

    private static int LogsCalls(FakeCliRunner runner) => runner.Calls.Count(c => c.Args[0] == "logs");

    private static readonly DateTimeOffset Start = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThreeLiveSessionsGiveOneLogsCallAnHour()
    {
        var runner = Cli(() => FakeCliRunner.Ok("nothing to warn about"));
        var (loop, clock, _) = Build(runner, Start);

        for (var minute = 0; minute < 60; minute++)
        {
            await loop.RunPassAsync([]);
            clock.Now = clock.Now.AddMinutes(1);
        }

        Assert.Equal(1, LogsCalls(runner));
    }

    [Fact]
    public async Task TheNextReadComesAnHourAfterTheFirst()
    {
        var runner = Cli(() => FakeCliRunner.Ok("nothing to warn about"));
        var (loop, clock, _) = Build(runner, Start);

        await loop.RunPassAsync([]);
        clock.Now = Start.AddMinutes(59);
        await loop.RunPassAsync([]);
        Assert.Equal(1, LogsCalls(runner));

        clock.Now = Start.AddHours(1);
        await loop.RunPassAsync([]);
        Assert.Equal(2, LogsCalls(runner));
    }

    [Fact]
    public async Task AnExpiringLoginIsReadOnEveryPassUntilItClears()
    {
        var text = "Your login expires in 1 day · run /login to renew";
        var runner = Cli(() => FakeCliRunner.Ok(text));
        var (loop, clock, _) = Build(runner, Start);

        await loop.RunPassAsync([]);
        clock.Now = Start.AddMinutes(1);
        await loop.RunPassAsync([]);
        clock.Now = Start.AddMinutes(2);
        await loop.RunPassAsync([]);
        Assert.Equal(3, LogsCalls(runner));

        // The login is renewed: the next read shows nothing, and the reads go back to one an hour.
        text = string.Empty;
        clock.Now = Start.AddMinutes(3);
        await loop.RunPassAsync([]);
        Assert.Equal(4, LogsCalls(runner));
        clock.Now = Start.AddMinutes(4);
        await loop.RunPassAsync([]);
        Assert.Equal(4, LogsCalls(runner));
    }

    [Fact]
    public async Task AFailedReadIsNotRetriedOnTheNextPass()
    {
        var runner = Cli(() => FakeCliRunner.Fail("job not found"));
        var (loop, clock, logged) = Build(runner, Start);

        await loop.RunPassAsync([]);
        clock.Now = Start.AddMinutes(1);
        await loop.RunPassAsync([]);
        clock.Now = Start.AddMinutes(10);
        await loop.RunPassAsync([]);

        Assert.Equal(1, LogsCalls(runner));
        Assert.Single(logged, l => l.Contains("not readable"));
    }

    [Fact]
    public async Task AFailedReadIsTriedAgainAnHourLater()
    {
        var runner = Cli(() => FakeCliRunner.Fail("job not found"));
        var (loop, clock, _) = Build(runner, Start);

        await loop.RunPassAsync([]);
        clock.Now = Start.AddHours(1);
        await loop.RunPassAsync([]);

        Assert.Equal(2, LogsCalls(runner));
    }

    [Fact]
    public async Task ThePassesBetweenReadsShowTheLastReading()
    {
        var runner = Cli(() => FakeCliRunner.Ok("nothing to warn about"));
        var (loop, clock, _) = Build(runner, Start);

        await loop.RunPassAsync([]);
        Assert.True(loop.LastLogin.Readable);

        clock.Now = Start.AddMinutes(5);
        await loop.RunPassAsync([]);

        Assert.True(loop.LastLogin.Readable);
        Assert.Null(loop.LastLogin.Remaining);
    }

    [Fact]
    public async Task NoLiveSessionMeansNoRead()
    {
        var runner = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            _ => FakeCliRunner.Ok(),
        });
        var (loop, _, _) = Build(runner, Start);

        await loop.RunPassAsync([]);

        Assert.Equal(0, LogsCalls(runner));
        Assert.False(loop.LastLogin.Readable);
    }
}

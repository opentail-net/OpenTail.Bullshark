using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// `claude daemon status` (round 7 item 1, revised by the planner). The not-running text is the real output of claude 2.1.326 on 2026-11-17,
/// with the user's folder name replaced. The running text is hand-built from the keys in `claude daemon --help`: no daemon was running on the
/// machine, and this round did not start one (the brief forbids it). "Not running" is the normal idle state, so it never stops a pass; it only
/// changes how a listed session that claims working or blocked is read.
/// </summary>
public sealed class DaemonStatusTests
{
    private const string NotRunningOutput = """
        not running

        bg sessions:
          sock dir:     \\.\pipe\cc-daemon-*
          control.sock: unreachable (connect ENOENT \\.\pipe\cc-daemon-*-control)
          bg workers:   0 in roster.json (control unreachable)
          roster.json:  updated 6604s ago
          daemon.log:   31.7KB at C:\Users\example\.claude\daemon.log
        """;

    // Hand-built: the running line and the keys follow the help text. Not captured (no running daemon was available to check).
    private const string RunningOutput = """
        running (pid 4312, version 2.1.326, uptime 2h 5m)

        bg sessions:
          sock dir:     \\.\pipe\cc-daemon-*
          control.sock: listening (\\.\pipe\cc-daemon-*-control)
          bg workers:   2 in roster.json
          roster.json:  updated 3s ago
          daemon.log:   12.4KB at C:\Users\example\.claude\daemon.log
        """;

    private const string RunningButControlUnreachable = """
        running (pid 4312, version 2.1.326, uptime 2h 5m)

        bg sessions:
          control.sock: unreachable (connect ENOENT \\.\pipe\cc-daemon-*-control)
          bg workers:   0 in roster.json (control unreachable)
        """;

    /// <summary>
    /// A listing with sessions that claim working and blocked (one with a waiting reason), a done and a stopped one, and an interactive one with
    /// a status. Shape as `claude agents --json` prints it; the ids and names are synthetic.
    /// </summary>
    private const string Listing = """
        [
          {"id":"aaaaaaa1","kind":"background","sessionId":"s-working","name":"a","state":"working","cwd":"C:\\W"},
          {"id":"bbbbbbb2","kind":"background","sessionId":"s-blocked","name":"b","state":"blocked","waitingFor":"permission prompt"},
          {"id":"ccccccc3","kind":"background","sessionId":"s-done","name":"c","state":"done"},
          {"id":"ddddddd4","kind":"background","sessionId":"s-stopped","name":"d","state":"stopped"},
          {"pid":15732,"kind":"interactive","sessionId":"s-busy","name":"e","status":"busy"}
        ]
        """;

    [Fact]
    public void TheCapturedNotRunningOutputIsNotRunning()
    {
        var status = DaemonStatus.Parse(NotRunningOutput);

        Assert.Equal(DaemonState.NotRunning, status.State);
        Assert.True(status.IsNotRunning);
        Assert.Null(status.Pid);
        Assert.Equal(false, status.ControlReachable);
        Assert.Equal(0, status.BackgroundWorkers);
        Assert.Equal(6604, status.RosterAgeSeconds);
        Assert.Equal(@"C:\Users\example\.claude\daemon.log", status.LogPath);
    }

    [Fact]
    public void ARunningDaemonWithItsControlSocketIsParsed()
    {
        var status = DaemonStatus.Parse(RunningOutput);

        Assert.Equal(DaemonState.Running, status.State);
        Assert.False(status.IsNotRunning);
        Assert.Equal(4312, status.Pid);
        Assert.Equal("2.1.326", status.Version);
        Assert.Equal("2h 5m", status.Uptime);
        Assert.Equal(true, status.ControlReachable);
        Assert.Equal(2, status.BackgroundWorkers);
        Assert.Equal(3, status.RosterAgeSeconds);
    }

    [Fact]
    public void ARunningDaemonWhoseControlIsUnreachableChangesNoListingAndNoticesNothing()
    {
        // Control unreachable no longer stops anything or adds a notice: the owner sees the value, and the listing is used as it is.
        var status = DaemonStatus.Parse(RunningButControlUnreachable);
        var agents = AgentInfo.ParseList(Listing);

        Assert.Equal(DaemonState.Running, status.State);
        Assert.Equal(false, status.ControlReachable);
        Assert.Same(agents, status.IdleListing(agents));
        Assert.Null(status.NeedsYouNotice(agents));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("the daemon is starting")]
    [InlineData("Unexpected output from a newer claude: 3 things changed")]
    public void UnreadableOutputIsUnknownAndChangesNothing(string output)
    {
        var status = DaemonStatus.Parse(output);
        var agents = AgentInfo.ParseList(Listing);

        Assert.Equal(DaemonState.Unknown, status.State);
        Assert.Null(status.Pid);
        Assert.Null(status.Version);
        Assert.Null(status.ControlReachable);
        Assert.Same(agents, status.IdleListing(agents));
        Assert.Null(status.NeedsYouNotice(agents));
    }

    [Fact]
    public async Task TheStatusCommandIsReadOnlyAndItsExitCodeIsNotAnError()
    {
        // The command exits with 1 when the daemon is not running. That is the answer, not a failure.
        var runner = new FakeCliRunner(_ => new CliResult(1, NotRunningOutput, string.Empty, TimedOut: false));
        var cli = new ClaudeCli(runner);

        var status = await cli.DaemonStatusAsync();

        Assert.Equal(DaemonState.NotRunning, status.State);
        Assert.Equal(["daemon", "status"], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task ACommandThatCannotBeRunIsUnknownNotAnError()
    {
        var cli = new ClaudeCli(new ThrowingRunner());

        var status = await cli.DaemonStatusAsync();

        Assert.Equal(DaemonState.Unknown, status.State);
        Assert.Contains("could not start", status.Raw);
    }

    [Fact]
    public async Task ATimedOutStatusCommandIsUnknown()
    {
        var runner = new FakeCliRunner(_ => new CliResult(-1, string.Empty, string.Empty, TimedOut: true));

        var status = await new ClaudeCli(runner).DaemonStatusAsync();

        Assert.Equal(DaemonState.Unknown, status.State);
    }

    [Fact]
    public void WhileNotRunningAListedWorkingOrBlockedSessionIsStaleAndTheOthersAreNot()
    {
        var agents = AgentInfo.ParseList(Listing);

        var seen = DaemonStatus.Parse(NotRunningOutput).IdleListing(agents);

        Assert.Equal(DaemonStatus.StaleState, seen[0].EffectiveState);
        Assert.Equal(DaemonStatus.StaleState, seen[1].EffectiveState);
        Assert.Null(seen[1].Waiting);
        Assert.Equal("done", seen[2].EffectiveState);
        Assert.Equal("stopped", seen[3].EffectiveState);
        Assert.Equal("busy", seen[4].EffectiveState);
        // Still listed under the same session id, so the policy does not resume it on this basis.
        Assert.Equal("s-blocked", seen[1].SessionId);
    }

    [Fact]
    public void AStaleRowKeepsTheStateItWasListedWithAndTheOtherRowsHaveNone()
    {
        // Round 8 item 1: the policy tells a stale working session from a stale blocked one by the state the listing printed.
        var seen = DaemonStatus.Parse(NotRunningOutput).IdleListing(AgentInfo.ParseList(Listing));

        Assert.Equal("working", seen[0].ListedState);
        Assert.Equal("blocked", seen[1].ListedState);
        Assert.Null(seen[2].ListedState);
        Assert.Null(seen[3].ListedState);
    }

    [Fact]
    public void WhileTheDaemonRunsTheListingIsUnchanged()
    {
        var agents = AgentInfo.ParseList(Listing);

        Assert.Same(agents, DaemonStatus.Parse(RunningOutput).IdleListing(agents));
    }

    [Fact]
    public void AStaleSessionIsShownAsDaemonIdle()
    {
        Assert.Equal("not running (daemon idle)", DaemonStatus.DisplayState(DaemonStatus.StaleState));
        Assert.Equal("blocked", DaemonStatus.DisplayState("blocked"));
    }

    [Fact]
    public void TheNeedsYouNoticeAppearsOnlyWhenNotRunningAndAClaimingSessionIsStillListed()
    {
        var agents = AgentInfo.ParseList(Listing);

        Assert.Equal(
            "Background daemon is not running: 2 session(s) listed as working/blocked are not alive.",
            DaemonStatus.Parse(NotRunningOutput).NeedsYouNotice(agents));
        Assert.Equal("background daemon not running: 2 listed session(s) not alive", DaemonStatus.Parse(NotRunningOutput).StatusNotice(agents));

        // Nothing claims working or blocked: no notice, however the daemon is.
        var idle = AgentInfo.ParseList("""[{"id":"ccccccc3","kind":"background","sessionId":"s-done","name":"c","state":"done"}]""");
        Assert.Null(DaemonStatus.Parse(NotRunningOutput).NeedsYouNotice(idle));

        // A running daemon with a claiming session is consistent: no notice.
        Assert.Null(DaemonStatus.Parse(RunningOutput).NeedsYouNotice(agents));
    }

    [Fact]
    public void TheStatusLineSaysTheDaemonNoticeWhenThereIsOne()
    {
        var agents = AgentInfo.ParseList(Listing);
        var down = PassSnapshot.StatusParts(new DateTime(2026, 11, 17, 12, 0, 0), 0, 0, 0, null, null,
            DaemonStatus.Parse(NotRunningOutput).StatusNotice(agents));
        var healthy = PassSnapshot.StatusParts(new DateTime(2026, 11, 17, 12, 0, 0), 0, 0, 0, null, null,
            DaemonStatus.Parse(RunningOutput).StatusNotice(agents));

        Assert.Contains(down, part => part.Contains("daemon not running"));
        Assert.DoesNotContain(healthy, part => part.Contains("daemon"));
    }

    /// <summary>A runner that cannot start the command, as when `claude` is not on the path.</summary>
    private sealed class ThrowingRunner : ICliRunner
    {
        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null) =>
            throw new InvalidOperationException("could not start 'claude'");
    }
}

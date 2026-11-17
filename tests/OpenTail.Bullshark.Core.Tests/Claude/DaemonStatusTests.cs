using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// `claude daemon status` (round 7 item 1). The not-running text is the real output of claude 2.1.326 on 2026-11-17, with the user's
/// folder name replaced. The running text is hand-built from the keys in `claude daemon --help`: no daemon was running on the machine,
/// and this round did not start one (the brief forbids it).
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

    [Fact]
    public void TheCapturedNotRunningOutputBlocksDecisions()
    {
        var status = DaemonStatus.Parse(NotRunningOutput);

        Assert.Equal(DaemonState.NotRunning, status.State);
        Assert.Null(status.Pid);
        Assert.Equal(false, status.ControlReachable);
        Assert.Equal(0, status.BackgroundWorkers);
        Assert.Equal(6604, status.RosterAgeSeconds);
        Assert.Equal(@"C:\Users\example\.claude\daemon.log", status.LogPath);
        Assert.True(status.BlocksDecisions);
        Assert.Contains("not running", status.Problem);
        Assert.Contains("daemon not running", status.StatusProblem);
    }

    [Fact]
    public void ARunningDaemonWithItsControlSocketDoesNotBlock()
    {
        var status = DaemonStatus.Parse(RunningOutput);

        Assert.Equal(DaemonState.Running, status.State);
        Assert.Equal(4312, status.Pid);
        Assert.Equal("2.1.326", status.Version);
        Assert.Equal("2h 5m", status.Uptime);
        Assert.Equal(true, status.ControlReachable);
        Assert.Equal(2, status.BackgroundWorkers);
        Assert.Equal(3, status.RosterAgeSeconds);
        Assert.False(status.BlocksDecisions);
        Assert.Null(status.Problem);
        Assert.Null(status.StatusProblem);
    }

    [Fact]
    public void A_RunningDaemonWhoseControlIsUnreachableBlocksDecisions()
    {
        var status = DaemonStatus.Parse(RunningButControlUnreachable);

        Assert.Equal(DaemonState.Running, status.State);
        Assert.Equal(false, status.ControlReachable);
        Assert.True(status.BlocksDecisions);
        Assert.Contains("control is unreachable", status.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("the daemon is starting")]
    [InlineData("Unexpected output from a newer claude: 3 things changed")]
    public void UnreadableOutputIsUnknownAndNeverBlocks(string output)
    {
        var status = DaemonStatus.Parse(output);

        Assert.Equal(DaemonState.Unknown, status.State);
        Assert.Null(status.Pid);
        Assert.Null(status.Version);
        Assert.Null(status.ControlReachable);
        Assert.False(status.BlocksDecisions);
        Assert.Null(status.Problem);
        Assert.Null(status.StatusProblem);
    }

    [Fact]
    public void AnUnknownStatusWithAnUnreachableControlLineStillBlocks()
    {
        // The state line is not recognised, but the control socket says it cannot be reached: that is a fact the supervisor acts on.
        var status = DaemonStatus.Parse("state changed\n  control.sock: unreachable (connect ENOENT)\n");

        Assert.Equal(DaemonState.Unknown, status.State);
        Assert.True(status.BlocksDecisions);
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
        Assert.False(status.BlocksDecisions);
        Assert.Contains("could not start", status.Raw);
    }

    [Fact]
    public async Task ATimedOutStatusCommandIsUnknown()
    {
        var runner = new FakeCliRunner(_ => new CliResult(-1, string.Empty, string.Empty, TimedOut: true));

        var status = await new ClaudeCli(runner).DaemonStatusAsync();

        Assert.Equal(DaemonState.Unknown, status.State);
        Assert.False(status.BlocksDecisions);
    }

    [Fact]
    public void TheStatusLineSaysTheDaemonProblemWhenThereIsOne()
    {
        var down = PassSnapshot.StatusParts(new DateTime(2026, 11, 17, 12, 0, 0), 0, 0, 0, null, null,
            DaemonStatus.Parse(NotRunningOutput).StatusProblem);
        var healthy = PassSnapshot.StatusParts(new DateTime(2026, 11, 17, 12, 0, 0), 0, 0, 0, null, null,
            DaemonStatus.Parse(RunningOutput).StatusProblem);

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

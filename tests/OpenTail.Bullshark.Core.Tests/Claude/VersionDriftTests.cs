using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// Round 7 item 2: the daemon's version against the installed claude's. The `--version` text is the real output of 2.1.326
/// ("2.1.326 (Claude Code)", captured 2026-11-17). Nothing in these tests respawns anything, except the one that checks the call.
/// </summary>
public sealed class VersionDriftTests
{
    [Theory]
    [InlineData("2.1.326 (Claude Code)\n", "2.1.326")]
    [InlineData("v2.1.326", "2.1.326")]
    [InlineData("  2.1.300 (Claude Code)", "2.1.300")]
    [InlineData("claude 2.1.326", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheVersionIsTheNumberAtTheStartOfTheText(string? text, string? expected) =>
        Assert.Equal(expected, VersionDrift.VersionOf(text));

    [Fact]
    public void ADifferentDaemonVersionIsDrift()
    {
        var drift = VersionDrift.Of("2.1.300", "2.1.326 (Claude Code)");

        Assert.NotNull(drift);
        Assert.Equal("2.1.300", drift.DaemonVersion);
        Assert.Equal("2.1.326", drift.CliVersion);
        Assert.Contains("runs claude 2.1.300", drift.Message);
        Assert.Contains("installed claude is 2.1.326", drift.Message);
    }

    [Fact]
    public void TheSameVersionIsNoDrift()
    {
        Assert.Null(VersionDrift.Of("2.1.326", "2.1.326 (Claude Code)"));
        Assert.Null(VersionDrift.Of("v2.1.326", "2.1.326"));
    }

    [Theory]
    [InlineData(null, "2.1.326")]
    [InlineData("2.1.300", null)]
    [InlineData("2.1.300", "not a version")]
    [InlineData("", "2.1.326")]
    public void AnUnknownVersionGivesNoNotice(string? daemon, string? cli) =>
        Assert.Null(VersionDrift.Of(daemon, cli));

    [Fact]
    public async Task TheInstalledVersionIsReadWithVersionFlag()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.326 (Claude Code)\n"));

        var version = await new ClaudeCli(runner).VersionAsync();

        Assert.Equal("2.1.326", version);
        Assert.Equal(["--version"], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task AFailedOrThrowingVersionReadIsUnknown()
    {
        var failed = await new ClaudeCli(new FakeCliRunner(_ => FakeCliRunner.Fail("boom"))).VersionAsync();
        var thrown = await new ClaudeCli(new ThrowingRunner()).VersionAsync();

        Assert.Null(failed);
        Assert.Null(thrown);
    }

    [Fact]
    public async Task RespawnAllRunsTheOneCommandAndReportsItsResult()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("respawned 3 sessions\n"));

        var result = await new ClaudeCli(runner).RespawnAllAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(["respawn", "--all"], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task ASupervisorPassNeverRespawnsADriftedDaemon()
    {
        // The daemon runs 2.1.300 and the listing shows a blocked session and a gone one: a pass decides and alerts, and never respawns.
        var runner = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("""[{"id":"11111111","kind":"background","sessionId":"11111111-2222-3333-4444-555555555555","name":"test-planner","state":"blocked"}]"""),
            "logs" => FakeCliRunner.Ok(string.Empty),
            _ => FakeCliRunner.Ok(),
        });
        var daemon = DaemonStatus.Parse("running (pid 4312, version 2.1.300, uptime 1h)\n  control.sock: listening\n");
        var path = Path.Combine(Path.GetTempPath(), "bullshark-drift-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var runnerForPass = new SupervisorRunner(new ClaudeCli(runner), new StateStore(path), TimeProvider.System) { Daemon = daemon };

            await runnerForPass.CheckOnceAsync([TestSpecs.Planner(@"C:\W", "t")]);

            Assert.DoesNotContain(runner.Calls, call => call.Args[0] == "respawn");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A runner that cannot start the command.</summary>
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

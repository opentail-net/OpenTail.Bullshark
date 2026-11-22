using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// R4: the probe runs the version command through the runner, keeps a Supported reading for the run, and reads every other result again.
/// </summary>
public sealed class ToolVersionProbeTests
{
    private const string Minimum = "2.1.330";

    /// <summary>A runner that cannot start the program, as a missing executable is.</summary>
    private sealed class MissingProgramRunner : ICliRunner
    {
        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null) =>
            throw new System.ComponentModel.Win32Exception(2);
    }

    [Fact]
    public async Task ASupportedReadingIsReusedForTheRun()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(FakeCliRunner.Ok("2.1.330 (Claude Code)\n"));
        runner.Enqueue(FakeCliRunner.Ok("2.1.4 (Claude Code)\n")); // Would be too old if it were read again.
        var probe = ToolVersionProbe.ForClaude(runner);

        var first = await probe.CheckAsync(Minimum);
        var second = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.Supported, first.Verdict);
        Assert.Equal(VersionVerdict.Supported, second.Verdict);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task AFailedReadingIsNotKept()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(FakeCliRunner.Fail("'claude' is not recognized", exitCode: 1));
        runner.Enqueue(FakeCliRunner.Ok("2.1.330"));
        var probe = ToolVersionProbe.ForClaude(runner);

        var first = await probe.CheckAsync(Minimum);
        var second = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.CouldNotDetermine, first.Verdict);
        Assert.Equal(VersionVerdict.Supported, second.Verdict);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ATooOldReadingIsReadAgainOnTheNextLaunch()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        runner.Enqueue(FakeCliRunner.Ok("2.1.330 (Claude Code)\n"));
        var probe = ToolVersionProbe.ForClaude(runner);

        var first = await probe.CheckAsync(Minimum);
        var second = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.TooOld, first.Verdict);
        Assert.Equal(VersionVerdict.Supported, second.Verdict);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ASupportedReadingIsCheckedAgainForANewMinimum()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.330\n"));
        var probe = ToolVersionProbe.ForClaude(runner);

        Assert.Equal(VersionVerdict.Supported, (await probe.CheckAsync("2.1.330")).Verdict);
        Assert.Equal(VersionVerdict.TooOld, (await probe.CheckAsync("2.1.400")).Verdict);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ATimeoutIsCouldNotDetermineNeverTooOld()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(new CliResult(-1, string.Empty, string.Empty, TimedOut: true));

        var result = await ToolVersionProbe.ForClaude(runner).CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.CouldNotDetermine, result.Verdict);
        Assert.Contains("timed out", result.Reason ?? string.Empty);
    }

    [Fact]
    public async Task ACommandThatCannotBeRunIsCouldNotDetermine()
    {
        var result = await ToolVersionProbe.ForClaude(new MissingProgramRunner()).CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.CouldNotDetermine, result.Verdict);
        Assert.Contains("Win32Exception", result.Reason ?? string.Empty);
    }

    [Fact]
    public async Task TheClaudeProbeRunsTheVersionCommandOnly()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.330"));

        await ToolVersionProbe.ForClaude(runner).CheckAsync(Minimum);

        Assert.Equal(["--version"], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task TheCodexProbeRunsTheSameCmdLineAsTheLauncher()
    {
        // CodexTerminalOpener starts "cmd.exe /d /c codex"; the probe asks that same program for the version.
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("codex-cli 0.171.0"));

        var result = await ToolVersionProbe.ForCodex(runner).CheckAsync("0.171.0");

        Assert.Equal(VersionVerdict.Supported, result.Verdict);
        Assert.Equal(["/d", "/c", "codex", "--version"], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task AVersionOnStandardErrorIsReadWhenStandardOutputHasNone()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(new CliResult(0, string.Empty, "2.1.330 (Claude Code)", TimedOut: false));

        var result = await ToolVersionProbe.ForClaude(runner).CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.Supported, result.Verdict);
    }
}

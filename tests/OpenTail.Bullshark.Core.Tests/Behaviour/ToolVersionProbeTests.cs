using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// R4: the probe runs the version command through the runner, keeps a Supported reading for the run, and keeps any other reading for the recheck
/// window (versions.recheckSeconds). With no window, or a window of zero, every other reading is read again.
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

    private static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A probe with a recheck window and a clock the test moves by hand.</summary>
    private static ToolVersionProbe WindowedClaude(ICliRunner runner, Func<TimeSpan> window, Func<DateTimeOffset> clock) =>
        new("Claude Code", runner, ["--version"], recheckWindow: window, clock: clock);

    [Fact]
    public async Task ATooOldReadingIsNotReadAgainInsideTheRecheckWindow()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        var first = await probe.CheckAsync(Minimum);
        now = now.AddSeconds(15);
        var second = await probe.CheckAsync(Minimum);
        now = now.AddSeconds(30);
        var third = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.TooOld, first.Verdict);
        Assert.Equal(VersionVerdict.TooOld, second.Verdict);
        Assert.Equal("2.1.4", second.Installed);
        Assert.Equal(VersionVerdict.TooOld, third.Verdict);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task ATooOldReadingIsReadAgainAtTheEndOfTheRecheckWindow()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        runner.Enqueue(FakeCliRunner.Ok("2.1.330 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        var first = await probe.CheckAsync(Minimum);
        now = now.AddSeconds(60); // The window is over at sixty seconds.
        var second = await probe.CheckAsync(Minimum);
        now = now.AddHours(1);
        var third = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.TooOld, first.Verdict);
        Assert.Equal(VersionVerdict.Supported, second.Verdict);
        Assert.Equal(VersionVerdict.Supported, third.Verdict);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task AnUnreadableReadingIsKeptInsideTheRecheckWindowToo()
    {
        var runner = new FakeCliRunner();
        runner.Enqueue(FakeCliRunner.Fail("'claude' is not recognized", exitCode: 1));
        runner.Enqueue(FakeCliRunner.Ok("2.1.330"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        var first = await probe.CheckAsync(Minimum);
        now = now.AddSeconds(30);
        var second = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.CouldNotDetermine, first.Verdict);
        Assert.Equal(VersionVerdict.CouldNotDetermine, second.Verdict);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task AChangedMinimumRunsTheCommandAtOnceInsideTheWindow()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        Assert.Equal(VersionVerdict.TooOld, (await probe.CheckAsync("2.1.330")).Verdict);
        Assert.Equal(VersionVerdict.Supported, (await probe.CheckAsync("2.1.3")).Verdict);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task AWindowOfZeroRunsTheCommandOnEveryCheck()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.Zero, () => now);

        await probe.CheckAsync(Minimum);
        await probe.CheckAsync(Minimum);

        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ASupportedReadingIsKeptWhateverTheRecheckWindow()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.330 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        await probe.CheckAsync(Minimum);
        now = now.AddHours(1);
        var later = await probe.CheckAsync(Minimum);

        Assert.Equal(VersionVerdict.Supported, later.Verdict);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task TheWindowIsReadAtEachCheck()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        var now = Start;
        var window = TimeSpan.FromSeconds(60);
        var probe = WindowedClaude(runner, () => window, () => now);

        await probe.CheckAsync(Minimum);
        window = TimeSpan.Zero; // The owner sets the knob to zero while the reading is kept.
        await probe.CheckAsync(Minimum);

        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task AReadingTakenAfterTheClockWentBackRunsTheCommandAgain()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        await probe.CheckAsync(Minimum);
        now = now.AddSeconds(-10);
        await probe.CheckAsync(Minimum);

        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task TwoChecksAtOnceRunASupportedCommandOnce()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.330 (Claude Code)\n"));
        var probe = ToolVersionProbe.ForClaude(runner);

        var results = await Task.WhenAll(probe.CheckAsync(Minimum), probe.CheckAsync(Minimum));

        Assert.All(results, r => Assert.Equal(VersionVerdict.Supported, r.Verdict));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task TwoChecksAtOnceRunARefusedCommandOnceInsideTheWindow()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("2.1.4 (Claude Code)\n"));
        var now = Start;
        var probe = WindowedClaude(runner, () => TimeSpan.FromSeconds(60), () => now);

        var results = await Task.WhenAll(probe.CheckAsync(Minimum), probe.CheckAsync(Minimum));

        Assert.All(results, r => Assert.Equal(VersionVerdict.TooOld, r.Verdict));
        Assert.Single(runner.Calls);
    }
}

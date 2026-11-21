using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>F8: what `claude` prints can reach app.log and events.jsonl only as one cut line, never a whole brief or transcript.</summary>
public sealed class CliErrorTextTests
{
    [Fact]
    public void OnlyTheFirstLineOfOutputIsKept()
    {
        Assert.Equal("first line", ClaudeCli.FirstLineOf("  first line\r\nsecret second line\n"));
        Assert.Equal(string.Empty, ClaudeCli.FirstLineOf("   \n"));
    }

    [Fact]
    public void ALongFirstLineIsCutAtTwoHundredCharacters()
    {
        var cut = ClaudeCli.FirstLineOf(new string('a', 500));

        Assert.Equal(ClaudeCli.MaxMessageCharacters + 3, cut.Length);
        Assert.EndsWith("...", cut);
    }

    private static BackgroundLaunch Launch() => new("planner", @"C:\W", "opus", "high", "Do the thing.");

    /// <summary>
    /// Review item 3 (2026-11-21): a launch's output can echo the brief, and an exception message goes to app.log and events.jsonl. So a failed
    /// launch names its command and its output's length only, never a word of the output.
    /// </summary>
    [Fact]
    public async Task AFailedBackgroundStartNamesNoOutputButItsLength()
    {
        const string stdout = "CANARY-PROMPT-7731 do the thing\n";
        var cli = new ClaudeCli(new FakeCliRunner(_ => FakeCliRunner.Ok(stdout)));

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => cli.StartBackgroundAsync(Launch()));

        Assert.DoesNotContain("CANARY", error.Message);
        Assert.Equal($"`claude --bg` did not print an id (output {stdout.Length} characters)", error.Message);
    }

    [Fact]
    public async Task AFailedBackgroundLaunchNamesNoOutputButItsExitCode()
    {
        var failed = new CliResult(1, "CANARY-PROMPT-7731 do the thing", "CANARY-PROMPT-7731 stderr echo", TimedOut: false);
        var cli = new ClaudeCli(new FakeCliRunner(_ => failed));

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => cli.StartBackgroundAsync(Launch()));

        Assert.DoesNotContain("CANARY", error.Message);
        Assert.Equal("`claude --bg` failed (exit 1)", error.Message);
    }

    [Fact]
    public async Task ATimedOutBackgroundLaunchSaysSoAndNamesNoOutput()
    {
        var timedOut = new CliResult(-1, "CANARY-PROMPT-7731 partial", string.Empty, TimedOut: true);
        var cli = new ClaudeCli(new FakeCliRunner(_ => timedOut));

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => cli.StartBackgroundAsync(Launch()));

        Assert.DoesNotContain("CANARY", error.Message);
        Assert.Equal("`claude --bg` failed (timed out)", error.Message);
    }
}

using System.ComponentModel;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Git;

namespace OpenTail.Bullshark.Core.Tests.Processes;

/// <summary>
/// The three start-failure conventions the process runners keep (dedup plan 2026-11-23, S6). Each runner gives a missing program its own answer:
/// the CLI runner throws, the Git runner returns a result, and the Codex model list returns a reading. A refactor must not change which one.
/// Windows only: the program name does not exist on any system.
/// </summary>
public sealed class StartFailureConventionTests
{
    private const string Missing = "opentail-no-such-program-for-a-test.exe";

    [Fact]
    public async Task TheCliRunnerThrowsWhenTheProgramCannotBeStarted()
    {
        await Assert.ThrowsAsync<Win32Exception>(() =>
            new ProcessCliRunner(Missing).RunAsync(["x"], null, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TheGitRunnerReturnsAResultWhenGitCannotBeStarted()
    {
        var output = await new ProcessGitRunner(Missing).RunAsync(["status"], Path.GetTempPath(), TimeSpan.FromSeconds(10), 1024);

        Assert.Equal(-1, output.ExitCode);
        Assert.False(output.TimedOut);
        Assert.False(output.OutputTruncated);
        Assert.StartsWith("git could not be started", output.StdErr);
    }

    [Fact]
    public async Task TheCodexModelListReturnsNotStartedWhenTheProgramCannotBeStarted()
    {
        var reading = await new CodexModelList(Missing, ["x"]).ReadAsync(CancellationToken.None);

        Assert.Equal(CodexModelProblem.NotStarted, reading.Problem);
    }
}

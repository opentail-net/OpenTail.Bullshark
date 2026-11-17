using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>Runs real Windows programs through the process path, so it is tested without `claude`.</summary>
public sealed class ProcessCliRunnerTests
{
    [Fact]
    public async Task AProgramRunsWithoutStandardInput()
    {
        var result = await new ProcessCliRunner("cmd.exe").RunAsync(["/c", "echo", "no input"], null, TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("no input", result.StdOut);
    }

    [Fact]
    public async Task StandardInputReachesTheProgram()
    {
        var result = await new ProcessCliRunner("cmd.exe").RunAsync(
            ["/c", "findstr", "/r", "."], null, TimeSpan.FromSeconds(30), CancellationToken.None, "piped line");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("piped line", result.StdOut);
    }

    /// <summary>
    /// Round 12 item A4: the command ends at once, but a child it started inherits its output pipe and runs for about nine seconds. The call
    /// returns with the text read so far after the output wait (one second here), not when the child ends. Windows only (the test uses cmd and ping).
    /// </summary>
    [Fact]
    public async Task AChildThatKeepsTheOutputOpenDoesNotHoldTheCall()
    {
        var runner = new ProcessCliRunner("cmd.exe", outputDrain: TimeSpan.FromSeconds(1));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = await runner.RunAsync(["/c", "echo first line & start /b ping -n 10 127.0.0.1"], null, TimeSpan.FromSeconds(30));

        clock.Stop();
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("first line", result.StdOut);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(6), $"the call held for {clock.Elapsed}");
    }
}

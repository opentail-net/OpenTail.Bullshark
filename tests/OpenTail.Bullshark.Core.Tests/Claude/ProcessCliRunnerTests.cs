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
    /// Round 12 item A4: the command ends at once, but a child it started inherits its output pipe and runs for about a minute. The call returns with
    /// the text read so far after the output wait (one second per pipe here), not when the child ends. Windows only (the test uses cmd and ping).
    /// The child runs for about a minute, not nine seconds: on a shared CI runner the call was seen to take 8 to 9 seconds, and a bound of six seconds
    /// against a nine-second child failed on timing alone. A call that waited for the child would take about a minute, so the twenty-second bound still
    /// catches it. Changed after this test failed on CI on 2026-11-23, when the root cause was not confirmed.
    /// </summary>
    [Fact]
    public async Task AChildThatKeepsTheOutputOpenDoesNotHoldTheCall()
    {
        var runner = new ProcessCliRunner("cmd.exe", outputDrain: TimeSpan.FromSeconds(1));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = await runner.RunAsync(["/c", "echo first line & start /b ping -n 60 127.0.0.1"], null, TimeSpan.FromSeconds(90));

        clock.Stop();
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("first line", result.StdOut);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"the call held for {clock.Elapsed}");
    }
}

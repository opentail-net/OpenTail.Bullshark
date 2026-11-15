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
}

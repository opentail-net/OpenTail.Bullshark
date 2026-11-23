using System.Diagnostics;
using System.Text;
using OpenTail.Bullshark.Core.Processes;

namespace OpenTail.Bullshark.Core.Tests.Processes;

/// <summary>The shared start information and stop (dedup plan 2026-11-23, S2). Windows only: the tests use cmd.exe and ping.</summary>
public sealed class ProcessLaunchTests
{
    [Fact]
    public void CreateRedirectsOutputAndErrorWithNoShellAndNoWindow()
    {
        var info = ProcessLaunch.Create("cmd.exe", ["/c", "echo"]);

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.False(info.RedirectStandardInput);
    }

    [Fact]
    public void CreatePassesTheArgumentsInOrder()
    {
        var info = ProcessLaunch.Create("cmd.exe", ["/c", "echo", "a b", "c"]);

        Assert.Equal(["/c", "echo", "a b", "c"], info.ArgumentList.ToArray());
    }

    [Fact]
    public void AnOutputEncodingIsSetForOutputAndError()
    {
        var info = ProcessLaunch.Create("cmd.exe", ["/c", "echo"], outputEncoding: Encoding.UTF8);

        Assert.Equal(Encoding.UTF8.CodePage, info.StandardOutputEncoding!.CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, info.StandardErrorEncoding!.CodePage);
    }

    [Fact]
    public void WithoutAnOutputEncodingTheDefaultIsKept()
    {
        var info = ProcessLaunch.Create("cmd.exe", ["/c", "echo"]);

        Assert.Null(info.StandardOutputEncoding);
        Assert.Null(info.StandardErrorEncoding);
    }

    [Fact]
    public void AnInputEncodingIsSetOnlyWhenStandardInputIsRedirected()
    {
        var notRedirected = ProcessLaunch.Create("cmd.exe", ["/c", "echo"], inputEncoding: Encoding.UTF8);
        var redirected = ProcessLaunch.Create("cmd.exe", ["/c", "findstr"], redirectStandardInput: true, inputEncoding: Encoding.UTF8);

        Assert.False(notRedirected.RedirectStandardInput);
        Assert.Null(notRedirected.StandardInputEncoding);
        Assert.True(redirected.RedirectStandardInput);
        Assert.Equal(Encoding.UTF8.CodePage, redirected.StandardInputEncoding!.CodePage);
    }

    [Fact]
    public void AWorkingFolderIsSetOnlyWhenGiven()
    {
        var unset = ProcessLaunch.Create("cmd.exe", ["/c", "echo"]);
        var set = ProcessLaunch.Create("cmd.exe", ["/c", "echo"], workingDirectory: Path.GetTempPath());

        Assert.Equal(string.Empty, unset.WorkingDirectory);
        Assert.Equal(Path.GetTempPath(), set.WorkingDirectory);
    }

    [Fact]
    public void KillStopsARunningProcess()
    {
        var info = ProcessLaunch.Create("cmd.exe", ["/c", "ping", "-n", "60", "127.0.0.1"]);
        using var process = Process.Start(info)!;

        ProcessLaunch.Kill(process);

        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(10)), "the process was not stopped");
    }

    [Fact]
    public void KillOnAProcessThatHasExitedDoesNotThrow()
    {
        var info = ProcessLaunch.Create("cmd.exe", ["/c", "exit", "0"]);
        using var process = Process.Start(info)!;
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(10)));

        ProcessLaunch.Kill(process);

        Assert.True(process.HasExited);
    }
}

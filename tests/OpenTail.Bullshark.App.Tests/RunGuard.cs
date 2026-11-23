using System.Diagnostics;
using System.IO;
using System.Management;
using OpenTail.Bullshark.App;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Every test class in the headless collection shares one run guard (Q7). Its disposal runs after the last of those classes: it counts the claude.exe
/// processes again and fails the run if there are more than when the test assembly loaded. It only counts.
/// </summary>
[CollectionDefinition(HeadlessCollection.Name, DisableParallelization = true)]
public sealed class HeadlessCollection : ICollectionFixture<RunGuard>
{
    public const string Name = "headless WPF";
}

/// <summary>The end-of-run count of claude.exe processes against the count taken when the test assembly loaded.</summary>
public sealed class RunGuard : IDisposable
{
    public void Dispose()
    {
        var before = TestGuard.ClaudeBefore;
        var after = TestGuard.ClaudeCount();
        if (after > before)
            throw new InvalidOperationException($"claude.exe processes grew during the test run: {before} before, {after} after");
    }
}

/// <summary>The guard itself: the flag is set and the app refuses to open its window, and no claude.exe process was added during the run.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class RunGuardTests
{
    [Fact]
    public void TheTestRunIsFlaggedAndTheAppRefusesItsWindow()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable(TestGuard.Flag));
        Assert.True(RunningUnderTest.Is);
    }

    [Fact]
    public void ClaudeProcessesDidNotGrowUpToThisPoint()
    {
        var before = TestGuard.ClaudeBefore;
        var now = TestGuard.ClaudeCount();
        Assert.True(now <= before, $"claude.exe processes: {before} when the run started, {now} now");
    }

    /// <summary>A copy of cmd.exe named claude.exe, in a folder of its own. It stands in for the Claude program: only its name is checked.</summary>
    private static string CopyNamedClaude(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "opentail-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "claude.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), copy);
        return copy;
    }

    private static void RemoveFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AClaudeProcessStartedByThisRunIsCounted()
    {
        var copy = CopyNamedClaude(out var folder);
        var before = TestGuard.ClaudeCount();
        var info = new ProcessStartInfo(copy) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("ping -n 30 127.0.0.1 > nul");
        using var child = Process.Start(info)!;
        try
        {
            Assert.Equal(before + 1, TestGuard.ClaudeCount());
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit(5000);
            RemoveFolder(folder);
        }
    }

    [Fact]
    public void AClaudeProcessThisRunDidNotStartIsNotCounted()
    {
        // Started through WMI, so its parent is the WMI provider and not this test host: as a Claude session the owner starts would be.
        var copy = CopyNamedClaude(out var folder);
        var before = TestGuard.ClaudeCount();
        using var processes = new ManagementClass("Win32_Process");
        var arguments = new object[] { $"\"{copy}\" /c ping -n 30 127.0.0.1 > nul", null!, null!, 0u };
        var result = processes.InvokeMethod("Create", arguments);
        var pid = Convert.ToInt32(arguments[3]);
        try
        {
            Assert.Equal(0u, Convert.ToUInt32(result));
            Assert.Equal(before, TestGuard.ClaudeCount());
        }
        finally
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch (ArgumentException) { }
            RemoveFolder(folder);
        }
    }
}

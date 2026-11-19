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
}

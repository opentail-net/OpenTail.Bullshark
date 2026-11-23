using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>Reliability batch R1: one supervisor per state. Each test uses its own random name, so no other process holds it.</summary>
public sealed class SingleInstanceTests
{
    private static string RandomName() => "Local\\OpenTail.Bullshark.test." + Guid.NewGuid().ToString("N");

    /// <summary>
    /// A mutex is re-entrant on its own thread, so a second acquire must come from another thread to show the name is taken. A real thread is
    /// used: a task waited on can run inline on the waiting thread, and that thread already holds the mutex.
    /// </summary>
    private static SingleInstance? TryFromOtherThread(string name)
    {
        SingleInstance? result = null;
        var thread = new Thread(() => result = SingleInstance.TryAcquire(name));
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary><see cref="TryFromOtherThread"/> that also gives the reason when the name is not taken, for a failure message.</summary>
    private static SingleInstance? TryFromOtherThreadWithReason(string name, out string? problem)
    {
        SingleInstance? result = null;
        string? reason = null;
        var thread = new Thread(() => result = SingleInstance.TryAcquire(name, out reason));
        thread.Start();
        thread.Join();
        problem = reason;
        return result;
    }

    [Fact]
    public void ASecondAcquireOfTheSameNameFails()
    {
        var name = RandomName();
        using var first = SingleInstance.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(TryFromOtherThread(name));
    }

    [Fact]
    public void ReleasingThenAcquiringWorksAgain()
    {
        var name = RandomName();
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        first.Dispose();
        using var second = TryFromOtherThread(name);

        Assert.NotNull(second);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var name = RandomName();
        var held = SingleInstance.TryAcquire(name)!;

        held.Dispose();
        held.Dispose();

        using var again = TryFromOtherThread(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void AnAbandonedNameIsTaken()
    {
        var name = RandomName();
        // A thread takes the name and ends without releasing it: the mutex is abandoned. The name must still be taken by the next holder.
        var holder = new Thread(() => { var mutex = new Mutex(false, name); mutex.WaitOne(); });
        holder.Start();
        holder.Join();

        // The thread has ended, but the operating system may take a moment to mark the mutex abandoned: a null read here is that moment. The read is
        // retried for up to two seconds, and the last reason is named if the name never comes free (the flake seen once in a full run, 2026-11-23).
        SingleInstance? taken = null;
        string? problem = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        do
        {
            taken = TryFromOtherThreadWithReason(name, out problem);
            if (taken is null) Thread.Sleep(20);
        }
        while (taken is null && DateTime.UtcNow < deadline);
        using (taken)
        {
            Assert.True(taken is not null, $"the abandoned name was not taken: {problem ?? "no reason was given"}");
        }
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(WaitHandleCannotBeOpenedException))]
    [InlineData(typeof(IOException))]
    public void AnUnusableNameGivesNoWindowAndAReasonAndNeverThrows(Type failure)
    {
        // The seam throws the failure a name held by another privilege level gives. The window then only watches: null, with the reason.
        Mutex Fails(string _) => throw (Exception)Activator.CreateInstance(failure)!;

        var taken = SingleInstance.TryAcquire(RandomName(), out var problem, Fails);

        Assert.Null(taken);
        Assert.NotNull(problem);
        Assert.Contains(failure.Name, problem);
    }

    [Fact]
    public void AnUnusableNameIsNotAProblemWhenAnotherHolderHasIt()
    {
        var name = RandomName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        // From another thread: the mutex is re-entrant on its own thread, so only another thread shows the name is taken.
        SingleInstance? second = null;
        string? problem = "not set";
        var thread = new Thread(() => second = SingleInstance.TryAcquire(name, out problem));
        thread.Start();
        thread.Join();

        Assert.Null(second);
        Assert.Null(problem);
    }

    [Fact]
    public void TheNameIsTheSameForTheSameStateInAnyCase()
    {
        var lower = SingleInstance.NameFor(@"C:\state\state.json");
        var upper = SingleInstance.NameFor(@"C:\STATE\State.JSON");

        Assert.Equal(lower, upper);
        Assert.StartsWith("Local\\OpenTail.Bullshark.supervisor.", lower, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoStatesHaveTwoNames()
    {
        Assert.NotEqual(
            SingleInstance.NameFor(@"C:\one\state.json"),
            SingleInstance.NameFor(@"C:\two\state.json"));
    }
}

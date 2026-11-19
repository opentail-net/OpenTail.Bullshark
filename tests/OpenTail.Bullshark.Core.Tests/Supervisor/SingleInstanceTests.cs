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

        using var taken = TryFromOtherThread(name);

        Assert.NotNull(taken);
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

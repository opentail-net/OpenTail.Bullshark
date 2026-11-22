using OpenTail.Bullshark.Core.Behaviour;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// The miss memory of failed transcript lookups (behaviour batch, R10): a miss is kept for the window, only a miss is kept, a window of zero keeps none,
/// the pane's clear forgets all, and the window is read on each call so a changed setting applies at once. The time is passed in, so nothing waits.
/// </summary>
public sealed class LookupMemoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    [Fact]
    public void A_miss_is_remembered_inside_the_window()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);

        Assert.True(memory.IsRemembered("s1", Start.AddSeconds(59), Window));
    }

    [Fact]
    public void A_miss_is_forgotten_at_and_after_the_window()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);

        // The window is strict: at exactly 60 seconds the search runs again.
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(60), Window));
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(61), Window));
    }

    [Fact]
    public void An_expired_miss_stays_dropped_when_a_longer_window_is_given_later()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(61), Window));

        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(62), TimeSpan.FromSeconds(600)));
    }

    [Fact]
    public void A_found_file_is_never_remembered_because_only_a_miss_is_recorded()
    {
        var memory = new LookupMemory();

        // A key that was never missed is not remembered, whatever the window.
        Assert.False(memory.IsRemembered("found", Start, Window));
    }

    [Fact]
    public void Forget_drops_a_miss_when_the_file_is_then_found()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);

        memory.Forget("s1");

        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(1), Window));
    }

    [Fact]
    public void A_zero_window_remembers_nothing()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, TimeSpan.Zero);

        Assert.False(memory.IsRemembered("s1", Start, TimeSpan.Zero));
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(1), Window));
    }

    [Fact]
    public void Turning_the_window_to_zero_stops_a_miss_that_was_remembered()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);

        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(10), TimeSpan.Zero));
        // Once off, the miss is gone: turning the window back on does not revive it.
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(10), Window));
    }

    [Fact]
    public void Clear_forgets_every_miss()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);
        memory.Remember("s2", Start, Window);

        memory.Clear();

        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(1), Window));
        Assert.False(memory.IsRemembered("s2", Start.AddSeconds(1), Window));
    }

    [Fact]
    public void A_different_key_is_unaffected()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);

        Assert.False(memory.IsRemembered("s2", Start.AddSeconds(1), Window));
        Assert.True(memory.IsRemembered("s1", Start.AddSeconds(1), Window));
    }

    [Fact]
    public void The_window_is_read_on_each_call()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);
        var now = Start.AddSeconds(30);

        Assert.True(memory.IsRemembered("s1", now, Window));
        // A shorter window applies at once: the 30-second-old miss is out of a 10-second window.
        Assert.False(memory.IsRemembered("s1", now, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void A_later_remember_restarts_the_window()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);
        memory.Remember("s1", Start.AddSeconds(50), Window);

        Assert.True(memory.IsRemembered("s1", Start.AddSeconds(100), Window));
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(110), Window));
    }

    [Fact]
    public void A_clock_that_went_back_does_not_keep_a_miss()
    {
        var memory = new LookupMemory();
        memory.Remember("s1", Start, Window);

        // The miss is later than now: it is dropped, so it cannot hide a file for longer than the window.
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(-1), Window));
        Assert.False(memory.IsRemembered("s1", Start.AddSeconds(1), Window));
    }
}

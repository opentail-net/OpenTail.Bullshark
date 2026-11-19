using OpenTail.Bullshark.Core.Tabs;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Tabs;

/// <summary>
/// Which tab becomes active after a close (perf and testability batch, Q6 b): the tab after the closed one in its pane, else the last tab of that pane,
/// and nothing when the left pane holds no tab after the close (the setup then opens). The right pane's tabs never take the left pane's place.
/// </summary>
public sealed class TabPolicyTests
{
    // "L" is a left-pane tab, "R" a right-pane tab. The index returned is in the strip after the close.
    private static int? Close(string strip, int closed)
    {
        var tabs = strip.Select(c => c.ToString()).ToList();
        return TabPolicy.ActiveAfterClose(tabs, closed, tab => tab == "R");
    }

    [Fact]
    public void TheTabAfterTheClosedOneTakesOver()
    {
        // L1 L2 L3, close L1 (index 0): the strip is now L2 L3, and L2 (index 0) takes over.
        Assert.Equal(0, Close("LLL", 0));
    }

    [Fact]
    public void TheLastTabTakesOverWhenTheClosedOneWasLast()
    {
        // L1 L2 L3, close L3 (index 2): nothing is after it, so the last remaining tab (index 1) takes over.
        Assert.Equal(1, Close("LLL", 2));
    }

    [Fact]
    public void TheNextLeftTabIsChosenOverARightTabThatFollows()
    {
        // L1 R1 L2, close L1: R1 is in the other pane, so L2 (now at index 1) takes over.
        Assert.Equal(1, Close("LRL", 0));
    }

    [Fact]
    public void WithNoLeftTabLeftNothingTakesOver()
    {
        // L1 R1 R2, close L1: the left pane is empty, so the caller opens the setup.
        Assert.Null(Close("LRR", 0));
    }

    [Fact]
    public void ARightTabCloseLeavesTheLeftNeighbourAfterIt()
    {
        // L1 R1 L2 L3, close R1 (index 1): the left tab after it (L2, now at index 1) is the new tab.
        Assert.Equal(1, Close("LRLL", 1));
    }

    [Fact]
    public void ANoLeftTabAtAllGivesNothing()
    {
        Assert.Null(Close("R", 0));
    }

    [Fact]
    public void AnIndexPastTheEndStillFindsALeftTab()
    {
        // The tab was not in the list (index -1 from IndexOf): the first left tab is taken, as the old Skip(0) did.
        Assert.Equal(0, Close("LL", -1));
    }
}

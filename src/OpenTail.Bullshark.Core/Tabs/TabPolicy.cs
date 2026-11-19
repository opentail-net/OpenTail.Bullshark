namespace OpenTail.Bullshark.Core.Tabs;

/// <summary>
/// Which tab becomes the active one when a tab closes (perf and testability batch, Q6, part b). The neighbour rule: the tab after the closed one in the
/// same pane takes over; with none, the last tab of that pane does; and a pane with no tab left is not closed here, the caller opens the setup. The
/// tabs of the right pane are never the left pane's neighbour. Pure.
/// </summary>
public static class TabPolicy
{
    /// <summary>
    /// The index, in the list as it stands after the close, of the tab to activate in the closed tab's pane; or null when the left pane holds no tab
    /// after the close (the caller then opens the setup, so the left pane is never empty). <paramref name="tabs"/> is the strip before the close,
    /// <paramref name="closed"/> the index of the closed tab in it, and <paramref name="isRight"/> tells the right pane's tabs.
    /// </summary>
    public static int? ActiveAfterClose<T>(IReadOnlyList<T> tabs, int closed, Func<T, bool> isRight)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(isRight);
        var remaining = new List<T>(tabs.Count);
        for (var i = 0; i < tabs.Count; i++)
            if (i != closed) remaining.Add(tabs[i]);

        var leftCount = remaining.Count(tab => !isRight(tab));
        if (leftCount == 0) return null;

        // The tab after the closed one takes over, as the list stood before the close: in the list after it, that is the tab at the same index.
        var from = Math.Clamp(closed, 0, remaining.Count);
        for (var i = from; i < remaining.Count; i++)
            if (!isRight(remaining[i])) return i;

        for (var i = remaining.Count - 1; i >= 0; i--)
            if (!isRight(remaining[i])) return i;
        return null;
    }
}

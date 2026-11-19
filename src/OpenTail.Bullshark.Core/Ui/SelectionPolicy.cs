namespace OpenTail.Bullshark.Core.Ui;

/// <summary>The panes a rail refresh can find on screen (the App's tab kinds).</summary>
public enum PaneKind
{
    Session,
    Setup,
    Transcript,
    Search,
    Changes,
    Insights,
    Settings,
}

/// <summary>What a rail refresh does with the selection (perf and testability batch, Q6, part a).</summary>
public enum SelectionAction
{
    /// <summary>The item the owner selected is still in the rail: it stays selected, and the pane is not touched.</summary>
    KeepSelected,

    /// <summary>No row is left to select, and the pane shows something that is not a rail row (a search, a transcript, ...): the pane keeps it.</summary>
    KeepPaneWithoutSelection,

    /// <summary>No row is left to select, and nothing else is shown: the setup page opens.</summary>
    ShowSetupWithoutSelection,

    /// <summary>A fallback row is selected, but the pane keeps what it shows (a search, a transcript or changes).</summary>
    SelectFallbackKeepPane,

    /// <summary>A fallback row is selected and its pane is shown.</summary>
    SelectFallbackAndShow,
}

/// <summary>
/// The selection after a rail refresh (perf and testability batch, Q6, part a). A refresh rebuilds the rail, so the selection is restored from the
/// owner's choice, or a fallback row is chosen, and the pane is changed only when it has to be. Pure; the App picks the fallback row itself (the
/// first live session, then the Trust step, then the first folder).
/// </summary>
public static class SelectionPolicy
{
    /// <summary>The panes that a refresh with no rail row to select keeps (a search, a transcript, changes, Insights and Settings).</summary>
    private static bool KeepsPaneWithNoRow(PaneKind? active) =>
        active is PaneKind.Search or PaneKind.Transcript or PaneKind.Changes or PaneKind.Insights or PaneKind.Settings;

    /// <summary>The panes that keep the pane when a fallback row is selected (a search, a transcript or changes).</summary>
    private static bool KeepsPaneWithRow(PaneKind? active) =>
        active is PaneKind.Search or PaneKind.Transcript or PaneKind.Changes;

    /// <summary>
    /// What the refresh does. <paramref name="selectedFound"/>: the owner's selected row is in the rebuilt rail. <paramref name="hasFallback"/>: a
    /// session, Trust or folder row exists to fall back to. <paramref name="active"/>: the pane kind of the active tab, or null when there is none.
    /// </summary>
    public static SelectionAction Decide(bool selectedFound, bool hasFallback, PaneKind? active)
    {
        if (selectedFound) return SelectionAction.KeepSelected;
        if (!hasFallback) return KeepsPaneWithNoRow(active) ? SelectionAction.KeepPaneWithoutSelection : SelectionAction.ShowSetupWithoutSelection;
        return KeepsPaneWithRow(active) ? SelectionAction.SelectFallbackKeepPane : SelectionAction.SelectFallbackAndShow;
    }
}

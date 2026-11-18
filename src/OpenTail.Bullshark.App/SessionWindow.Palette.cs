using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The command palette (ideas D3, docs/plans/palette-2026-11-18.md): Ctrl+Shift+P, or the "Go to…" button in the status bar, opens a box that goes to a
/// session or a page, or runs a safe action. Nothing is sent to a session, and nothing is stopped, restarted or removed (principle 3). Each entry runs
/// the handler the button, tab or rail already uses. The entries come from what is in memory, so opening the palette reads no file.
/// </summary>
public partial class SessionWindow
{
    private const string NotifyKey = "notify";

    /// <summary>
    /// The session records as the last rail refresh drew them. The palette's session entries come from this, not from the state file.
    /// Replaced on the UI thread only, by <see cref="RefreshRail"/>.
    /// </summary>
    private IReadOnlyDictionary<string, SessionRecord> _railRecords = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);

    /// <summary>
    /// One entry and what it runs. <paramref name="ReturnsKeyboard"/> is set for an action that leaves the pane as it is: after it runs, the
    /// keyboard goes back to the control that had it. A navigation sets its own focus, so it is not set for those.
    /// </summary>
    private sealed record PaletteItem(PaletteEntry Entry, Action Run, bool ReturnsKeyboard = false);

    private void GoToButton_Click(object sender, RoutedEventArgs e) => OpenPalette();

    /// <summary>
    /// Shows the palette and runs the entry chosen, if any. The keyboard goes back to the terminal (or control) that had it when the palette
    /// closes without a choice, and after an action that keeps the pane.
    /// </summary>
    private void OpenPalette()
    {
        var items = PaletteItems();
        var keyboard = KeyboardTarget();
        var dialog = new CommandPaletteWindow(items.Select(item => item.Entry).ToList(), Palette) { Owner = this };
        dialog.ShowDialog();

        var chosen = dialog.ChosenId is { } id ? items.FirstOrDefault(item => item.Entry.Id == id) : null;
        if (chosen is null)
        {
            ReturnKeyboard(keyboard);
            return;
        }
        chosen.Run();
        if (chosen.ReturnsKeyboard) ReturnKeyboard(keyboard);
    }

    /// <summary>
    /// What the keyboard was on: the terminal whose native window had it, else the terminal shown last while it is visible, else the control
    /// that has WPF focus. A click on the status bar moves WPF focus but not the native one (F55), so the last shown terminal is asked too.
    /// </summary>
    private UIElement? KeyboardTarget() =>
        NativeFocusedTerminal() ?? (_focusTerminal is { IsVisible: true } last ? last : null) ?? Keyboard.FocusedElement as UIElement;

    /// <summary>Gives the keyboard back to a control, after the palette has closed. It is queued, as ShowOnly queues its own focus.</summary>
    private void ReturnKeyboard(UIElement? target)
    {
        if (target is null) return;
        Dispatcher.BeginInvoke(new Action(() => target.Focus()), DispatcherPriority.Input);
    }

    /// <summary>
    /// The palette's entries, in their order: the sessions that wait for the owner, the other sessions, the pages, then the actions. The
    /// session entries are the rail's live rows; a session in a window of its own is left out (its terminal is not in this window).
    /// </summary>
    private List<PaletteItem> PaletteItems()
    {
        var items = new List<PaletteItem>();
        var waiting = _attention.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        var sessions = _railRecords.Values
            .Where(record => !string.IsNullOrEmpty(record.SessionId) && !IsInOwnWindow(record.SessionId))
            .OrderBy(record => waiting.Contains(record.Name) ? 0 : 1)
            .ThenBy(record => record.Name, StringComparer.Ordinal);
        foreach (var record in sessions)
        {
            var sessionId = record.SessionId;
            var listed = _agents.FirstOrDefault(agent => SameId(agent.SessionId, sessionId));
            var entry = new PaletteEntry("session:" + sessionId, "Go to session " + LiveTitle(record), StateWithWaiting(listed),
                waiting.Contains(record.Name) ? 0 : 1);
            items.Add(new PaletteItem(entry, () => GoToSession(sessionId, LiveTitle(record))));
        }

        items.Add(PageItem("timeline", "Open Timeline", null, () => TimelineButton_Click(TimelineButton, new RoutedEventArgs())));
        items.Add(PageItem("storage", "Open Storage", null, () => StorageButton_Click(StorageButton, new RoutedEventArgs())));
        items.Add(PageItem("limits", "Open Limits", null, () => LimitsButton_Click(LimitsButton, new RoutedEventArgs())));
        items.Add(PageItem("away", "Open Away", null, () => AwayButton_Click(AwayButton, new RoutedEventArgs())));
        // The window has no Library page: its past sessions are listed under the rail's Archived filter, so the entry sets that filter.
        items.Add(PageItem("library", "Show archived sessions (Library)", "rail filter: Archived", () => ChooseFilter(RailFilter.Archived)));
        items.Add(PageItem("search", "Search what was said", null, SearchFromPalette));
        items.Add(PageItem("new-session", "New session setup", null, () => OpenSetupTab(null)));

        items.Add(ActionItem("refresh", "Refresh now", "the pass that runs every 15 seconds", () => _ = PassAndRefreshAsync()));
        // The re-alerts are only silenced, never answered: nothing is typed into a session (escalation D3, D4).
        var needing = _attention.Where(AttentionEscalation.Escalates).Select(item => item.Name).Distinct(StringComparer.Ordinal).ToList();
        if (needing.Count > 0)
        {
            var count = needing.Count == 1 ? "1 session" : $"{needing.Count} sessions";
            items.Add(ActionItem("ack-all", "Acknowledge all waiting", count, () =>
            {
                foreach (var name in needing) AcknowledgeNeed(name);
            }));
            items.Add(ActionItem("snooze-all", "Snooze all waiting 1 hour", count, () =>
            {
                foreach (var name in needing) SnoozeNeed(name, TimeSpan.FromHours(1));
            }));
        }
        items.Add(ActionItem("notify", "Toggle sound and flash for new waits", null, ToggleNotify));
        return items;
    }

    private static PaletteItem PageItem(string id, string title, string? hint, Action run) =>
        new(new PaletteEntry("page:" + id, title, hint, 2), run);

    private static PaletteItem ActionItem(string id, string title, string? hint, Action run) =>
        new(new PaletteEntry("action:" + id, title, hint, 3), run, ReturnsKeyboard: true);

    /// <summary>
    /// Shows a session's tab and its terminal, as a click on the tab does. A session whose tab the owner closed gets its tab back, and
    /// the rail row is selected when the rail shows it.
    /// </summary>
    private void GoToSession(string sessionId, string title)
    {
        _hiddenSessions.Remove(sessionId);
        var tab = TabFor(TabKind.Session, sessionId, () => title);
        ActivateTab(tab);
    }

    /// <summary>Puts the search box in front of the owner, opening the rail first when it is hidden.</summary>
    private void SearchFromPalette()
    {
        if (_railCollapsed) ExpandRail();
        SearchBox.Focus();
    }

    /// <summary>
    /// Turns sound and flash for new waits on or off in ui.json, the same key the attention code reads each pass. The status line says what the
    /// file holds after the write, so a file that cannot be written does not show as changed.
    /// </summary>
    private void ToggleNotify()
    {
        var on = AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, NotifyKey));
        UiFile.Set(_options.StatePath, NotifyKey, JsonValue.Create(!on)!);
        var now = AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, NotifyKey));
        AppLog.Write($"sound and flash for new waits: {(now ? "on" : "off")} (from the command palette)");
        SetStatus($"Sound and flash for new waits: {(now ? "on" : "off")}.");
    }
}

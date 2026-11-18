using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Library;
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
    /// The project folder's name of each live session, by session id, as the last rail refresh drew it. The palette matches it as a keyword.
    /// Replaced on the UI thread only, by <see cref="RefreshRail"/>, together with <see cref="_railRecords"/>.
    /// </summary>
    private IReadOnlyDictionary<string, string> _railFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            var hint = StateWithWaiting(listed);
            var entry = new PaletteEntry("session:" + sessionId, "Go to session " + LiveTitle(record), hint,
                waiting.Contains(record.Name) ? 0 : 1, KeywordsOf(sessionId, hint));
            items.Add(new PaletteItem(entry, () => GoToSession(sessionId, LiveTitle(record))));
        }

        // The four pages are sub-tabs of Insights (2026-11-18): their entries open Insights on that sub-tab.
        items.Add(PageItem("timeline", "Open Timeline", null, () => OpenInsights(InsightsTab.Timeline)));
        items.Add(PageItem("storage", "Open Storage", null, () => OpenInsights(InsightsTab.Storage)));
        items.Add(PageItem("limits", "Open Limits", null, () => OpenInsights(InsightsTab.Usage)));
        items.Add(PageItem("away", "Open Away", null, () => OpenInsights(InsightsTab.Away)));
        items.Add(PageItem("insights", "Open Insights", "the sub-tab shown last", () => OpenInsights(_insights.Current)));
        items.Add(PageItem("settings", "Open Settings", null, () => OpenSettings(_settings.Current)));
        // The window has no Library page: its past sessions are listed under the rail's Archived filter, so the entry sets that filter.
        items.Add(PageItem("library", "Show archived sessions (Library)", "rail filter: Archived", () => ChooseFilter(RailFilter.Archived)));
        items.Add(PageItem("search", "Search what was said", null, SearchFromPalette));
        items.Add(PageItem("new-session", "New session setup", null, () => OpenSetupTab(null)));

        // The next waiting session (Ctrl+Shift+N), counted from the session that is active as the palette opens. Hidden when no other session
        // waits. It navigates, so it does not return the keyboard.
        var from = ActiveSessionId();
        if (NextWaitingId(from) is not null)
            items.Add(new PaletteItem(new PaletteEntry("action:next-waiting", "Go to next waiting session", "Ctrl+Shift+N", 3),
                () => GoToNextWaitingFrom(from)));
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
        // Marking is a note in ui.json only: nothing is sent to a session (ready-for-review D4).
        if (_review.Count > 0)
            items.Add(ActionItem("review-all", "Mark all ready sessions reviewed", _review.Count == 1 ? "1 session" : $"{_review.Count} sessions",
                MarkAllReviewed));
        // The tag filter (tags plan, Part A3): one entry per tag in use, and Clear when one is set. The keyboard route to the rail's dropdown.
        items.AddRange(TagPaletteItems());
        // The magic rename (magic-rename plan): Haiku names cold past sessions, after a confirm. Nothing is sent to a session.
        items.AddRange(MagicPaletteItems());
        items.Add(ActionItem("notify", "Toggle sound and flash for new waits", null, ToggleNotify));
        items.Add(ActionItem("toast", "Toggle toasts", "when the window is in the background", ToggleToasts));
        // Quick capture (ideas #7, SessionWindow.Ideas.cs): an idea goes to ideas.md, never to a session.
        items.Add(ActionItem("add-idea", "Add idea…", "saved to ideas.md, not sent to a session", AddIdea));
        items.Add(ActionItem("open-ideas", "Open ideas file", "ideas.md beside the state file", OpenIdeas));
        return items;
    }

    /// <summary>
    /// The palette's entries for the tag filter (tags plan, Part A3): "Filter by tag #x" for each tag in use, with its session count, and
    /// "Clear tag filter" while one is set. Read from memory only.
    /// </summary>
    private List<PaletteItem> TagPaletteItems()
    {
        var items = new List<PaletteItem>();
        foreach (var tag in TagIndex.Counts(_marks.Values))
        {
            var count = tag.Count == 1 ? "1 session" : $"{tag.Count} sessions";
            items.Add(ActionItem("tag:" + tag.Tag, $"Filter by tag #{tag.Tag}", count, () => ChooseTag(tag.Tag)));
        }
        if (ActiveTagFilter is { } active) items.Add(ActionItem("tag-clear", "Clear tag filter", "#" + active, () => ChooseTag(null)));
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

    /// <summary>
    /// Shows the session that waits next, after the active one: Ctrl+Shift+N and its palette entry. It goes through <see cref="GoToSession"/>, so the
    /// wait is acknowledged as a click acknowledges it. Nothing is sent to any session. With no other session waiting, the status line says so.
    /// </summary>
    private void GoToNextWaiting() => GoToNextWaitingFrom(ActiveSessionId());

    /// <summary>The same step, counted from the session that was active when the step was offered (the palette takes it as it opens).</summary>
    private void GoToNextWaitingFrom(string? from)
    {
        var next = NextWaitingId(from);
        if (next is null)
        {
            SetStatus("No other session is waiting.");
            return;
        }
        var record = _railRecords.Values.FirstOrDefault(r => SameId(r.SessionId, next));
        GoToSession(next, record is null ? $"Session {ShortId(next)}" : LiveTitle(record));
    }

    /// <summary>
    /// The session that the next-waiting step goes to, or null. A session in a window of its own is left out, as the palette leaves it out, since
    /// its terminal is not in this window.
    /// </summary>
    private string? NextWaitingId(string? from)
    {
        var reachable = _attention.Where(item => string.IsNullOrEmpty(item.SessionId) || !IsInOwnWindow(item.SessionId)).ToList();
        return AttentionList.NextWaiting(reachable, from);
    }

    /// <summary>
    /// The session the owner is on: the active tab of the right pane when the keyboard is in that pane, else the active tab of the left pane.
    /// Null when that tab is not a session (the setup, a transcript or a page), so the next-waiting step starts from the first waiting session.
    /// </summary>
    private string? ActiveSessionId()
    {
        var inRight = KeyboardTarget() is { } focused && focused.IsDescendantOf(PaneB) && _activeTabRight is not null;
        var tab = inRight ? _activeTabRight : _activeTab;
        return tab is { Kind: TabKind.Session } ? tab.Key : null;
    }

    /// <summary>
    /// The text a session entry matches besides its title: its project folder's name, its tags (the library's, as the rail row shows them) and
    /// its state. Empty words are left out. The palette ranks a keyword match below any title match.
    /// </summary>
    private string? KeywordsOf(string sessionId, string state)
    {
        var parts = new List<string>();
        if (_railFolders.TryGetValue(sessionId, out var folder)) parts.Add(folder);
        if (MarkOf(sessionId)?.Tags is { } tags) parts.AddRange(tags);
        parts.Add(state);
        var keywords = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return keywords.Length == 0 ? null : keywords;
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
    private void ToggleNotify() =>
        WriteFlag(NotifyKey, !AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, NotifyKey)), "Sound and flash for new waits");

    /// <summary>
    /// Turns the toast for new waits on or off in ui.json, the key the announcement reads (toasts D3). Like the sound, it is a setting only:
    /// "notify": false still silences toasts too.
    /// </summary>
    private void ToggleToasts() =>
        WriteFlag(ToastKey, !AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, ToastKey)), "Toasts when the window is in the background");
}

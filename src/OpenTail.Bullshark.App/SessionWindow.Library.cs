using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The owner's marks on past sessions in the rail (session library, L3 and L4). The marks are read from library.json beside the
/// state file. Every change goes through <see cref="ChangeMark"/>: the row menu, the edit dialog and any self-test call the same
/// setters, so there is one path to the file.
/// </summary>
public partial class SessionWindow
{
    private SessionLibrary _library = null!;

    /// <summary>
    /// Claude's marks as they were last read, by session id. Replaced, never edited in place. Every Claude path reads these, whatever the
    /// dropdown shows: the background ones (limit wake, magic rename, the Claude tab titles) are not Codex's.
    /// </summary>
    private Dictionary<string, LibraryEntry> _marks = new(StringComparer.Ordinal);

    /// <summary>Codex's marks as they were last read, by session id: the codex: entries of the same file (D1). The Codex rows read these.</summary>
    private Dictionary<string, LibraryEntry> _codexMarks = new(StringComparer.Ordinal);

    /// <summary>Copilot's marks as they were last read, by session id: the copilot: entries of the same file (G2.3). The Copilot rows read these.</summary>
    private Dictionary<string, LibraryEntry> _copilotMarks = new(StringComparer.Ordinal);

    /// <summary>Why library.json cannot be read, or null when it can (D-L3). While it is set, no mark is shown and nothing is written.</summary>
    private string? _libraryError;

    private void InitLibrary(string statePath)
    {
        _library = SessionLibrary.Beside(statePath, AppLog.Write);
        LoadLibrary();
        _transcript.OpenSessionRequested += OpenRailSession;
    }

    /// <summary>
    /// Reads the marks. A file that cannot be read gives no marks and a status line, and the rail still shows (D-L3). Only the
    /// first failure sets the status, so a rail refresh every 15 seconds does not keep overwriting other status text.
    /// </summary>
    private void LoadLibrary()
    {
        try
        {
            // The UI thread waits briefly for the lock (reliability batch R7). A busy file keeps the marks shown until the next read.
            // One read gives both providers' views (D1): Claude's, as before, and Codex's for the Codex rows.
            var all = _library.LoadAll(UiFile.UiWait);
            _marks = SessionLibrary.ProviderView(all, ProviderId.Claude);
            _codexMarks = SessionLibrary.ProviderView(all, ProviderId.Codex);
            _copilotMarks = SessionLibrary.ProviderView(all, ProviderId.Copilot);
            _libraryError = null;
            ReadBusy.Note(false);
        }
        catch (TimeoutException)
        {
            ReadBusy.Note(true);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            _marks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            _codexMarks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            _copilotMarks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            if (_libraryError is null)
            {
                AppLog.Write($"library not read: {ex.GetType().Name}");
                SetStatus($"The library cannot be read, so its names, pins, tags and colours are not shown, and nothing is written until it can be. The library could not be read ({ex.GetType().Name}).");
            }
            _libraryError = ex.GetType().Name;
        }
    }

    private LibraryEntry? MarkOf(string sessionId) => _marks.TryGetValue(sessionId, out var entry) ? entry : null;

    /// <summary>The marks of one provider as last read (D1): Codex's for a Codex row, Claude's for everything else.</summary>
    private Dictionary<string, LibraryEntry> MarksOf(ProviderId provider) =>
        provider == ProviderId.Codex ? _codexMarks : provider == ProviderId.Copilot ? _copilotMarks : _marks;

    /// <summary>A session's mark under one provider's key, or null (D1).</summary>
    private LibraryEntry? MarkOf(ProviderId provider, string sessionId) => MarksOf(provider).TryGetValue(sessionId, out var entry) ? entry : null;

    /// <summary>A past session's title as the owner sees it (D-L1): the owner's name when there is one, else the transcript's title.</summary>
    private string DisplayTitle(SessionDigest digest)
    {
        // The session this one continues, from the history, for the title of a resumed session with no title of its own (L5).
        var earlier = digest.ContinuesSessionId is { Length: > 0 } id
            ? _history?.FirstOrDefault(d => string.Equals(d.SessionId, id, StringComparison.OrdinalIgnoreCase))
            : null;
        return SessionLibrary.TitleFor(digest, MarkOf(digest.SessionId), earlier);
    }

    /// <summary>A live session's title: the owner's name when there is one, else the name the session was started with.</summary>
    private string LiveTitle(SessionRecord record) =>
        MarkOf(record.SessionId)?.Name is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : record.Name;

    /// <summary>The title of a session's tab, whichever list it came from. The records are passed in, so a strip reads them once.</summary>
    private string TitleOfSessionNow(string sessionId, IEnumerable<SessionRecord> records)
    {
        var record = records.FirstOrDefault(r => SameId(r.SessionId, sessionId));
        if (record is not null) return LiveTitle(record);
        var digest = _history?.FirstOrDefault(d => SameId(d.SessionId, sessionId));
        return digest is not null ? DisplayTitle(digest) : $"Session {ShortId(sessionId)}";
    }

    /// <summary>Keeps the tab titles in step with the owner's names, which can change while a tab is open. Reads the records once.</summary>
    private void RefreshTabTitles()
    {
        // The Claude tabs follow the Claude marks and records, as before (C4).
        var tabs = _tabs.Where(t => t.Provider == ProviderId.Claude && t.Kind is (TabKind.Session or TabKind.Transcript)).ToList();
        if (tabs.Count > 0)
        {
            var records = ReadRecords().Values.ToList();
            foreach (var tab in tabs) tab.Title = TitleOfSessionNow(tab.Key, records);
        }
        // A Codex tab follows its history row's title (the owner's name, else the Codex title) while the session is in the history (D1). A tab
        // of a new session keeps its title until the session is listed.
        foreach (var tab in _tabs.Where(t => t.Provider == ProviderId.Codex && t.Kind == TabKind.Session).ToList())
        {
            if (_codexSessions.FirstOrDefault(s => SameId(s.Key.SessionId, tab.Key)) is { } session)
            {
                // An ended tab keeps its suffix when the row's title is applied again (R6).
                var title = CodexRowTitle(MarkOf(ProviderId.Codex, tab.Key)?.Name, session);
                tab.Title = tab.Ended ? CodexExit.EndedTitle(title) : title;
            }
        }
        // A Copilot tab follows its history row's title the same way (G2.7).
        foreach (var tab in _tabs.Where(t => t.Provider == ProviderId.Copilot && t.Kind == TabKind.Session).ToList())
        {
            if (_copilotSessions.FirstOrDefault(s => SameId(s.Key.SessionId, tab.Key)) is { } session)
            {
                var title = CopilotRowTitle(MarkOf(ProviderId.Copilot, tab.Key)?.Name, session);
                tab.Title = tab.Ended ? CopilotExit.EndedTitle(title) : title;
            }
        }
    }

    /// <summary>
    /// Changes one session's mark and writes the file under its lock. A file that cannot be read is not written: the status line
    /// says so, and the marks stay as they were (D-L3). Returns true when the file was written.
    /// </summary>
    private bool ChangeMark(string sessionId, Func<LibraryEntry, LibraryEntry> change) => ChangeMarkOf(ProviderId.Claude, sessionId, change);

    /// <summary>
    /// <see cref="ChangeMark"/> under one provider's key (D1): a Codex row writes its codex: entry, a Claude row writes Claude's. The same checks and
    /// the same refresh apply to both.
    /// </summary>
    private bool ChangeMarkOf(ProviderId provider, string sessionId, Func<LibraryEntry, LibraryEntry> change)
    {
        if (_libraryError is not null)
        {
            SetStatus($"Not saved: the library cannot be read ({_libraryError}). Nothing is written until it can be.");
            return false;
        }
        try
        {
            ApplyMark(_library, provider, sessionId, change);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"library not changed: {ex.GetType().Name}");
            SetStatus($"Not saved ({ex.GetType().Name}).");
            RefreshRail();
            return false;
        }
        // RefreshRail reads the marks again, so the success path reads them once.
        RefreshRail();
        RefreshTabTitles();
        return true;
    }

    /// <summary>
    /// The one write of a mark (D1): under the file lock, one session's entry under one provider is changed. Static, so a test can run it on a
    /// temporary library. A change that leaves the entry empty removes it (<see cref="SessionLibrary.UpdateProvider"/>).
    /// </summary>
    internal static void ApplyMark(SessionLibrary library, ProviderId provider, string sessionId, Func<LibraryEntry, LibraryEntry> change) =>
        library.UpdateProvider(provider, entries =>
        {
            var current = entries.TryGetValue(sessionId, out var found) ? found : new LibraryEntry();
            entries[sessionId] = change(current);
        });

    public void SetPinned(string sessionId, bool pinned) => ChangeMark(sessionId, mark => mark with { Pinned = pinned });

    public void SetArchived(string sessionId, bool archived) => ChangeMark(sessionId, mark => mark with { Archived = archived });

    /// <summary>The owner's "Wake after the limit resets" choice for a session (limit wake). Written like the other marks.</summary>
    public void SetAutoWake(string sessionId, bool on) => ChangeMark(sessionId, mark => mark with { AutoWake = on });

    /// <summary>The owner's name. Blank removes it, so the transcript's title shows again.</summary>
    public void SetName(string sessionId, string? name) =>
        ChangeMark(sessionId, mark => WithName(mark, name));

    /// <summary>
    /// The name set by the owner. A name that differs from the one before makes it the owner's: the magic marks are cleared (magic rename, D6),
    /// so the wand goes. The same name keeps them.
    /// </summary>
    private static LibraryEntry WithName(LibraryEntry mark, string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (string.Equals(trimmed, mark.Name?.Trim(), StringComparison.Ordinal)) return mark with { Name = trimmed };
        return mark with { Name = trimmed, MagicAt = null, MagicNamedAt = null };
    }

    /// <summary>The tags typed in one box (D-L6).</summary>
    public void SetTags(string sessionId, string text) =>
        ChangeMark(sessionId, mark => mark with { Tags = SessionLibrary.TagsOf(text) });

    public void SetNote(string sessionId, string? note) =>
        ChangeMark(sessionId, mark => mark with { Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });

    /// <summary>One of the eight colours (D-L11), or null for none.</summary>
    public void SetColour(string sessionId, string? colour) =>
        ChangeMark(sessionId, mark => mark with { Colour = string.IsNullOrEmpty(colour) ? null : colour });

    /// <summary>The name, tags, note, colour and plan file from the edit dialog, in one write.</summary>
    public void SetDetails(string sessionId, LibraryEntry details) => ChangeMark(sessionId, mark => WithDetails(mark, details));

    /// <summary>The edit dialog's fields set on a mark. Shared by Claude's rows and Codex's (D1).</summary>
    private static LibraryEntry WithDetails(LibraryEntry mark, LibraryEntry details) => WithName(mark, details.Name) with
    {
        Tags = details.Tags ?? [],
        Note = string.IsNullOrWhiteSpace(details.Note) ? null : details.Note.Trim(),
        Colour = string.IsNullOrEmpty(details.Colour) ? null : details.Colour,
        PlanFile = PlanProgress.PathOf(details.PlanFile),
    };

    /// <summary>
    /// The plan counts of a session's plan file, read through the cache, or null when the session has no plan file or it cannot be read (plan
    /// progress, Part 2). The counts are cached per file; the text is never logged. Codex rows read Codex's marks (parity P3).
    /// </summary>
    private PlanProgressCount? PlanOf(ProviderId provider, string sessionId) =>
        MarkOf(provider, sessionId)?.PlanFile is { } path ? _planProgress.Get(path) : null;

    /// <summary>The plan figure of a row, "3/7": the checked items of all the items. Null when there is no plan file, or it has no items.</summary>
    private string? PlanFigure(ProviderId provider, string sessionId) =>
        PlanOf(provider, sessionId) is { Total: > 0 } plan ? $"{plan.Done}/{plan.Total}" : null;

    /// <summary>The Claude row's plan figure (the rail's live and history rows use Claude's marks).</summary>
    private string? PlanFigure(string sessionId) => PlanFigure(ProviderId.Claude, sessionId);

    /// <summary>
    /// The plan line at the top of a row's tooltip: "Plan: 3 of 7 done. Next: ...", with the first open item's text. Empty when the row shows no
    /// plan figure. The plan's text is in the tooltip only, never in the log.
    /// </summary>
    private string PlanTip(LibraryEntry? mark)
    {
        if (mark?.PlanFile is not { } path || _planProgress.Get(path) is not { Total: > 0 } plan) return string.Empty;
        var next = plan.Next is null ? string.Empty : " Next: " + plan.Next;
        return $"Plan: {plan.Done} of {plan.Total} done.{next}\n";
    }

    /// <summary>The text the search reads besides the messages: the owner's name, tags and note (D-L7).</summary>
    private static string? SearchableText(IReadOnlyDictionary<string, LibraryEntry> marks, string sessionId)
    {
        if (!marks.TryGetValue(sessionId, out var mark)) return null;
        var parts = new[] { mark.Name, string.Join(' ', mark.Tags ?? []), mark.Note };
        var text = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return text.Length == 0 ? null : text;
    }

    /// <summary>The tags as words after a row's "N msgs", or nothing (D-L4 row, D-L6).</summary>
    private static string TagsSuffix(LibraryEntry? mark) =>
        mark?.Tags is { Count: > 0 } tags ? " · " + string.Join(" ", tags) : string.Empty;

    /// <summary>
    /// The tags and the note at the top of a row's tooltip, when there are any. The tags are also on the second line, but a narrow
    /// rail cuts that line, so the tooltip is where they can always be read.
    /// </summary>
    private static string NoteTip(LibraryEntry? mark)
    {
        var lines = new List<string>();
        if (mark?.Tags is { Count: > 0 } tags) lines.Add("Tags: " + string.Join(" ", tags));
        if (!string.IsNullOrWhiteSpace(mark?.Note)) lines.Add($"Note: {mark!.Note}");
        return lines.Count == 0 ? string.Empty : string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// The line that names the session a resumed session continues (D-L8, L5), with that session's title when it is in the history.
    /// Empty for a session that continues none.
    /// </summary>
    private string ContinuesTip(SessionDigest digest)
    {
        if (digest.ContinuesSessionId is not { Length: > 0 } earlier) return string.Empty;
        var source = _history?.FirstOrDefault(d => string.Equals(d.SessionId, earlier, StringComparison.OrdinalIgnoreCase));
        return $"continues: {(source is not null ? DisplayTitle(source) : "session " + ShortId(earlier))}\n";
    }

    /// <summary>
    /// A row with its session's colour (D-L11): a thin bar at its left edge. Without a colour the bar keeps its width, so the
    /// text of every row starts at the same place.
    /// </summary>
    private UIElement ColourEdge(UIElement content, string? colour)
    {
        var hex = Palette.ColourHex(colour);
        return new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = hex is null ? Brushes.Transparent : RailPalette.Brush(hex),
            Child = content,
        };
    }

    /// <summary>The brush of a session's colour on the tab's edge, or null for none. Claude's colour.</summary>
    private Brush? TabEdgeBrush(string sessionId) => TabEdgeBrush(ProviderId.Claude, sessionId);

    /// <summary>The brush of a session's colour under one provider's key, on the tab's edge (D1), or null for none.</summary>
    private Brush? TabEdgeBrush(ProviderId provider, string sessionId)
    {
        var hex = Palette.ColourHex(MarkOf(provider, sessionId)?.Colour);
        return hex is null ? null : RailPalette.Brush(hex);
    }

    /// <summary>
    /// The right-click menu of a session row (D-L10). Pin and Archive are offered on past sessions only: a running session is not
    /// archived from the rail. Edit opens the dialog for the name, tags, note and colour.
    /// </summary>
    private ContextMenu RowMenu(string sessionId, bool history, string title)
    {
        var mark = MarkOf(sessionId) ?? new LibraryEntry();
        var menu = StyledMenu();
        if (history)
        {
            menu.Items.Add(MenuItemFor(mark.Pinned ? "Unpin" : "Pin", () => SetPinned(sessionId, !mark.Pinned)));
            menu.Items.Add(MenuItemFor(mark.Archived ? "Unarchive" : "Archive", () => SetArchived(sessionId, !mark.Archived)));
        }
        // A live session's changes, read from its folder (item 2). A past session has no folder to read: its transcript is its record.
        if (!history) menu.Items.Add(MenuItemFor("Changes…", () => OpenChanges(sessionId, title)));
        // The owner's choice to start the session again after its usage limit resets (limit wake, 2026-11-20). Live and past sessions alike.
        menu.Items.Add(WakeMenuItem.Create(mark.AutoWake, on => SetAutoWake(sessionId, on)));
        menu.Items.Add(MenuItemFor("Edit…", () => EditMark(sessionId, title)));
        return menu;
    }

    /// <summary>Opens the edit dialog and writes what it returns in one change. Nothing is written when it is cancelled.</summary>
    private void EditMark(string sessionId, string fallbackTitle) => EditMarkOf(ProviderId.Claude, sessionId, fallbackTitle);

    /// <summary>
    /// <see cref="EditMark"/> for one provider's row (D1): the dialog shows that provider's mark, and its result is written under that provider's key.
    /// </summary>
    private void EditMarkOf(ProviderId provider, string sessionId, string fallbackTitle)
    {
        if (_libraryError is not null)
        {
            SetStatus($"Not edited: the library cannot be read ({_libraryError}).");
            return;
        }
        var mark = MarkOf(provider, sessionId) ?? new LibraryEntry();
        var dialog = new LibraryEditDialog(mark, fallbackTitle, Palette) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } details) return;
        ChangeMarkOf(provider, sessionId, current => WithDetails(current, details));
    }

    private ContextMenu StyledMenu()
    {
        var palette = Palette;
        var menu = new ContextMenu
        {
            HasDropShadow = true,
            Background = RailPalette.Brush(palette.Panel),
            BorderBrush = RailPalette.Brush(palette.Muted),
            Foreground = RailPalette.Brush(palette.Text),
        };
        menu.Resources[SystemColors.MenuBrushKey] = RailPalette.Brush(palette.Panel);
        menu.Resources[SystemColors.MenuTextBrushKey] = RailPalette.Brush(palette.Text);
        menu.Resources[SystemColors.ControlBrushKey] = RailPalette.Brush(palette.Panel);
        menu.Resources[SystemColors.ControlTextBrushKey] = RailPalette.Brush(palette.Text);
        menu.Resources[SystemColors.HighlightBrushKey] = RailPalette.Brush(palette.Selection);
        menu.Resources[SystemColors.HighlightTextBrushKey] = RailPalette.Brush(palette.SelectionText);
        return menu;
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header, Padding = new Thickness(10, 4, 16, 4) };
        item.Click += (_, _) => action();
        return item;
    }
}

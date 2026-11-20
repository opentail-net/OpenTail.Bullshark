using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;
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

    /// <summary>The marks as they were last read. Replaced, never edited in place.</summary>
    private Dictionary<string, LibraryEntry> _marks = new(StringComparer.Ordinal);

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
            _marks = new Dictionary<string, LibraryEntry>(_library.Load(UiFile.UiWait), StringComparer.Ordinal);
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
            if (_libraryError is null)
            {
                AppLog.Write($"library not read: {ex.Message}");
                SetStatus($"The library cannot be read, so its names, pins, tags and colours are not shown, and nothing is written until it can be. {ex.Message}");
            }
            _libraryError = ex.Message;
        }
    }

    private LibraryEntry? MarkOf(string sessionId) => _marks.TryGetValue(sessionId, out var entry) ? entry : null;

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
        var tabs = _tabs.Where(t => t.Kind is TabKind.Session or TabKind.Transcript).ToList();
        if (tabs.Count == 0) return;
        var records = ReadRecords().Values.ToList();
        foreach (var tab in tabs) tab.Title = TitleOfSessionNow(tab.Key, records);
    }

    /// <summary>
    /// Changes one session's mark and writes the file under its lock. A file that cannot be read is not written: the status line
    /// says so, and the marks stay as they were (D-L3). Returns true when the file was written.
    /// </summary>
    private bool ChangeMark(string sessionId, Func<LibraryEntry, LibraryEntry> change)
    {
        if (_libraryError is not null)
        {
            SetStatus($"Not saved: the library cannot be read ({_libraryError}). Nothing is written until it can be.");
            return false;
        }
        try
        {
            _library.Update(entries =>
            {
                var current = entries.TryGetValue(sessionId, out var found) ? found : new LibraryEntry();
                entries[sessionId] = change(current);
            });
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"library not changed: {ex.Message}");
            SetStatus($"Not saved: {ex.Message}");
            RefreshRail();
            return false;
        }
        // RefreshRail reads the marks again, so the success path reads them once.
        RefreshRail();
        RefreshTabTitles();
        return true;
    }

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
    public void SetDetails(string sessionId, LibraryEntry details) => ChangeMark(sessionId, mark => WithName(mark, details.Name) with
    {
        Tags = details.Tags ?? [],
        Note = string.IsNullOrWhiteSpace(details.Note) ? null : details.Note.Trim(),
        Colour = string.IsNullOrEmpty(details.Colour) ? null : details.Colour,
        PlanFile = PlanProgress.PathOf(details.PlanFile),
    });

    /// <summary>
    /// The plan counts of a session's plan file, read through the cache, or null when the session has no plan file or it cannot be read (plan
    /// progress, Part 2). The counts are cached per file; the text is never logged.
    /// </summary>
    private PlanProgressCount? PlanOf(string sessionId) =>
        MarkOf(sessionId)?.PlanFile is { } path ? _planProgress.Get(path) : null;

    /// <summary>The plan figure of a row, "3/7": the checked items of all the items. Null when there is no plan file, or it has no items.</summary>
    private string? PlanFigure(string sessionId) =>
        PlanOf(sessionId) is { Total: > 0 } plan ? $"{plan.Done}/{plan.Total}" : null;

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

    /// <summary>The brush of a session's colour on the tab's edge, or null for none.</summary>
    private Brush? TabEdgeBrush(string sessionId)
    {
        var hex = Palette.ColourHex(MarkOf(sessionId)?.Colour);
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
    private void EditMark(string sessionId, string fallbackTitle)
    {
        if (_libraryError is not null)
        {
            SetStatus($"Not edited: the library cannot be read ({_libraryError}).");
            return;
        }
        var mark = MarkOf(sessionId) ?? new LibraryEntry();
        var dialog = new LibraryEditDialog(mark, fallbackTitle, Palette) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } details) return;
        SetDetails(sessionId, details);
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

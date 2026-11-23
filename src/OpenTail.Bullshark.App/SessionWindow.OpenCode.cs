using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// opencode in the window (OC2, copied from the Copilot partial, OC-D16): the opencode rows of the history, read from opencode's database read-only
/// (OC-D2 to OC-D4), the row menu, the terminal tab of a resume (OC-D8) and the rail's status line (OC-D7). The rows are history only: a row resumes in a
/// terminal tab, and its menu opens a read-only transcript. Nothing is supervised. Claude's, Codex's and Copilot's tabs and records are not changed.
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// The Continue refusal of an opencode session (<see cref="ContinueScreenRefusalOf"/>). Continue does not open the opencode resume screen yet; "Resume…"
    /// does (<see cref="ResumeOpenCodeScreen"/>).
    /// </summary>
    private const string OpenCodeResumeScreenNotBuilt = "The opencode resume screen is not built yet.";

    /// <summary>The opencode sessions as the last read gave them: root sessions, newest first (OC-D3).</summary>
    private IReadOnlyList<SessionSummary> _openCodeSessions = [];

    /// <summary>The status of the last read (OC-D7). The rail shows its fixed phrase; a status other than Ok shows no rows.</summary>
    private OpenCodeHistoryStatus _openCodeStatus = OpenCodeHistoryStatus.Ok;

    /// <summary>The keys of the sessions opencode archived in the last read (OC-D4). Read only: the database is never written.</summary>
    private IReadOnlySet<SessionKey> _openCodeArchived = new HashSet<SessionKey>();

    /// <summary>Rows of the last read that were not listed or not used (OC-D3, OC-D5): counted, not shown.</summary>
    private int _openCodeSkippedRows;

    /// <summary>The database location of the last read, or null before the first read. Its file path is shown in the empty state.</summary>
    private DatabasePath? _openCodeLocation;

    /// <summary>The tag the opencode rail shows rows for, or null. The shared tag menu sets it (<see cref="ChooseTag"/>, OC2d).</summary>
    private string? _openCodeTagFilter = null;

    /// <summary>The view the opencode rail shows (All, History or Archived, as Copilot's). The shared view menu sets it (<see cref="ChooseOpenCodeView"/>, OC2d).</summary>
    private RailFilter _openCodeView = RailFilter.All;

    /// <summary>True once the opencode history has been read in this window.</summary>
    private bool _openCodeRead;

    /// <summary>True while the opencode history is being read, so a second read does not start.</summary>
    private bool _openCodeLoading;

    /// <summary>
    /// The opencode read that is running, or null when none is. A second caller is given this task and waits for the read (the Copilot rule, S1). Set when
    /// a read starts, cleared when it ends.
    /// </summary>
    private Task? _openCodeLoadTask;

    /// <summary>True when the last read threw. The rail then says so, and the status line says why.</summary>
    private bool _openCodeFailed;

    /// <summary>The terminal command builder of the resume (OC-D8). It starts nothing.</summary>
    private static readonly OpenCodeTerminalOpener OpenCodeOpener = new();

    /// <summary>The window's provider is opencode.</summary>
    private bool IsOpenCodeActive => _activeProvider.Id == ProviderId.OpenCode;

    /// <summary>The tag the opencode rail filters by, or null. Nothing is filtered while library.json cannot be read (D-L3).</summary>
    private string? ActiveOpenCodeTagFilter => _libraryError is null ? _openCodeTagFilter : null;

    /// <summary>
    /// The mark an opencode session is listed and drawn with: its owner's mark, with Archived set when opencode archived the session (OC-D4). A session
    /// opencode archived and the owner has not marked gets a mark of its own, with only Archived set. Pure.
    /// </summary>
    internal static LibraryEntry? EffectiveOpenCodeMark(LibraryEntry? mark, bool archivedInOpenCode) =>
        archivedInOpenCode && mark?.Archived != true ? (mark ?? new LibraryEntry()) with { Archived = true } : mark;

    /// <summary>The location of opencode's database from the environment and the user's profile (OC-D2). Reads the variables; calls nothing.</summary>
    internal static DatabasePath CurrentOpenCodeLocation()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [OpenCodeDatabase.DatabaseVariable] = Environment.GetEnvironmentVariable(OpenCodeDatabase.DatabaseVariable),
            ["XDG_DATA_HOME"] = Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        };
        return OpenCodeDatabase.Resolve(env, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>
    /// The rail under opencode: a header with its reload mark, the status line (the fixed phrase of the read, OC-D7), then the empty state or the failure,
    /// or the pinned sessions, the folders or tag groups and the archived group. No row is drawn when the database could not be read. Replaces the Claude
    /// rows; the Claude tabs are kept in step by <see cref="SyncSessionTabs"/>.
    /// </summary>
    private void RefreshOpenCodeRail(IReadOnlyDictionary<string, SessionRecord> stored)
    {
        _railSignature = null;
        _rebuilding = true;
        try
        {
            Rail.Items.Clear();
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(Label("opencode history", bold: true));
            var reload = Label(_openCodeLoading ? "⏳" : "🔄", muted: true, emoji: true);
            reload.Margin = new Thickness(8, 0, 0, 0);
            reload.Cursor = Cursors.Hand;
            reload.ToolTip = OpenCodeRailText.ReloadTip;
            reload.MouseLeftButtonUp += async (_, _) => await LoadOpenCodeHistoryAsync();
            header.Children.Add(reload);
            var section = NewItem(header, new RailTag(RailKind.HistorySection, "opencode"));
            section.IsExpanded = true;
            Rail.Items.Add(section);

            section.Items.Add(NewItem(Label(OpenCodeRailText.LiveHeading, muted: true), new RailTag(RailKind.Heading, "opencode:live"),
                OpenCodeRailText.LiveTip));

            if (_openCodeFailed)
            {
                section.Items.Add(NewItem(Label(OpenCodeRailText.FailedHeading, muted: true), new RailTag(RailKind.Heading, "opencode:failed"),
                    OpenCodeRailText.FailedTip));
            }
            else if (!_openCodeRead)
            {
                section.Items.Add(NewItem(Label(_openCodeLoading ? OpenCodeRailText.Reading : "not read yet", muted: true),
                    new RailTag(RailKind.Heading, "opencode:none")));
            }
            else
            {
                // The schema line (OC-D7): the fixed phrase of the read, "opencode 1.4 tables" when it was read; the reason otherwise.
                section.Items.Add(NewItem(Label(OpenCodeHistorySource.TextOf(_openCodeStatus), muted: true), new RailTag(RailKind.Heading, "opencode:status")));
                // A database that cannot be read shows no rows (OC-D7): the status line of the window says the same.
                if (_openCodeStatus == OpenCodeHistoryStatus.Ok && _openCodeSessions.Count == 0)
                {
                    section.Items.Add(NewItem(Label(OpenCodeRailText.EmptyHeading, muted: true), new RailTag(RailKind.Heading, "opencode:empty"),
                        OpenCodeRailText.EmptyReason(_openCodeLocation?.FilePath ?? string.Empty, _openCodeSkippedRows)));
                }
                else if (_openCodeStatus == OpenCodeHistoryStatus.Ok)
                {
                    var pinned = OpenCodePinned();
                    var archived = OpenCodeArchived();
                    // The middle of the history is the tag groups under Group by tag, else the folders. The same tag filter applies to both (tags P5).
                    IReadOnlyList<TagGroup<SessionSummary>> tagGroups = _groupByTag ? OpenCodeTagGroups() : [];
                    IReadOnlyList<OpenCodeFolder> folders = _groupByTag ? [] : OpenCodeFolders();
                    var listed = _groupByTag ? tagGroups.Count : folders.Count;
                    // A tag filter or the view that leaves no row says so, so an empty rail is not taken for a fault (tags P5, parity P11).
                    if (pinned.Count == 0 && listed == 0 && archived.Count == 0)
                    {
                        if (ActiveOpenCodeTagFilter is { } tag)
                            section.Items.Add(NewItem(Label(OpenCodeRailText.TagEmpty(tag), muted: true), new RailTag(RailKind.Heading, "opencode:tag-empty"),
                                OpenCodeRailText.TagEmptyTip));
                        else if (_openCodeView == RailFilter.Archived)
                            section.Items.Add(NewItem(Label(OpenCodeRailText.ArchivedEmpty, muted: true), new RailTag(RailKind.Heading, "opencode:archived-empty"),
                                OpenCodeRailText.ArchivedEmptyTip));
                    }
                    if (pinned.Count > 0) section.Items.Add(OpenCodePinnedItem(pinned));
                    if (_groupByTag)
                    {
                        for (int i = 0; i < tagGroups.Count; i++) section.Items.Add(OpenCodeTagItem(tagGroups[i], expandByDefault: i == 0));
                    }
                    else
                    {
                        foreach (var folder in folders) section.Items.Add(OpenCodeFolderItem(folder));
                    }
                    if (archived.Count > 0) section.Items.Add(OpenCodeArchivedItem(archived));
                }
            }
        }
        finally
        {
            _rebuilding = false;
        }
        SyncSessionTabs(stored);
    }

    /// <summary>A folder of opencode sessions: its working folder, or "" for the sessions whose record names none.</summary>
    private sealed record OpenCodeFolder(string Folder, IReadOnlyList<SessionSummary> Sessions);

    /// <summary>
    /// The opencode folders under the view in use, newest first. The sessions in each are newest first too. Under All and History a folder holds the sessions
    /// that are neither archived nor pinned (parity P4); under Archived, the archived sessions (parity P11). The rule is <see cref="ProviderRailGroups.PlaceOf"/>.
    /// </summary>
    private IReadOnlyList<OpenCodeFolder> OpenCodeFolders() =>
        _openCodeSessions
            .Where(session => OpenCodePlaceOf(session) == ProviderPlace.FolderGroups)
            .GroupBy(session => session.Folder ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OpenCodeFolder(group.Key, ProviderRailGroups.Sorted(group).ToList()))
            .OrderByDescending(folder => folder.Sessions[0].LastActivity ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>The mark of an opencode session as the rail reads it: its owner's mark, with the archive state opencode gives (OC-D4).</summary>
    private LibraryEntry? OpenCodeMarkOf(SessionSummary session) =>
        EffectiveOpenCodeMark(MarkOf(ProviderId.OpenCode, session.Key.SessionId), _openCodeArchived.Contains(session.Key));

    /// <summary>Where an opencode session is listed under the view in use (parity P11), read from its mark with the opencode tag filter first.</summary>
    private ProviderPlace OpenCodePlaceOf(SessionSummary session) =>
        ProviderRailGroups.PlaceOf(_openCodeView, OpenCodeMarkOf(session), ActiveOpenCodeTagFilter);

    /// <summary>The opencode sessions in the Pinned group, newest first (parity P4).</summary>
    private IReadOnlyList<SessionSummary> OpenCodePinned() =>
        _openCodeSessions.Where(session => OpenCodePlaceOf(session) == ProviderPlace.PinnedGroup)
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>The archived opencode sessions of the All view, newest first (D1). Under the other views the list is empty.</summary>
    private IReadOnlyList<SessionSummary> OpenCodeArchived() =>
        _openCodeSessions.Where(session => OpenCodePlaceOf(session) == ProviderPlace.ArchivedGroup)
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>The opencode tag groups under Group by tag (tags P5), with the opencode marks.</summary>
    private IReadOnlyList<TagGroup<SessionSummary>> OpenCodeTagGroups() =>
        ProviderRailGroups.ByTag(
            _openCodeSessions,
            id => EffectiveOpenCodeMark(MarkOf(ProviderId.OpenCode, id), _openCodeArchived.Contains(new SessionKey(ProviderId.OpenCode, id))),
            ActiveOpenCodeTagFilter,
            _openCodeView);

    /// <summary>One opencode tag group (tags P5): "#tag (N)", or "No tag (N)" for the sessions with no tag.</summary>
    private TreeViewItem OpenCodeTagItem(TagGroup<SessionSummary> group, bool expandByDefault)
    {
        var key = group.Tag is null ? "opencode-tag-none" : "opencode-tag:" + group.Tag;
        var header = group.Tag is null ? $"No tag ({group.Sessions.Count})" : $"#{group.Tag} ({group.Sessions.Count})";
        var tooltip = group.Tag is null
            ? "Sessions with no tag."
            : $"Tag #{group.Tag}. A session with several tags is listed under each of them.";
        var item = NewItem(Label(header, bold: true), new RailTag(RailKind.Heading, key), tooltip);
        item.IsExpanded = _expanded.GetValueOrDefault(key,
            expandByDefault || Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_EXPAND") == "1");
        foreach (var session in group.Sessions.Take(MaxHistoryPerFolder)) item.Items.Add(OpenCodeRow(session));
        return item;
    }

    /// <summary>The pinned opencode sessions, in one group at the top of the opencode history (parity P4).</summary>
    private TreeViewItem OpenCodePinnedItem(IReadOnlyList<SessionSummary> pinned)
    {
        const string key = "opencode-pinned";
        var item = NewItem(Label("Pinned", bold: true), new RailTag(RailKind.Heading, key),
            "opencode sessions you pinned. Each one is listed here only.");
        item.IsExpanded = _expanded.GetValueOrDefault(key, true);
        foreach (var session in pinned) item.Items.Add(OpenCodeRow(session));
        return item;
    }

    /// <summary>The archived opencode sessions, in one collapsed group at the end of the history (D1). Each row's menu unarchives it.</summary>
    private TreeViewItem OpenCodeArchivedItem(IReadOnlyList<SessionSummary> archived)
    {
        var item = NewItem(Label($"Archived ({archived.Count})", bold: true), new RailTag(RailKind.Heading, "opencode-archived"),
            "opencode sessions you archived, or that opencode archived. Each one is listed here only. Its menu unarchives it.");
        item.IsExpanded = false;
        foreach (var session in archived) item.Items.Add(OpenCodeRow(session));
        return item;
    }

    private TreeViewItem OpenCodeFolderItem(OpenCodeFolder folder)
    {
        var named = folder.Folder.Length > 0;
        var leaf = named ? LeafOf(folder.Folder) : string.Empty;
        var header = !named ? "No folder named" : leaf.Length > 0 ? leaf : folder.Folder;
        var tooltip = named ? $"Folder: {folder.Folder}" : "The database does not name the working folder.";
        var item = NewItem(Label(header, bold: true), new RailTag(RailKind.Heading, "opencode-folder:" + folder.Folder), tooltip);
        item.IsExpanded = true;
        foreach (var session in folder.Sessions.Take(MaxHistoryPerFolder)) item.Items.Add(OpenCodeRow(session));
        return item;
    }

    /// <summary>
    /// One opencode session (D1 for its name, tags, note, colour and archive). Its action is the resume in a terminal tab: from its menu, or a double click.
    /// The second line is the model when there is one; the third the time and the tags.
    /// </summary>
    private TreeViewItem OpenCodeRow(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var mark = OpenCodeMarkOf(session);
        var title = OpenCodeRowTitle(mark?.Name, session);
        var detail = RelativeTime(session.LastActivity, _behaviour.State.ClampClockBackwards) + TagsSuffix(mark);
        var line = TwoLine(title, session.Model ?? string.Empty, detail);
        // The row's kind is the opencode session kind (OC2). Its selection does nothing (ShowRailItemAsync).
        var item = NewItem(ColourEdge(line, mark?.Colour),
            new RailTag(RailKind.OpenCodeSession, id, Detail: session.Folder),
            NoteTip(mark) + "Double click, or the menu, to resume it in a terminal tab.");
        if (mark?.Archived == true) item.Opacity = 0.55;
        item.ContextMenu = OpenCodeRowMenu(session, title);
        item.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            ResumeOpenCode(session);
        };
        return item;
    }

    /// <summary>
    /// An opencode row's title (D1): the owner's name when there is one, else the title from the database, else "opencode session" and the first eight
    /// characters of the id.
    /// </summary>
    internal static string OpenCodeRowTitle(string? name, SessionSummary session)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        if (!string.IsNullOrWhiteSpace(session.Title)) return session.Title;
        var id = session.Key.SessionId;
        return $"opencode session {id[..Math.Min(8, id.Length)]}";
    }

    /// <summary>
    /// The right-click menu of an opencode row: the same items in the same order as the Copilot row's: Resume…, Resume in a terminal tab, View transcript,
    /// Pin or Unpin, Archive or Unarchive, and Edit… for the name, tags, note and colour.
    /// </summary>
    private ContextMenu OpenCodeRowMenu(SessionSummary session, string title)
    {
        var id = session.Key.SessionId;
        var mark = OpenCodeMarkOf(session) ?? new LibraryEntry();
        var items = new List<(string Header, Action Action)>
        {
            ("Resume…", () => ResumeOpenCodeScreen(session)),
            ("Resume in a terminal tab", () => ResumeOpenCode(session)),
            ("View transcript", () => OpenCodeTranscriptTab(session)),
            (mark.Pinned ? "Unpin" : "Pin", () => SetOpenCodePinned(id, !mark.Pinned)),
        };
        // The archive item is left out for a session opencode archived (OC-D21): the app cannot clear opencode's flag.
        if (OpenCodeArchiveItemText(mark.Archived, archivedInOpenCode: _openCodeArchived.Contains(session.Key)) is { } archiveText)
            items.Add((archiveText, () => SetOpenCodeArchived(id, !mark.Archived)));
        items.Add(("Edit…", () => EditMarkOf(ProviderId.OpenCode, id, title)));
        return ProviderRowMenu(items.ToArray());
    }

    /// <summary>
    /// The archive item of an opencode row: "Unarchive" or "Archive" for the app's own mark, or null when opencode archived the session (OC-D21). The
    /// app can clear only its own mark, so an Unarchive that does nothing is not offered. Pure.
    /// </summary>
    internal static string? OpenCodeArchiveItemText(bool archived, bool archivedInOpenCode) =>
        archivedInOpenCode ? null : archived ? "Unarchive" : "Archive";

    /// <summary>Pins or unpins an opencode session. The change is written under its opencode: key (D1).</summary>
    private bool SetOpenCodePinned(string sessionId, bool pinned) =>
        ChangeMarkOf(ProviderId.OpenCode, sessionId, mark => mark with { Pinned = pinned });

    /// <summary>
    /// Archives or unarchives an opencode session in the app's own marks. The change is written under its opencode: key (D1). It does not change opencode's
    /// archive state, which the database holds and the app never writes (OC-D4).
    /// </summary>
    private bool SetOpenCodeArchived(string sessionId, bool archived) =>
        ChangeMarkOf(ProviderId.OpenCode, sessionId, mark => mark with { Archived = archived });

    /// <summary>
    /// Loads the opencode history off the UI thread. Nothing is written. A failed read keeps the list it had and says so in the status line. A read that is
    /// already running is not started again: the returned task is that read, so an awaiting caller gets its list (S1).
    /// </summary>
    private Task LoadOpenCodeHistoryAsync()
    {
        // While a read runs (the flag), its task is returned and nothing new starts. The task is null only during that read's first synchronous part.
        if (_openCodeLoading) return _openCodeLoadTask ?? Task.CompletedTask;
        var load = ReadOpenCodeHistoryAsync();
        _openCodeLoadTask = load;
        return load;
    }

    /// <summary>The read itself: sets the loading flag, reads the sessions with the read-only source, and records the result.</summary>
    private async Task ReadOpenCodeHistoryAsync()
    {
        _openCodeLoading = true;
        if (IsOpenCodeActive) RefreshRail();
        try
        {
            var location = CurrentOpenCodeLocation();
            var source = new OpenCodeHistorySource(location);
            var sessions = await source.ListAsync(CancellationToken.None);
            _openCodeLocation = location;
            _openCodeSessions = sessions;
            _openCodeStatus = source.Status;
            _openCodeArchived = source.ArchivedKeys;
            _openCodeSkippedRows = source.SkippedRows;
            _openCodeRead = true;
            _openCodeFailed = false;
            // The log holds counts and the status name only: no id, title, folder, path or message.
            AppLog.Write($"opencode history read: {sessions.Count} sessions, {source.SkippedRows} skipped rows, status {source.Status}");
            if (IsOpenCodeActive)
            {
                if (source.Status != OpenCodeHistoryStatus.Ok) SetStatus(source.StatusText);
                else if (OpenCodeRailText.IncompleteNote(source.SkippedRows) is { } note) SetStatus(note);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _openCodeFailed = true;
            AppLog.Write($"opencode history read failed: {ex.GetType().Name}");
            SetStatus(OpenCodeRailText.ReadFailed(ex.GetType().Name));
        }
        finally
        {
            _openCodeLoading = false;
            _openCodeLoadTask = null;
            if (IsOpenCodeActive) RefreshRail();
        }
    }

    /// <summary>
    /// "Resume…" on an opencode row: the opencode resume screen (OC3, in full and from the last turns). Nothing starts until its Start
    /// (<see cref="ShowOpenCodeResume"/>).
    /// </summary>
    private void ResumeOpenCodeScreen(SessionSummary session) => ShowOpenCodeResume(session);

    /// <summary>
    /// Resumes an opencode session in a terminal tab (OC-D8): <c>cmd.exe /d /c opencode -s &lt;id&gt;</c>, started in the session's folder. A session whose tab
    /// is already open is shown and not started again. A resume is refused, with a status line, when the id is not in the app's list of opencode sessions,
    /// the folder no longer exists, or the id has not the opencode shape: there is no silent fallback to another session.
    /// </summary>
    private void ResumeOpenCode(SessionSummary session)
    {
        var id = session.Key.SessionId;
        if (ShowOpenCodeTab(id)) return;

        if (!_openCodeSessions.Any(listed => SameId(listed.Key.SessionId, id)))
        {
            SetStatus(OpenCodeRailText.NotListed);
            return;
        }
        var folder = session.Folder;
        if (folder is null || !Directory.Exists(folder))
        {
            SetStatus(OpenCodeRailText.FolderMissing(folder));
            return;
        }

        string command;
        try
        {
            command = OpenCodeOpener.ResumeCommandLine(id);
        }
        catch (ArgumentException)
        {
            SetStatus(OpenCodeRailText.InvalidId);
            return;
        }
        OpenCodeTab(id, OpenCodeRowTitle(MarkOf(ProviderId.OpenCode, id)?.Name, session), command, folder);
    }

    /// <summary>Shows the terminal tab of an opencode session that is already open in this window. True when there was one to show.</summary>
    private bool ShowOpenCodeTab(string id)
    {
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.Session && t.Provider == ProviderId.OpenCode && SameId(t.Key, id));
        if (open is null) return false;
        SetActive(open);
        if (_terminals.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
        return true;
    }

    /// <summary>
    /// Opens an opencode terminal tab: a terminal that runs <paramref name="command"/> in <paramref name="folder"/>, owned by this window, through the shared
    /// tab open (<see cref="OpenProviderTab"/>) with opencode's exit rules (<c>OpenCodeTerminal</c>, in the exit partial).
    /// </summary>
    private OpenTab OpenCodeTab(string id, string title, string command, string folder)
    {
        var tab = OpenProviderTab(OpenCodeTerminal, id, title, command, folder);
        SetStatus($"opencode tab opened in {folder}.");
        return tab;
    }

    /// <summary>
    /// Closes an opencode tab: the tab goes, and its terminal is closed with it. opencode keeps the conversation in its database, and it can be resumed from
    /// the opencode rows. Nothing is asked, as there is no Claude session behind the tab.
    /// </summary>
    private void CloseOpenCodeTab(OpenTab tab)
    {
        var key = KeyOf(tab);
        // Marked before the terminal is detached: the process end that the detach causes is then expected, and changes nothing.
        tab.ClosedByOwner = true;
        CloseTab(tab);
        DetachTerminal(key);
        SetStatus($"{tab.Title} is closed. opencode keeps the conversation: resume it from the opencode rows.");
    }

    /// <summary>
    /// The command a "+" new opencode session runs in a folder (OC2d): the opencode terminal's new command (OC-D8), as Copilot's "+" uses its own. Pure:
    /// it builds the text and starts nothing. A folder the command builder refuses throws <see cref="ArgumentException"/>.
    /// </summary>
    internal static string OpenCodeNewCommandLine(string folder) => OpenCodeOpener.NewCommandLine(folder);

    /// <summary>
    /// "New opencode session…" (OC2d, the "+" under opencode, as "New Copilot session…" is under Copilot): asks for a folder, then opens opencode there in a
    /// terminal tab. Cancel starts nothing. A folder the command builder refuses is refused with a status line.
    /// </summary>
    private void NewOpenCodeSession()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the opencode session" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        string command;
        try
        {
            command = OpenCodeNewCommandLine(folder);
        }
        catch (ArgumentException ex)
        {
            SetStatus($"Not started: {ex.Message}");
            return;
        }
        OpenCodeTab("new-" + Guid.NewGuid().ToString("N"), $"opencode: {LeafOf(folder)}", command, folder);
    }

    /// <summary>The views the opencode rail has (parity P11): All, History and Archived, as the Copilot rail has them.</summary>
    internal static bool IsOpenCodeView(RailFilter view) => view is RailFilter.All or RailFilter.History or RailFilter.Archived;

    /// <summary>Sets the view the opencode rail shows (parity P11). Claude's views that opencode does not have fall back to All.</summary>
    private void ChooseOpenCodeView(RailFilter view)
    {
        _openCodeView = IsOpenCodeView(view) ? view : RailFilter.All;
        UpdateFilterMark();
        RefreshRail();
    }

    /// <summary>The opencode marks of the sessions in the history: what the opencode tag menu counts (tags P5).</summary>
    private IEnumerable<LibraryEntry> OpenCodeHistoryMarks() =>
        _openCodeSessions.Select(session => OpenCodeMarkOf(session)).OfType<LibraryEntry>();

    /// <summary>Adds the opencode tag entries to the filter dropdown (tags P5): one per tag the opencode sessions hold, and "Clear tag filter" while one is set.</summary>
    private void AddOpenCodeTagItems(ContextMenu menu)
    {
        var tags = TagIndex.Counts(OpenCodeHistoryMarks());
        var active = ActiveOpenCodeTagFilter;
        if (tags.Count == 0 && active is null)
        {
            var reason = _libraryError is null ? "No tags on opencode sessions yet" : "No tags: library.json cannot be read";
            menu.Items.Add(new MenuItem { Header = reason, IsEnabled = false, Padding = new Thickness(10, 4, 16, 4) });
            return;
        }
        foreach (var tag in tags)
        {
            var name = tag.Tag;
            var item = new MenuItem
            {
                Header = $"Filter by tag #{name} ({tag.Count})",
                IsCheckable = true,
                IsChecked = SameTag(name, active),
                Padding = new Thickness(10, 4, 16, 4),
            };
            // A click on the tag in use clears it; a click on another tag makes it the one in use.
            item.Click += (_, _) => ChooseTag(SameTag(name, ActiveOpenCodeTagFilter) ? null : name);
            menu.Items.Add(item);
        }
        if (active is not null)
        {
            var clear = new MenuItem { Header = "Clear tag filter", Padding = new Thickness(10, 4, 16, 4) };
            clear.Click += (_, _) => ChooseTag(null);
            menu.Items.Add(clear);
        }
    }

    /// <summary>An opencode tag that no opencode mark holds any more clears the opencode tag filter, with a status line (tags P5).</summary>
    private void DropMissingOpenCodeTagFilter()
    {
        if (_openCodeTagFilter is null || _libraryError is not null) return;
        if (TagIndex.Counts(MarksOf(ProviderId.OpenCode).Values).Any(tag => SameTag(tag.Tag, _openCodeTagFilter))) return;
        _openCodeTagFilter = null;
        UpdateFilterMark();
        SetStatus("The tag filter is cleared: no opencode session has that tag any more.");
    }
}

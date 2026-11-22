using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Copilot in the window (stage G2, copied from the Codex partial, G-D10): the Copilot rows under the provider dropdown, the Copilot terminal tabs, the
/// row menu, and the full resume (G-D12). The Copilot rows are history only: a row resumes in a terminal tab, and its menu opens a read-only transcript.
/// Claude's and Codex's tabs and records are not changed by any of this. Copilot has no supervisor (G-D7), so its rows are never live.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The Copilot sessions as the last read gave them.</summary>
    private IReadOnlyList<SessionSummary> _copilotSessions = [];

    /// <summary>The tag the Copilot rail shows rows for, or null (tags P5). Kept apart from Claude's and Codex's tag filters.</summary>
    private string? _copilotTagFilter;

    /// <summary>The view the Copilot rail shows: All, History or Archived (parity P11). One at a time, held for the window run.</summary>
    private RailFilter _copilotView = RailFilter.All;

    /// <summary>True once the Copilot history has been read in this window.</summary>
    private bool _copilotRead;

    /// <summary>True while the Copilot history is being read, so a second read does not start.</summary>
    private bool _copilotLoading;

    /// <summary>
    /// The Copilot read that is running, or null when none is. A second caller is given this task, so it waits for the read and does not take a stale list
    /// (Continue, S1). Set when a read starts, cleared when it ends.
    /// </summary>
    private Task? _copilotLoadTask;

    /// <summary>True when the last read failed. The rail then says so, and the status line says why.</summary>
    private bool _copilotFailed;

    /// <summary>Folders with no events file in the last read: counted, not listed (G-D3). Shown in the empty state.</summary>
    private int _copilotEmptySessions;

    /// <summary>Folders the last read could not use (a damaged session record or events file): counted, not listed.</summary>
    private int _copilotDamagedFolders;

    /// <summary>Lines of the session files the last read skipped (not JSON, or over 16 MB). Shown in the empty state.</summary>
    private int _copilotSkippedLines;

    /// <summary>The window's provider is Copilot.</summary>
    private bool IsCopilotActive => _activeProvider.Id == ProviderId.Copilot;

    /// <summary>The tag the Copilot rail filters by, or null. Nothing is filtered while library.json cannot be read (D-L3).</summary>
    private string? ActiveCopilotTagFilter => _libraryError is null ? _copilotTagFilter : null;

    /// <summary>The views the Copilot rail has (parity P11): All, History and Archived, as the Codex rail has them.</summary>
    private static bool IsCopilotView(RailFilter view) => view is RailFilter.All or RailFilter.History or RailFilter.Archived;

    /// <summary>
    /// The rail under Copilot: a header with its reload mark, "New Copilot session…", the note that live sessions are not shown, the empty state or the
    /// failure, then the pinned sessions (parity P4), the folders or tag groups, and the archived group. The view in use decides which of these are listed
    /// (<see cref="ProviderRailGroups.PlaceOf"/>). Replaces the Claude rows; the Claude tabs are kept in step by <see cref="SyncSessionTabs"/>.
    /// </summary>
    private void RefreshCopilotRail(IReadOnlyDictionary<string, SessionRecord> stored)
    {
        _railSignature = null;
        _rebuilding = true;
        try
        {
            Rail.Items.Clear();
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(Label("Copilot history", bold: true));
            var reload = Label(_copilotLoading ? "⏳" : "🔄", muted: true, emoji: true);
            reload.Margin = new Thickness(8, 0, 0, 0);
            reload.Cursor = Cursors.Hand;
            reload.ToolTip = "Reload the Copilot history from its session files (no model)";
            reload.MouseLeftButtonUp += async (_, _) => await LoadCopilotHistoryAsync();
            header.Children.Add(reload);
            var section = NewItem(header, new RailTag(RailKind.HistorySection, "copilot"));
            section.IsExpanded = true;
            Rail.Items.Add(section);

            section.Items.Add(NewItem(Label("New Copilot session…"), new RailTag(RailKind.CopilotNew, "copilot:new"),
                "Choose a folder. Copilot opens there in a terminal tab of this window."));
            section.Items.Add(NewItem(Label("Copilot live sessions are not shown", muted: true), new RailTag(RailKind.Heading, "copilot:live"),
                "This window shows Copilot history and the terminal tabs it opens. Live Copilot sessions started elsewhere are not shown, and nothing is supervised."));

            if (_copilotFailed)
            {
                section.Items.Add(NewItem(Label("Copilot history could not be read", muted: true), new RailTag(RailKind.Heading, "copilot:failed"),
                    "The status line says why. Reload to try again."));
            }
            else if (!_copilotRead)
            {
                section.Items.Add(NewItem(Label(_copilotLoading ? "reading the Copilot history…" : "not read yet", muted: true),
                    new RailTag(RailKind.Heading, "copilot:none")));
            }
            else if (_copilotSessions.Count == 0)
            {
                // The empty state says why there is nothing, where the app looked, and how many folders were left out (G2.2).
                var root = (_copilot ??= new CopilotProvider()).Paths.HistoryRoot;
                section.Items.Add(NewItem(Label(CopilotRailText.EmptyHeading, muted: true), new RailTag(RailKind.Heading, "copilot:empty"),
                    CopilotRailText.EmptyReason(root, _copilotEmptySessions, _copilotDamagedFolders, _copilotSkippedLines)));
            }
            else
            {
                var pinned = CopilotPinned();
                var archived = CopilotArchived();
                // The middle of the history is the tag groups under Group by tag, else the folders. The same tag filter applies to both (tags P5).
                IReadOnlyList<TagGroup<SessionSummary>> tagGroups = _groupByTag ? CopilotTagGroups() : [];
                IReadOnlyList<CopilotFolder> folders = _groupByTag ? [] : CopilotFolders();
                var listed = _groupByTag ? tagGroups.Count : folders.Count;
                // A tag filter or the view that leaves no row says so, so an empty rail is not taken for a fault (tags P5, parity P11).
                if (pinned.Count == 0 && listed == 0 && archived.Count == 0)
                {
                    if (ActiveCopilotTagFilter is { } tag)
                        section.Items.Add(NewItem(Label($"No Copilot session has the tag #{tag}", muted: true), new RailTag(RailKind.Heading, "copilot:tag-empty"),
                            "The tag filter shows the Copilot sessions that carry the tag. Clear it from the filter menu."));
                    else if (_copilotView == RailFilter.Archived)
                        section.Items.Add(NewItem(Label("No Copilot session is archived", muted: true), new RailTag(RailKind.Heading, "copilot:archived-empty"),
                            "Archive a Copilot session from its menu, and it is listed here."));
                }
                if (pinned.Count > 0) section.Items.Add(CopilotPinnedItem(pinned));
                if (_groupByTag)
                {
                    for (int i = 0; i < tagGroups.Count; i++) section.Items.Add(CopilotTagItem(tagGroups[i], expandByDefault: i == 0));
                }
                else
                {
                    foreach (var folder in folders) section.Items.Add(CopilotFolderItem(folder));
                }
                if (archived.Count > 0) section.Items.Add(CopilotArchivedItem(archived));
            }
        }
        finally
        {
            _rebuilding = false;
        }
        SyncSessionTabs(stored);
    }

    /// <summary>A folder of Copilot sessions: its working folder, or "" for the sessions whose files name none.</summary>
    private sealed record CopilotFolder(string Folder, IReadOnlyList<SessionSummary> Sessions);

    /// <summary>
    /// The Copilot folders under the view in use, newest first. The sessions in each are newest first too. Under All and History a folder holds the sessions
    /// that are neither archived nor pinned (parity P4); under Archived, the archived sessions (parity P11). The rule is <see cref="ProviderRailGroups.PlaceOf"/>.
    /// </summary>
    private IReadOnlyList<CopilotFolder> CopilotFolders() =>
        _copilotSessions
            .Where(session => CopilotPlaceOf(session) == ProviderPlace.FolderGroups)
            .GroupBy(session => session.Folder ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CopilotFolder(group.Key, ProviderRailGroups.Sorted(group).ToList()))
            .OrderByDescending(folder => folder.Sessions[0].LastActivity ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>Where a Copilot session is listed under the view in use (parity P11): read from its Copilot mark, with the Copilot tag filter first.</summary>
    private ProviderPlace CopilotPlaceOf(SessionSummary session) =>
        ProviderRailGroups.PlaceOf(_copilotView, MarkOf(ProviderId.Copilot, session.Key.SessionId), ActiveCopilotTagFilter);

    /// <summary>The Copilot sessions in the Pinned group, newest first (parity P4).</summary>
    private IReadOnlyList<SessionSummary> CopilotPinned() =>
        _copilotSessions.Where(session => CopilotPlaceOf(session) == ProviderPlace.PinnedGroup)
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>The archived Copilot sessions of the All view, newest first (D1). Under the other views the list is empty.</summary>
    private IReadOnlyList<SessionSummary> CopilotArchived() =>
        _copilotSessions.Where(session => CopilotPlaceOf(session) == ProviderPlace.ArchivedGroup)
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>The Copilot tag groups under Group by tag (tags P5), with the Copilot marks.</summary>
    private IReadOnlyList<TagGroup<SessionSummary>> CopilotTagGroups() =>
        ProviderRailGroups.ByTag(_copilotSessions, id => MarkOf(ProviderId.Copilot, id), ActiveCopilotTagFilter, _copilotView);

    /// <summary>One Copilot tag group (tags P5): "#tag (N)", or "No tag (N)" for the sessions with no tag.</summary>
    private TreeViewItem CopilotTagItem(TagGroup<SessionSummary> group, bool expandByDefault)
    {
        var key = group.Tag is null ? "copilot-tag-none" : "copilot-tag:" + group.Tag;
        var header = group.Tag is null ? $"No tag ({group.Sessions.Count})" : $"#{group.Tag} ({group.Sessions.Count})";
        var tooltip = group.Tag is null
            ? "Sessions with no tag."
            : $"Tag #{group.Tag}. A session with several tags is listed under each of them.";
        var item = NewItem(Label(header, bold: true), new RailTag(RailKind.Heading, key), tooltip);
        item.IsExpanded = _expanded.GetValueOrDefault(key,
            expandByDefault || Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_EXPAND") == "1");
        foreach (var session in group.Sessions.Take(MaxHistoryPerFolder)) item.Items.Add(CopilotRow(session));
        return item;
    }

    /// <summary>The pinned Copilot sessions, in one group at the top of the Copilot history (parity P4).</summary>
    private TreeViewItem CopilotPinnedItem(IReadOnlyList<SessionSummary> pinned)
    {
        const string key = "copilot-pinned";
        var item = NewItem(Label("Pinned", bold: true), new RailTag(RailKind.Heading, key),
            "Copilot sessions you pinned. Each one is listed here only.");
        item.IsExpanded = _expanded.GetValueOrDefault(key, true);
        foreach (var session in pinned) item.Items.Add(CopilotRow(session));
        return item;
    }

    /// <summary>The archived Copilot sessions, in one collapsed group at the end of the history (D1). Each row's menu unarchives it.</summary>
    private TreeViewItem CopilotArchivedItem(IReadOnlyList<SessionSummary> archived)
    {
        var item = NewItem(Label($"Archived ({archived.Count})", bold: true), new RailTag(RailKind.Heading, "copilot-archived"),
            "Copilot sessions you archived. Each one is listed here only. Its menu unarchives it.");
        item.IsExpanded = false;
        foreach (var session in archived) item.Items.Add(CopilotRow(session));
        return item;
    }

    private TreeViewItem CopilotFolderItem(CopilotFolder folder)
    {
        var named = folder.Folder.Length > 0;
        var leaf = named ? LeafOf(folder.Folder) : string.Empty;
        var header = !named ? "No folder named" : leaf.Length > 0 ? leaf : folder.Folder;
        var tooltip = named ? $"Folder: {folder.Folder}" : "The session files do not name the working folder.";
        var item = NewItem(Label(header, bold: true), new RailTag(RailKind.Heading, "copilot-folder:" + folder.Folder), tooltip);
        item.IsExpanded = true;
        foreach (var session in folder.Sessions.Take(MaxHistoryPerFolder)) item.Items.Add(CopilotRow(session));
        return item;
    }

    /// <summary>
    /// One Copilot session (D1 for its name, tags, note, colour and archive). Its action is the resume in a terminal tab: from its menu, or a double click.
    /// The second line has the model; the third the time and the tags.
    /// </summary>
    private TreeViewItem CopilotRow(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var mark = MarkOf(ProviderId.Copilot, id);
        var title = CopilotRowTitle(mark?.Name, session);
        // The third line: the time, the tags, then the plan figure "3/7" when the session has a plan file (parity P3).
        var detail = RelativeTime(session.LastActivity, _behaviour.State.ClampClockBackwards) + TagsSuffix(mark);
        if (PlanFigure(ProviderId.Copilot, id) is { } plan) detail = detail.Length == 0 ? plan : detail + " · " + plan;
        var line = TwoLine(title, session.Model ?? string.Empty, detail);
        var item = NewItem(ColourEdge(line, mark?.Colour),
            new RailTag(RailKind.CopilotSession, id, Detail: session.Folder),
            NoteTip(mark) + PlanTip(mark) + "Double click, or the menu, to resume it in a terminal tab.");
        if (mark?.Archived == true) item.Opacity = 0.55;
        item.ContextMenu = CopilotRowMenu(session, title);
        item.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            ResumeCopilot(session);
        };
        return item;
    }

    /// <summary>
    /// A Copilot row's title (D1): the owner's name when there is one, else the title from the history, else "Copilot session" and the first eight
    /// characters of the id.
    /// </summary>
    internal static string CopilotRowTitle(string? name, SessionSummary session)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        if (!string.IsNullOrWhiteSpace(session.Title)) return session.Title;
        var id = session.Key.SessionId;
        return $"Copilot session {id[..Math.Min(8, id.Length)]}";
    }

    /// <summary>
    /// The right-click menu of a Copilot row: the same items in the same order as the Codex row's (G2.5): Resume…, Resume in a terminal tab, View
    /// transcript, Pin or Unpin, Archive or Unarchive, and Edit… for the name, tags, note and colour.
    /// </summary>
    private ContextMenu CopilotRowMenu(SessionSummary session, string title)
    {
        var id = session.Key.SessionId;
        var mark = MarkOf(ProviderId.Copilot, id) ?? new LibraryEntry();
        return ProviderRowMenu(
            ("Resume…", () => ResumeCopilotScreen(session)),
            ("Resume in a terminal tab", () => ResumeCopilot(session)),
            ("View transcript", () => OpenCopilotTranscript(session)),
            (mark.Pinned ? "Unpin" : "Pin", () => SetCopilotPinned(id, !mark.Pinned)),
            (mark.Archived ? "Unarchive" : "Archive", () => SetCopilotArchived(id, !mark.Archived)),
            ("Edit…", () => EditMarkOf(ProviderId.Copilot, id, title)));
    }

    /// <summary>Pins or unpins a Copilot session. The change is written under its copilot: key (D1).</summary>
    private bool SetCopilotPinned(string sessionId, bool pinned) =>
        ChangeMarkOf(ProviderId.Copilot, sessionId, mark => mark with { Pinned = pinned });

    /// <summary>Archives or unarchives a Copilot session. The change is written under its copilot: key (D1).</summary>
    private bool SetCopilotArchived(string sessionId, bool archived) =>
        ChangeMarkOf(ProviderId.Copilot, sessionId, mark => mark with { Archived = archived });

    /// <summary>
    /// Loads the Copilot history off the UI thread. Nothing is written. A failed read keeps the list it had and says so in the status line. A read that
    /// skipped something says so too (risk R-C1). A read that is already running is not started again: the returned task is that read, so an awaiting
    /// caller gets its list (S1).
    /// </summary>
    private Task LoadCopilotHistoryAsync()
    {
        // While a read runs (the flag), its task is returned and nothing new starts. The task is null only during that read's first synchronous part.
        if (_copilotLoading) return _copilotLoadTask ?? Task.CompletedTask;
        var load = ReadCopilotHistoryAsync();
        _copilotLoadTask = load;
        return load;
    }

    /// <summary>The read itself: sets the loading flag, reads the sessions, and records the result (see <see cref="LoadCopilotHistoryAsync"/>).</summary>
    private async Task ReadCopilotHistoryAsync()
    {
        _copilotLoading = true;
        if (IsCopilotActive) RefreshRail();
        try
        {
            var provider = _copilot ??= new CopilotProvider();
            _copilotSessions = await provider.ListAsync(CancellationToken.None);
            var history = provider.History;
            _copilotEmptySessions = history.EmptySessions;
            _copilotDamagedFolders = history.SkippedFiles;
            _copilotSkippedLines = history.SkippedLines;
            _copilotRead = true;
            _copilotFailed = false;
            // The log holds counts only: no id, title, folder or line text.
            AppLog.Write($"copilot history read: {_copilotSessions.Count} sessions, {history.EmptySessions} empty, {history.SkippedFiles} damaged folders, {history.SkippedLines} skipped lines");
            if (IsCopilotActive && CopilotRailText.IncompleteNote(history.SkippedFiles, history.SkippedLines) is { } note) SetStatus(note);
            OpenCopilotResumeForShot();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _copilotFailed = true;
            AppLog.Write($"copilot history read failed: {ex.GetType().Name}");
            SetStatus($"Copilot history not read: {ex.GetType().Name}");
        }
        finally
        {
            _copilotLoading = false;
            _copilotLoadTask = null;
            if (IsCopilotActive) RefreshRail();
        }
    }

    /// <summary>
    /// Resumes a Copilot session in a terminal tab (G-D12): <c>copilot --resume=&lt;id&gt;</c>, started in the session's folder. A session whose tab is already
    /// open is shown and not started again. A resume is refused, with a status line, when the id is not in the app's list of Copilot sessions or the
    /// folder no longer exists: there is no silent fallback to another session.
    /// </summary>
    private void ResumeCopilot(SessionSummary session)
    {
        var id = session.Key.SessionId;
        if (ShowOpenCopilotTab(id)) return;

        if (!_copilotSessions.Any(listed => SameId(listed.Key.SessionId, id)))
        {
            SetStatus(CopilotRailText.NotListed);
            return;
        }
        var folder = session.Folder;
        if (folder is null || !Directory.Exists(folder))
        {
            SetStatus(CopilotRailText.FolderMissing(folder));
            return;
        }

        var provider = _copilot ??= new CopilotProvider();
        string command;
        try
        {
            command = provider.ResumeCommandLine(id);
        }
        catch (ArgumentException)
        {
            SetStatus(CopilotRailText.InvalidId);
            return;
        }
        OpenCopilotTab(id, CopilotRowTitle(MarkOf(ProviderId.Copilot, id)?.Name, session), command, folder);
    }

    /// <summary>Shows the terminal tab of a Copilot session that is already open in this window. True when there was one to show.</summary>
    private bool ShowOpenCopilotTab(string id)
    {
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.Session && t.Provider == ProviderId.Copilot && SameId(t.Key, id));
        if (open is null) return false;
        SetActive(open);
        if (_terminals.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
        return true;
    }

    /// <summary>
    /// "New Copilot session…": asks for a folder, then opens <c>copilot -C</c> there in a terminal tab (G-D11). Cancel starts nothing. A folder the
    /// command builder refuses (a double quote, a percent sign or an exclamation mark) is refused with a status line.
    /// </summary>
    private void NewCopilotSession()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the Copilot session" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        string command;
        try
        {
            command = (_copilot ??= new CopilotProvider()).NewCommandLine(folder);
        }
        catch (ArgumentException ex)
        {
            SetStatus($"Not started: {ex.Message}");
            return;
        }
        OpenCopilotTab("new-" + Guid.NewGuid().ToString("N"), $"Copilot: {LeafOf(folder)}", command, folder);
    }

    /// <summary>
    /// Opens a Copilot terminal tab: a terminal that runs <paramref name="command"/> in <paramref name="folder"/>, owned by this window. Its key is the id
    /// it was opened with: the session's id for a resume, or a new unique id for a new session (no thread link, as Codex's, G-D6).
    /// </summary>
    private OpenTab OpenCopilotTab(string id, string title, string command, string folder)
    {
        var tab = OpenProviderTab(CopilotTerminal, id, title, command, folder);
        SetStatus($"Copilot tab opened in {folder}.");
        return tab;
    }

    /// <summary>
    /// Closes a Copilot tab: the tab goes, and its terminal is closed with it. Copilot keeps the conversation in its session files, and it can be resumed
    /// from the Copilot rows. Nothing is asked, as there is no Claude session behind the tab.
    /// </summary>
    private void CloseCopilotTab(OpenTab tab)
    {
        var key = KeyOf(tab);
        // Marked before the terminal is detached: the process end that the detach causes is then expected, and changes nothing.
        tab.ClosedByOwner = true;
        CloseTab(tab);
        DetachTerminal(key);
        SetStatus($"{tab.Title} is closed. Copilot keeps the conversation: resume it from the Copilot rows.");
    }

    /// <summary>
    /// Removes the Copilot scratch folders that earlier runs left beside the state file (G-D15, startup age zero). Called once this instance holds the
    /// single-instance guard for the state file, so no other instance can be using one of them. Logs the count only. A failure is logged by type, and the
    /// window starts anyway.
    /// </summary>
    private static void SweepCopilotScratch(string statePath)
    {
        try
        {
            var removed = CopilotScratchFolder.RemoveOld(CopilotScratchFolder.ParentBeside(statePath), CopilotScratchFolder.StartupAge, DateTimeOffset.UtcNow);
            AppLog.Write($"copilot scratch folders removed at startup: {removed}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"copilot scratch folders not removed at startup: {ex.GetType().Name}");
        }
    }

    /// <summary>Adds the Copilot tag entries to the filter dropdown (tags P5): one per tag the Copilot sessions hold, and "Clear tag filter" while one is set.</summary>
    private void AddCopilotTagItems(ContextMenu menu)
    {
        var tags = TagIndex.Counts(CopilotHistoryMarks());
        var active = ActiveCopilotTagFilter;
        if (tags.Count == 0 && active is null)
        {
            var reason = _libraryError is null ? "No tags on Copilot sessions yet" : "No tags: library.json cannot be read";
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
            item.Click += (_, _) => ChooseTag(SameTag(name, ActiveCopilotTagFilter) ? null : name);
            menu.Items.Add(item);
        }
        if (active is not null)
        {
            var clear = new MenuItem { Header = "Clear tag filter", Padding = new Thickness(10, 4, 16, 4) };
            clear.Click += (_, _) => ChooseTag(null);
            menu.Items.Add(clear);
        }
    }

    /// <summary>The Copilot marks of the sessions in the history: what the Copilot tag menu counts (tags P5).</summary>
    private IEnumerable<LibraryEntry> CopilotHistoryMarks() =>
        _copilotSessions.Select(session => MarkOf(ProviderId.Copilot, session.Key.SessionId)).OfType<LibraryEntry>();

    /// <summary>A Copilot tag that no Copilot mark holds any more clears the Copilot tag filter, with a status line (tags P5).</summary>
    private void DropMissingCopilotTagFilter()
    {
        if (_copilotTagFilter is null || _libraryError is not null) return;
        if (TagIndex.Counts(MarksOf(ProviderId.Copilot).Values).Any(tag => SameTag(tag.Tag, _copilotTagFilter))) return;
        _copilotTagFilter = null;
        UpdateFilterMark();
        SetStatus("The tag filter is cleared: no Copilot session has that tag any more.");
    }

    /// <summary>Sets the view the Copilot rail shows (parity P11). Claude's views that Copilot does not have fall back to All.</summary>
    private void ChooseCopilotView(RailFilter view)
    {
        _copilotView = IsCopilotView(view) ? view : RailFilter.All;
        UpdateFilterMark();
        RefreshRail();
    }
}

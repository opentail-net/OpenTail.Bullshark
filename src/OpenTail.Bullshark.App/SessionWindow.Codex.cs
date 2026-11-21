using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Codex in the window (stage C3, C4): the provider dropdown at the top of the rail, the Codex rows under it, and the Codex terminal tabs.
/// The Codex rows are history only (C-D4): a row resumes in a terminal tab, and no transcript is shown. Claude's tabs and records are not changed
/// by any of this: they stay as they were, whatever the dropdown shows. Codex has no supervisor (B5), so its rows are never live.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The Codex sessions as the last read gave them.</summary>
    private IReadOnlyList<SessionSummary> _codexSessions = [];

    /// <summary>
    /// The tag the Codex rail shows rows for, or null (tags P5). Kept apart from Claude's tag filter, which DropMissingTagFilter clears when no
    /// Claude mark holds the tag. One at a time, held for the window run, as Claude's is.
    /// </summary>
    private string? _codexTagFilter;

    /// <summary>True once the Codex history has been read in this window.</summary>
    private bool _codexRead;

    /// <summary>True while the Codex history is being read, so a second read does not start.</summary>
    private bool _codexLoading;

    /// <summary>True when the last read failed. The rail then says so, and the status line says why.</summary>
    private bool _codexFailed;

    /// <summary>True once the dropdown has its items. Its selection events before that are not a choice of the owner.</summary>
    private bool _providerReady;

    /// <summary>The search hint as the XAML sets it, kept so Claude's text comes back when the dropdown returns to Claude.</summary>
    private string _claudeSearchHint = string.Empty;

    /// <summary>The window's provider is Codex.</summary>
    private bool IsCodexActive => _activeProvider.Id == ProviderId.Codex;

    /// <summary>
    /// The Claude-only features (C4): the supervised rows, Needs you, limits, trust, respawn, retry and the search box. They show when the window's
    /// provider supervises. Claude does (ISupervisedLauncher); Codex does not.
    /// </summary>
    private bool ClaudeSupervision => ProviderFeatures.Has<ISupervisedLauncher>(_activeProvider);

    /// <summary>The dropdown's items: Claude, and Codex where this build offers it (C-D2). Then the chrome follows the saved provider.</summary>
    private void InitProvider()
    {
        ProviderBox.Items.Add(new ComboBoxItem { Content = "Claude", Tag = ProviderId.Claude });
        if (CodexProvider.Available) ProviderBox.Items.Add(new ComboBoxItem { Content = "Codex", Tag = ProviderId.Codex });
        ProviderBox.SelectedItem = ProviderBox.Items.Cast<ComboBoxItem>().First(item => item.Tag is ProviderId id && id == _activeProvider.Id);
        _claudeSearchHint = SearchHint.Text;
        _providerReady = true;
        ApplyProviderChrome();
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_providerReady || ProviderBox.SelectedItem is not ComboBoxItem { Tag: ProviderId id }) return;
        ChooseProvider(id);
    }

    /// <summary>
    /// The dropdown's choice: the rail lists that provider's rows from now on, and the choice is saved in ui.json (C4). Open tabs keep their own
    /// provider and stay as they are. Nothing is started or stopped here.
    /// </summary>
    private void ChooseProvider(ProviderId id)
    {
        if (id == _activeProvider.Id) return;
        IProvider next = id == ProviderId.Codex ? (_codex ??= new CodexProvider()) : _claude;
        _activeProvider = next;
        ProviderStore.Save(_options.StatePath, id);
        ApplyProviderChrome();
        _railSignature = null;
        if (IsCodexActive && !_codexRead) _ = LoadCodexHistoryAsync();
        RefreshRail();
    }

    /// <summary>
    /// The chrome that is Claude's: the search box and the status bar's Claude items (C4). The filter box stays under Codex, where its menu offers
    /// the Codex tags (tags P5). Under Codex the search box is off and its hint says why. The Claude items are set again by the next pass when Claude shows.
    /// </summary>
    private void ApplyProviderChrome()
    {
        var claude = ClaudeSupervision;
        UpdateFilterMark();
        SearchBox.IsEnabled = claude;
        SearchHint.Text = claude ? _claudeSearchHint : "Codex search comes later";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (claude) return;
        TrustButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        RespawnButton.Visibility = Visibility.Collapsed;
        NeedsYouText.Visibility = Visibility.Collapsed;
        LimitStatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The rail under Codex: a header with its reload mark, "New Codex session…", the empty state of the live part (Codex has none), then the
    /// pinned sessions (parity P4), then the history grouped by folder, newest folder first, or by tag under Group by tag (tags P5). Replaces the
    /// Claude rows; the Claude tabs are kept in step by <see cref="SyncSessionTabs"/>.
    /// </summary>
    private void RefreshCodexRail(IReadOnlyDictionary<string, SessionRecord> stored)
    {
        _railSignature = null;
        _rebuilding = true;
        try
        {
            Rail.Items.Clear();
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(Label("Codex history", bold: true));
            var reload = Label(_codexLoading ? "⏳" : "🔄", muted: true, emoji: true);
            reload.Margin = new Thickness(8, 0, 0, 0);
            reload.Cursor = Cursors.Hand;
            reload.ToolTip = "Reload the Codex history from its rollouts (no model)";
            reload.MouseLeftButtonUp += async (_, _) => await LoadCodexHistoryAsync();
            header.Children.Add(reload);
            var section = NewItem(header, new RailTag(RailKind.HistorySection, "codex"));
            section.IsExpanded = true;
            Rail.Items.Add(section);

            section.Items.Add(NewItem(Label("New Codex session…"), new RailTag(RailKind.CodexNew, "codex:new"),
                "Choose a folder. Codex opens there in a terminal tab of this window."));
            section.Items.Add(NewItem(Label("Codex live sessions are not shown", muted: true), new RailTag(RailKind.Heading, "codex:live"),
                "This window shows Codex history and the terminal tabs it opens. Live Codex sessions started elsewhere are not shown, and nothing is supervised."));

            if (_codexFailed)
            {
                section.Items.Add(NewItem(Label("Codex history could not be read", muted: true), new RailTag(RailKind.Heading, "codex:failed"),
                    "The status line says why. Reload to try again."));
            }
            else if (!_codexRead)
            {
                section.Items.Add(NewItem(Label(_codexLoading ? "reading the Codex history…" : "not read yet", muted: true),
                    new RailTag(RailKind.Heading, "codex:none")));
            }
            else if (_codexSessions.Count == 0)
            {
                section.Items.Add(NewItem(Label("No Codex sessions found", muted: true), new RailTag(RailKind.Heading, "codex:empty"),
                    "No rollouts were found under the Codex home folder."));
            }
            else
            {
                var pinned = CodexPinned();
                var archived = CodexArchived();
                // The middle of the history is the tag groups under Group by tag, else the folders. The same tag filter applies to both (tags P5).
                IReadOnlyList<TagGroup<SessionSummary>> tagGroups = _groupByTag ? CodexTagGroups() : [];
                IReadOnlyList<CodexFolder> folders = _groupByTag ? [] : CodexFolders();
                var listed = _groupByTag ? tagGroups.Count : folders.Count;
                // A tag filter that leaves no row says which tag it is, so an empty rail is not taken for a fault (tags P5).
                if (ActiveCodexTagFilter is { } tag && pinned.Count == 0 && listed == 0 && archived.Count == 0)
                    section.Items.Add(NewItem(Label($"No Codex session has the tag #{tag}", muted: true), new RailTag(RailKind.Heading, "codex:tag-empty"),
                        "The tag filter shows the Codex sessions that carry the tag. Clear it from the filter menu."));
                if (pinned.Count > 0) section.Items.Add(CodexPinnedItem(pinned));
                if (_groupByTag)
                {
                    for (int i = 0; i < tagGroups.Count; i++) section.Items.Add(CodexTagItem(tagGroups[i], expandByDefault: i == 0));
                }
                else
                {
                    foreach (var folder in folders) section.Items.Add(CodexFolderItem(folder));
                }
                if (archived.Count > 0) section.Items.Add(CodexArchivedItem(archived));
            }
        }
        finally
        {
            _rebuilding = false;
        }
        SyncSessionTabs(stored);
    }

    /// <summary>A folder of Codex sessions: its working folder, or "" for the sessions whose rollouts name none.</summary>
    private sealed record CodexFolder(string Folder, IReadOnlyList<SessionSummary> Sessions);

    /// <summary>
    /// The Codex folders, newest first. The sessions in each are newest first too. Archived sessions are left out: they are in the Archived group.
    /// A pinned session is left out too: it is listed in the Pinned group only (parity P4). With a Codex tag filter, a session without the tag is
    /// left out, so a folder with none left is not listed (tags P5). The rule is <see cref="CodexRailGroups.InHistoryGroups"/>, shared with the tag groups.
    /// </summary>
    private IReadOnlyList<CodexFolder> CodexFolders() =>
        _codexSessions
            .Where(session => CodexRailGroups.InHistoryGroups(MarkOf(ProviderId.Codex, session.Key.SessionId), ActiveCodexTagFilter))
            .GroupBy(session => session.Folder ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CodexFolder(group.Key, group.OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList()))
            .OrderByDescending(folder => folder.Sessions[0].LastActivity ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>True when the owner archived a Codex session (its codex: entry, D1).</summary>
    private bool IsCodexArchived(SessionSummary session) => MarkOf(ProviderId.Codex, session.Key.SessionId)?.Archived == true;

    /// <summary>
    /// True when a Codex session is listed in the Pinned group rather than its folder: pinned and not archived (parity P4). The pin is the owner's
    /// codex: entry (D1), never Claude's. The Claude filter does not apply: the Codex rail has no filter of its own (its box is hidden), so the
    /// group shows whichever Claude filter is set.
    /// </summary>
    private bool InCodexPinnedGroup(SessionSummary session) =>
        MarkOf(ProviderId.Codex, session.Key.SessionId) is { Pinned: true, Archived: false };

    /// <summary>The Codex sessions in the Pinned group, newest first (parity P4), with the Codex tag filter applied (tags P5).</summary>
    private IReadOnlyList<SessionSummary> CodexPinned() =>
        _codexSessions.Where(session => InCodexPinnedGroup(session) && CodexTagShows(session))
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>
    /// The Codex tag groups under Group by tag (tags P5): the sessions that are neither archived nor pinned, with the Codex tag filter applied first.
    /// The groups are those of <see cref="CodexRailGroups.ByTag"/>, with the Codex marks.
    /// </summary>
    private IReadOnlyList<TagGroup<SessionSummary>> CodexTagGroups() =>
        CodexRailGroups.ByTag(_codexSessions, id => MarkOf(ProviderId.Codex, id), ActiveCodexTagFilter);

    /// <summary>
    /// One Codex tag group (tags P5): "#tag (N)", or "No tag (N)" for the sessions with no tag, as a Claude tag group is named. A session with several tags
    /// is in each of its groups. The first group starts expanded; a group the owner opens or closes keeps its state, as a Claude tag group's does.
    /// </summary>
    private TreeViewItem CodexTagItem(TagGroup<SessionSummary> group, bool expandByDefault)
    {
        var key = group.Tag is null ? "codex-tag-none" : "codex-tag:" + group.Tag;
        var header = group.Tag is null ? $"No tag ({group.Sessions.Count})" : $"#{group.Tag} ({group.Sessions.Count})";
        var tooltip = group.Tag is null
            ? "Sessions with no tag."
            : $"Tag #{group.Tag}. A session with several tags is listed under each of them.";
        var item = NewItem(Label(header, bold: true), new RailTag(RailKind.Heading, key), tooltip);
        item.IsExpanded = _expanded.GetValueOrDefault(key,
            expandByDefault || Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_EXPAND") == "1");
        foreach (var session in group.Sessions.Take(MaxHistoryPerFolder)) item.Items.Add(CodexRow(session));
        return item;
    }

    /// <summary>
    /// The pinned Codex sessions, in one group at the top of the Codex history, before the folders (parity P4, as the Claude Pinned group). Each row
    /// is a Codex row, so its menu unpins it. The group's expansion is kept the way the Claude group's is.
    /// </summary>
    private TreeViewItem CodexPinnedItem(IReadOnlyList<SessionSummary> pinned)
    {
        const string key = "codex-pinned";
        var item = NewItem(Label("Pinned", bold: true), new RailTag(RailKind.Heading, key),
            "Codex sessions you pinned. Each one is listed here only.");
        item.IsExpanded = _expanded.GetValueOrDefault(key, true);
        foreach (var session in pinned) item.Items.Add(CodexRow(session));
        return item;
    }

    /// <summary>The archived Codex sessions, newest first, or an empty list, with the Codex tag filter applied (tags P5).</summary>
    private IReadOnlyList<SessionSummary> CodexArchived() =>
        _codexSessions.Where(session => IsCodexArchived(session) && CodexTagShows(session))
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>The tag the Codex rail filters by, or null. Nothing is filtered while library.json cannot be read (D-L3).</summary>
    private string? ActiveCodexTagFilter => _libraryError is null ? _codexTagFilter : null;

    /// <summary>
    /// True when a Codex session carries the Codex tag filter, or no Codex tag filter is set (tags P5). Read from the session's Codex mark only, so the
    /// Claude marks do not decide it.
    /// </summary>
    private bool CodexTagShows(SessionSummary session) =>
        TagIndex.Has(MarkOf(ProviderId.Codex, session.Key.SessionId), ActiveCodexTagFilter);

    /// <summary>The Codex marks of the sessions in the history: what the Codex tag menu counts (tags P5).</summary>
    private IEnumerable<LibraryEntry> CodexHistoryMarks() =>
        _codexSessions.Select(session => MarkOf(ProviderId.Codex, session.Key.SessionId)).OfType<LibraryEntry>();

    /// <summary>
    /// A Codex tag that no Codex mark holds any more clears the Codex tag filter, with a status line (tags P5). Nothing is cleared while library.json
    /// cannot be read. Claude's filter is left alone.
    /// </summary>
    private void DropMissingCodexTagFilter()
    {
        if (_codexTagFilter is null || _libraryError is not null) return;
        if (TagIndex.Counts(MarksOf(ProviderId.Codex).Values).Any(tag => SameTag(tag.Tag, _codexTagFilter))) return;
        _codexTagFilter = null;
        UpdateFilterMark();
        SetStatus("The tag filter is cleared: no Codex session has that tag any more.");
    }

    /// <summary>
    /// The Codex part of the filter dropdown (tags P5): "Filter by tag #x (n)" for each tag the Codex sessions in the history hold, with a tick on
    /// the one in use, and "Clear tag filter" while one is set. Each click works as Claude's tag entries do. No view entry is listed: the grouping
    /// entries follow these (<see cref="SessionWindow.AddGroupingItems"/>, tags P5).
    /// </summary>
    private void AddCodexTagItems(ContextMenu menu)
    {
        var tags = TagIndex.Counts(CodexHistoryMarks());
        var active = ActiveCodexTagFilter;
        if (tags.Count == 0 && active is null)
        {
            var reason = _libraryError is null ? "No tags on Codex sessions yet" : "No tags: library.json cannot be read";
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
            item.Click += (_, _) => ChooseTag(SameTag(name, ActiveCodexTagFilter) ? null : name);
            menu.Items.Add(item);
        }
        if (active is not null)
        {
            var clear = new MenuItem { Header = "Clear tag filter", Padding = new Thickness(10, 4, 16, 4) };
            clear.Click += (_, _) => ChooseTag(null);
            menu.Items.Add(clear);
        }
    }

    /// <summary>
    /// The archived Codex sessions, in one collapsed group at the end of the history (D1). Their rows are muted, and each row's menu unarchives it,
    /// so an archive is seen and reversed here (the rail has no Archived view under Codex).
    /// </summary>
    private TreeViewItem CodexArchivedItem(IReadOnlyList<SessionSummary> archived)
    {
        var item = NewItem(Label($"Archived ({archived.Count})", bold: true), new RailTag(RailKind.Heading, "codex-archived"),
            "Codex sessions you archived. Each one is listed here only. Its menu unarchives it.");
        item.IsExpanded = false;
        foreach (var session in archived) item.Items.Add(CodexRow(session));
        return item;
    }

    private TreeViewItem CodexFolderItem(CodexFolder folder)
    {
        var named = folder.Folder.Length > 0;
        var leaf = named ? LeafOf(folder.Folder) : string.Empty;
        var header = !named ? "No folder named" : leaf.Length > 0 ? leaf : folder.Folder;
        var tooltip = named ? $"Folder: {folder.Folder}" : "The rollouts do not name the working folder.";
        var item = NewItem(Label(header, bold: true), new RailTag(RailKind.Heading, "codex-folder:" + folder.Folder), tooltip);
        item.IsExpanded = true;
        foreach (var session in folder.Sessions.Take(MaxHistoryPerFolder)) item.Items.Add(CodexRow(session));
        return item;
    }

    /// <summary>
    /// One Codex session (D1 for its name, tags, note, colour and archive). Its action is the resume in a terminal tab: from its menu, or a double
    /// click (C3). The second line has the model; the third the time and the tags.
    /// </summary>
    private TreeViewItem CodexRow(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var mark = MarkOf(ProviderId.Codex, id);
        var title = CodexRowTitle(mark?.Name, session);
        // The third line: the time, the tags, then the plan figure "3/7" when the session has a plan file (parity P3).
        var detail = RelativeTime(session.LastActivity) + TagsSuffix(mark);
        if (PlanFigure(ProviderId.Codex, id) is { } plan) detail = detail.Length == 0 ? plan : detail + " · " + plan;
        var line = TwoLine(title, session.Model ?? string.Empty, detail);
        var item = NewItem(ColourEdge(line, mark?.Colour),
            new RailTag(RailKind.CodexSession, id, Detail: session.Folder),
            NoteTip(mark) + PlanTip(mark) + "Double click, or the menu, to resume it in a terminal tab.");
        if (mark?.Archived == true) item.Opacity = 0.55;
        item.ContextMenu = CodexRowMenu(session, title);
        item.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            ResumeCodex(session);
        };
        return item;
    }

    /// <summary>
    /// A Codex row's title (D1): the owner's name when there is one, else the title from the history (C1), else "Codex session" and the first eight
    /// characters of the id. The order is the same as a Claude row's (SessionLibrary.TitleFor).
    /// </summary>
    internal static string CodexRowTitle(string? name, SessionSummary session)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        if (!string.IsNullOrWhiteSpace(session.Title)) return session.Title;
        var id = session.Key.SessionId;
        return $"Codex session {id[..Math.Min(8, id.Length)]}";
    }

    /// <summary>
    /// The right-click menu of a Codex row (D1): resume, pin or unpin, archive or unarchive, and the edit dialog for the name, tags, note and colour.
    /// The pin order is the Claude row's (Library.cs RowMenu): Pin before Archive.
    /// </summary>
    private ContextMenu CodexRowMenu(SessionSummary session, string title)
    {
        var id = session.Key.SessionId;
        var mark = MarkOf(ProviderId.Codex, id) ?? new LibraryEntry();
        var menu = StyledMenu();
        menu.Items.Add(MenuItemFor("Resume…", () => ResumeCodexScreen(session)));
        menu.Items.Add(MenuItemFor("Resume in a terminal tab", () => ResumeCodex(session)));
        menu.Items.Add(MenuItemFor(mark.Pinned ? "Unpin" : "Pin", () => SetCodexPinned(id, !mark.Pinned)));
        menu.Items.Add(MenuItemFor(mark.Archived ? "Unarchive" : "Archive", () => SetCodexArchived(id, !mark.Archived)));
        menu.Items.Add(MenuItemFor("Edit…", () => EditMarkOf(ProviderId.Codex, id, title)));
        return menu;
    }

    /// <summary>
    /// Pins or unpins a Codex session. The change is written under its codex: key (D1), so Claude's marks are not touched. Unlike
    /// <see cref="SessionWindow.SetPinned"/>, which writes Claude's (parity P4).
    /// </summary>
    private bool SetCodexPinned(string sessionId, bool pinned) =>
        ChangeMarkOf(ProviderId.Codex, sessionId, mark => mark with { Pinned = pinned });

    /// <summary>Archives or unarchives a Codex session. The change is written under its codex: key (D1).</summary>
    private bool SetCodexArchived(string sessionId, bool archived) =>
        ChangeMarkOf(ProviderId.Codex, sessionId, mark => mark with { Archived = archived });

    /// <summary>
    /// Loads the Codex history off the UI thread (C1). Nothing is written. A failed read keeps the list it had and says so in the status line.
    /// </summary>
    private async Task LoadCodexHistoryAsync()
    {
        if (_codexLoading) return;
        _codexLoading = true;
        if (IsCodexActive) RefreshRail();
        try
        {
            var provider = _codex ??= new CodexProvider();
            _codexSessions = await provider.ListAsync(CancellationToken.None);
            _codexRead = true;
            _codexFailed = false;
            AppLog.Write($"codex history read: {_codexSessions.Count} sessions");
            OpenCodexResumeForShot();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _codexFailed = true;
            AppLog.Write($"codex history read failed: {ex.GetType().Name}");
            SetStatus($"Codex history not read: {ex.GetType().Name}");
        }
        finally
        {
            _codexLoading = false;
            if (IsCodexActive) RefreshRail();
        }
    }

    /// <summary>
    /// Resumes a Codex session in a terminal tab: <c>codex resume &lt;id&gt;</c>, run in the session's folder (C3). A session whose tab is already open
    /// is shown and not started again, so one conversation has one process in this window.
    /// </summary>
    private void ResumeCodex(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.Session && t.Provider == ProviderId.Codex && SameId(t.Key, id));
        if (open is not null)
        {
            SetActive(open);
            if (_terminals.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
            return;
        }

        string command;
        try
        {
            command = (_codex ??= new CodexProvider()).ResumeCommandLine(id);
        }
        catch (ArgumentException)
        {
            SetStatus("Not resumed: the session id is not a valid Codex id.");
            return;
        }
        var folder = session.Folder is { } path && Directory.Exists(path) ? path : _options.WorkDir;
        OpenCodexTab(id, CodexRowTitle(MarkOf(ProviderId.Codex, id)?.Name, session), command, folder);
    }

    /// <summary>"New Codex session…": asks for a folder, then opens <c>codex -C</c> there in a terminal tab (C3, C4). Cancel starts nothing.</summary>
    private void NewCodexSession()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the Codex session" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        string command;
        try
        {
            command = (_codex ??= new CodexProvider()).NewCommandLine(folder);
        }
        catch (ArgumentException ex)
        {
            SetStatus($"Not started: {ex.Message}");
            return;
        }
        OpenCodexTab("new-" + Guid.NewGuid().ToString("N"), $"Codex: {LeafOf(folder)}", command, folder);
    }

    /// <summary>
    /// Opens a Codex terminal tab: a terminal that runs <paramref name="command"/> in <paramref name="folder"/>, owned by this window (C3). A Codex
    /// tab has no thread link (B6): its key is the id it was opened with, the session's id for a resume or a new unique id for a new session.
    /// </summary>
    private void OpenCodexTab(string id, string title, string command, string folder)
    {
        var terminal = new ExtendedTerminalControl
        {
            StartupCommandLine = command,
            WorkingDirectory = folder,
            Visibility = Visibility.Hidden,
        };
        _terminals[new SessionKey(ProviderId.Codex, id)] = terminal;
        Pane.Children.Add(terminal);
        ConfigureTerminal(terminal);
        var tab = AddTab(TabKind.Session, id, title, ProviderId.Codex);
        SetActive(tab);
        ShowOnly(terminal);
        SetStatus($"Codex tab opened in {folder}.");
    }

    /// <summary>The last part of a folder path, or the path itself when it has none (a drive root).</summary>
    private static string LeafOf(string folder)
    {
        var leaf = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return leaf.Length > 0 ? leaf : folder;
    }
}

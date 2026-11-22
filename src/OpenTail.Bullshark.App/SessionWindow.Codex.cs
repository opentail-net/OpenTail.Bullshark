using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;
using Wpf.Ui.Terminal;
using Wpf.Ui.Terminal.Internals;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Codex in the window (stage C3, C4): the provider dropdown at the top of the rail, the Codex rows under it, and the Codex terminal tabs.
/// The Codex rows are history only (C-D4): a row resumes in a terminal tab, and its menu can open a read-only transcript (parity P7).
/// Claude's tabs and records are not changed by any of this: they stay as they were, whatever the dropdown shows. Codex has no supervisor (B5), so its rows are never live.
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

    /// <summary>
    /// The view the Codex rail shows: All, History or Archived, as Claude's View menu names them (parity P11, A-D1). Kept apart from Claude's view, which
    /// the Codex rail does not use. One at a time, held for the window run, as Claude's view is (not kept between launches).
    /// </summary>
    private RailFilter _codexView = RailFilter.All;

    /// <summary>True once the Codex history has been read in this window.</summary>
    private bool _codexRead;

    /// <summary>True while the Codex history is being read, so a second read does not start.</summary>
    private bool _codexLoading;

    /// <summary>
    /// True once the window's Closing has started (R6). A Codex process that ends because the app detaches its terminals is then expected, and is not
    /// shown as ended.
    /// </summary>
    private bool _appShuttingDown;

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
        ProviderBox.Items.Add(new ComboBoxItem { Content = "Copilot", Tag = ProviderId.Copilot });
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
        IProvider next = id == ProviderId.Codex ? (_codex ??= new CodexProvider())
            : id == ProviderId.Copilot ? (_copilot ??= new CopilotProvider())
            : _claude;
        _activeProvider = next;
        ProviderStore.Save(_options.StatePath, id);
        ApplyProviderChrome();
        _railSignature = null;
        if (IsCodexActive && !_codexRead) _ = LoadCodexHistoryAsync();
        if (IsCopilotActive && !_copilotRead) _ = LoadCopilotHistoryAsync();
        RefreshRail();
    }

    /// <summary>
    /// The chrome that is Claude's: the status bar's Claude items (C4). The search box is on under both providers: Claude's search under Claude, and the
    /// Codex history search under Codex (parity P6), and its hint says which. The filter box stays under Codex, where its menu offers
    /// the Codex tags (tags P5). The Claude items are set again by the next pass when Claude shows.
    /// </summary>
    private void ApplyProviderChrome()
    {
        var claude = ClaudeSupervision;
        UpdateFilterMark();
        SearchBox.IsEnabled = claude || IsCodexActive || IsCopilotActive;
        SearchHint.Text = claude ? _claudeSearchHint : IsCodexActive ? CodexSearchHintText : IsCopilotActive ? CopilotSearchHintText : string.Empty;
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
    /// pinned sessions (parity P4), then the history grouped by folder, newest folder first, or by tag under Group by tag (tags P5). The view in use
    /// (All, History or Archived, parity P11) decides which of these are listed (<see cref="ProviderRailGroups.PlaceOf"/>). Replaces the
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
                // A tag filter or the view that leaves no row says so, so an empty rail is not taken for a fault (tags P5, parity P11).
                if (pinned.Count == 0 && listed == 0 && archived.Count == 0)
                {
                    if (ActiveCodexTagFilter is { } tag)
                        section.Items.Add(NewItem(Label($"No Codex session has the tag #{tag}", muted: true), new RailTag(RailKind.Heading, "codex:tag-empty"),
                            "The tag filter shows the Codex sessions that carry the tag. Clear it from the filter menu."));
                    else if (_codexView == RailFilter.Archived)
                        section.Items.Add(NewItem(Label("No Codex session is archived", muted: true), new RailTag(RailKind.Heading, "codex:archived-empty"),
                            "Archive a Codex session from its menu, and it is listed here."));
                }
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
    /// The Codex folders under the view in use, newest first. The sessions in each are newest first too. Under All and History a folder holds the
    /// sessions that are neither archived nor pinned: an archived one is in the Archived group, a pinned one in the Pinned group only (parity P4). Under
    /// Archived a folder holds the archived sessions (parity P11). With a Codex tag filter, a session without the tag is left out, so a folder with none
    /// left is not listed (tags P5). The rule is <see cref="ProviderRailGroups.PlaceOf"/>, shared with the tag groups.
    /// </summary>
    private IReadOnlyList<CodexFolder> CodexFolders() =>
        _codexSessions
            .Where(session => CodexPlaceOf(session) == ProviderPlace.FolderGroups)
            .GroupBy(session => session.Folder ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CodexFolder(group.Key, ProviderRailGroups.Sorted(group).ToList()))
            .OrderByDescending(folder => folder.Sessions[0].LastActivity ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>
    /// Where a Codex session is listed under the view in use (parity P11, A-D2): the Pinned group, the folders or tag groups, the Archived group, or
    /// nowhere. The Codex marks decide it, and the Codex tag filter applies first (tags P5). The pin and the archive are the owner's codex: entries (D1),
    /// never Claude's, and Claude's filter does not apply (the Codex rail has no filter of its own).
    /// </summary>
    private ProviderPlace CodexPlaceOf(SessionSummary session) =>
        ProviderRailGroups.PlaceOf(_codexView, MarkOf(ProviderId.Codex, session.Key.SessionId), ActiveCodexTagFilter);

    /// <summary>The Codex sessions in the Pinned group, newest first (parity P4), under the view in use: All and History only (parity P11).</summary>
    private IReadOnlyList<SessionSummary> CodexPinned() =>
        _codexSessions.Where(session => CodexPlaceOf(session) == ProviderPlace.PinnedGroup)
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>
    /// The Codex tag groups under Group by tag (tags P5): the sessions the view in use lists in the folders (parity P11), with the Codex tag filter
    /// applied first. The groups are those of <see cref="ProviderRailGroups.ByTag"/>, with the Codex marks.
    /// </summary>
    private IReadOnlyList<TagGroup<SessionSummary>> CodexTagGroups() =>
        ProviderRailGroups.ByTag(_codexSessions, id => MarkOf(ProviderId.Codex, id), ActiveCodexTagFilter, _codexView);

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

    /// <summary>
    /// The archived Codex sessions of the All view, newest first, with the Codex tag filter applied (tags P5). Under the other views the list is empty:
    /// Archived lists its sessions in the folders (parity P11).
    /// </summary>
    private IReadOnlyList<SessionSummary> CodexArchived() =>
        _codexSessions.Where(session => CodexPlaceOf(session) == ProviderPlace.ArchivedGroup)
            .OrderByDescending(s => s.LastActivity ?? DateTimeOffset.MinValue).ToList();

    /// <summary>The tag the Codex rail filters by, or null. Nothing is filtered while library.json cannot be read (D-L3).</summary>
    private string? ActiveCodexTagFilter => _libraryError is null ? _codexTagFilter : null;

    /// <summary>
    /// Sets the view the Codex rail shows (parity P11, A-D1). Claude's views that Codex does not have (Running, Temporary) fall back to All. The rail is
    /// drawn again at once, and Claude's view is not changed.
    /// </summary>
    private void ChooseCodexView(RailFilter view)
    {
        _codexView = IsCodexView(view) ? view : RailFilter.All;
        UpdateFilterMark();
        RefreshRail();
    }

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
    /// the one in use, and "Clear tag filter" while one is set. Each click works as Claude's tag entries do. The view entries come before these, and the
    /// grouping entries after (<see cref="SessionWindow.AddGroupingItems"/>, tags P5, parity P11).
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
    /// so an archive is seen and reversed here. Under All only: the Archived view lists them in its folders (parity P11).
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
        var detail = RelativeTime(session.LastActivity, _behaviour.State.ClampClockBackwards) + TagsSuffix(mark);
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
    /// The right-click menu of a Codex row (D1): resume, view the transcript (parity P7), pin or unpin, archive or unarchive, and the edit dialog for the
    /// name, tags, note and colour. The pin order is the Claude row's (Library.cs RowMenu): Pin before Archive.
    /// </summary>
    private ContextMenu CodexRowMenu(SessionSummary session, string title)
    {
        var id = session.Key.SessionId;
        var mark = MarkOf(ProviderId.Codex, id) ?? new LibraryEntry();
        var menu = StyledMenu();
        menu.Items.Add(MenuItemFor("Resume…", () => ResumeCodexScreen(session)));
        menu.Items.Add(MenuItemFor("Resume in a terminal tab", () => ResumeCodex(session)));
        menu.Items.Add(MenuItemFor("View transcript", () => OpenCodexTranscript(session)));
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
            OpenCodexTranscriptForShot();
            OpenCodexSearchForShot();
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
    private void ResumeCodex(SessionSummary session) => FireAndForget.Observe(ResumeCodexAsync(session), "codex resume");

    /// <summary>
    /// The start of <see cref="ResumeCodex"/>. The version check runs before the tab is made (R4), and the open tab is looked for again after each wait,
    /// since the checks wait: a second request meanwhile shows the tab that the first one opened. Then the resume ladder (R7): the saved id when its
    /// rollout is found, else the picker, or the newest session in the folder when <c>resume.allowNewestInFolder</c> allows it, or a refusal. The step
    /// and the label are logged by name and counts only; the label is the tab title (the newest and the picker tabs) and the status line.
    /// </summary>
    private async Task ResumeCodexAsync(SessionSummary session)
    {
        var id = session.Key.SessionId;
        if (ShowOpenCodexTab(id)) return;

        var provider = _codex ??= new CodexProvider();
        string command;
        try
        {
            command = provider.ResumeCommandLine(id);
        }
        catch (ArgumentException)
        {
            SetStatus("Not resumed: the session id is not a valid Codex id.");
            return;
        }
        var folder = session.Folder is { } path && Directory.Exists(path) ? path : _options.WorkDir;

        var launch = await CodexLaunchCheckAsync();
        if (launch.Refusal is { } refusal)
        {
            SetStatus(refusal);
            return;
        }
        if (ShowOpenCodexTab(id)) return;

        // A look that fails is a refusal, not a miss: a saved session that may be there is never replaced by another step.
        bool found;
        try
        {
            found = await Task.Run(() => provider.HasRollout(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"codex resume not started: the saved session's file could not be looked for ({ex.GetType().Name})");
            SetStatus(WithWarning(launch.Warning, "Not resumed: the saved session's file could not be looked for."));
            return;
        }
        if (ShowOpenCodexTab(id)) return;

        // The newest-in-folder candidates are the history's sessions in the folder that exists (the one the saved session was in), without the saved one.
        var sameFolder = session.Folder is { } saved && Directory.Exists(saved) ? saved : null;
        var candidates = CodexHistoryQuery.NewestInFolderCandidates(_codexSessions, sameFolder, id);
        var choice = ResumeLadder.Decide(found, candidates.TopRankCount, candidates.NewestId, candidates.EarliestTopRankId, _behaviour.Resume);
        AppLog.Write($"codex resume step {choice.Step}: saved file found {found}, {candidates.TopRankCount} candidates at the top rank");

        switch (choice.Step)
        {
            case ResumeStep.Saved:
                OpenCodexTab(id, CodexRowTitle(MarkOf(ProviderId.Codex, id)?.Name, session), command, folder).LadderStep = ResumeStep.Saved;
                break;
            case ResumeStep.Picker:
                // The picker is not one conversation, so its tab has a unique key: it never stands in for the saved session's tab.
                OpenCodexTab("picker-" + Guid.NewGuid().ToString("N"), choice.Label, provider.PickerCommandLine(), folder).LadderStep = ResumeStep.Picker;
                break;
            case ResumeStep.NewestInFolder:
                // The candidate's own tab, keyed by its id, so one conversation keeps one tab (an open one is shown, not started again).
                var newest = choice.CandidateId!;
                if (ShowOpenCodexTab(newest))
                {
                    SetStatus(WithWarning(launch.Warning, choice.Label));
                    return;
                }
                string newestCommand;
                try
                {
                    newestCommand = provider.ResumeCommandLine(newest);
                }
                catch (ArgumentException)
                {
                    SetStatus(WithWarning(launch.Warning, "Not resumed: the newest session's id is not a valid Codex id."));
                    return;
                }
                OpenCodexTab(newest, choice.Label, newestCommand, folder).LadderStep = ResumeStep.NewestInFolder;
                break;
            default:
                SetStatus(WithWarning(launch.Warning, choice.Label));
                return;
        }
        SetStatus(WithWarning(launch.Warning, choice.Label));
    }

    /// <summary>Shows the terminal tab of a Codex session that is already open in this window. True when there was one to show.</summary>
    private bool ShowOpenCodexTab(string id)
    {
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.Session && t.Provider == ProviderId.Codex && SameId(t.Key, id));
        if (open is null) return false;
        SetActive(open);
        if (_terminals.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
        return true;
    }

    /// <summary>
    /// "New Codex session…": asks for a folder, then the model (P9, M-D4 b), then opens <c>codex -C</c> there in a terminal tab (C3, C4). Cancel at either
    /// step starts nothing. Codex's default is used when the model list could not be read.
    /// </summary>
    private void NewCodexSession() => FireAndForget.Observe(NewCodexSessionAsync(), "new Codex session");

    private async Task NewCodexSessionAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the Codex session" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        var modelDialog = new CodexModelPrompt($"New Codex session in {LeafOf(folder)}", Palette, CodexModels()) { Owner = this };
        modelDialog.ShowDialog();
        if (!modelDialog.Accepted)
        {
            SetStatus("Not started.");
            return;
        }
        string command;
        try
        {
            command = (_codex ??= new CodexProvider()).NewCommandLine(folder, modelDialog.Model);
        }
        catch (ArgumentException ex)
        {
            SetStatus($"Not started: {ex.Message}");
            return;
        }
        var launch = await CodexLaunchCheckAsync();
        if (launch.Refusal is { } refusal)
        {
            SetStatus(refusal);
            return;
        }
        OpenCodexTab("new-" + Guid.NewGuid().ToString("N"), $"Codex: {LeafOf(folder)}", command, folder);
        if (launch.Warning is { } warning) SetStatus(WithWarning(warning, StatusText.Text));
    }

    /// <summary>
    /// Opens a Codex terminal tab: a terminal that runs <paramref name="command"/> in <paramref name="folder"/>, owned by this window (C3). A Codex
    /// tab has no thread link (B6): its key is the id it was opened with, the session's id for a resume or a new unique id for a new session.
    /// </summary>
    private OpenTab OpenCodexTab(string id, string title, string command, string folder)
    {
        var terminal = new ExtendedTerminalControl
        {
            StartupCommandLine = command,
            WorkingDirectory = folder,
            Visibility = Visibility.Hidden,
        };
        _terminals[new SessionKey(ProviderId.Codex, id)] = terminal;
        var tab = AddTab(TabKind.Session, id, title, ProviderId.Codex);
        // Watched before the control is shown: its process starts when the control loads, so the watch must be in place by then (R6).
        if (terminal.ConPTYTerm is { } term) WatchCodexExit(tab, term);
        Pane.Children.Add(terminal);
        ConfigureTerminal(terminal);
        SetActive(tab);
        ShowOnly(terminal);
        SetStatus($"Codex tab opened in {folder}.");
        return tab;
    }

    /// <summary>
    /// Watches for the start of a Codex terminal's process (R6). The connection raises <c>TermReady</c> on its start thread once the process object
    /// exists, so the watch reads it there and starts one wait for it. The watch is one-shot: it unsubscribes as it fires, so a process gets one wait.
    /// </summary>
    private void WatchCodexExit(OpenTab tab, TermPTY term)
    {
        EventHandler? started = null;
        started = (_, _) =>
        {
            term.TermReady -= started;
            // The process is set before TermReady is raised. Null only when the start produced no process: then there is nothing to wait for.
            if (term.Process is not { } process) return;
            FireAndForget.Observe(WaitForCodexExitAsync(tab, process), "codex exit wait");
        };
        term.TermReady += started;
    }

    /// <summary>
    /// The one wait for a Codex process (R6). A pool thread blocks in <c>WaitForExit</c> until the process ends, then the end is handled on the UI
    /// thread. A failed wait is logged by <see cref="FireAndForget"/>. A dispatcher that is shutting down takes no more work, as the app is closing.
    /// </summary>
    private async Task WaitForCodexExitAsync(OpenTab tab, IProcess process)
    {
        // A process that has ended before this wait began can make WaitForExit throw (its id is gone). That is an end, not a failure, so it is handled as one.
        await Task.Run(() =>
        {
            try
            {
                process.WaitForExit();
            }
            catch (ArgumentException)
            {
                // The process had already ended: nothing left to wait for.
            }
        }).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() => CodexProcessEnded(tab, process));
    }

    /// <summary>
    /// Handles the end of a Codex terminal's process on the UI thread (R6). The end is classified, and logged with its expected flag and text only.
    /// An expected end changes nothing. Any other end marks the tab ended, but only when the tab is still open and its process is still the one that
    /// ended, so an old end never changes a newer tab. Nothing is retried, and the tab stays open.
    /// </summary>
    private void CodexProcessEnded(OpenTab tab, IProcess process)
    {
        var record = CodexExit.Classify(tab.ClosedByOwner, _appShuttingDown, exitCode: null);
        var step = tab.LadderStep is { } ladder ? $"resume step {ladder}; " : string.Empty;
        AppLog.Write($"codex tab process ended: {step}expected {record.Expected}; {record.Text}");
        if (record.Expected) return;
        if (!_tabs.Contains(tab)) return;
        if (!_terminals.TryGetValue(KeyOf(tab), out var terminal) || !ReferenceEquals(terminal.ConPTYTerm?.Process, process)) return;

        tab.Ended = true;
        tab.Title = CodexExit.EndedTitle(tab.Title);
        RefreshTabStrip();
        SetStatus(record.Text);
    }

    /// <summary>The last part of a folder path, or the path itself when it has none (a drive root).</summary>
    private static string LeafOf(string folder)
    {
        var leaf = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return leaf.Length > 0 ? leaf : folder;
    }
}

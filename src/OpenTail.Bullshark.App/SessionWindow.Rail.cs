using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Search;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>What a rail item stands for. It is stored in the item's Tag.</summary>
public enum RailKind
{
    /// <summary>A recorded session. Key: its session id. Shows its terminal.</summary>
    Session,

    /// <summary>A past session from the transcripts. Key: its session id. Shows its transcript.</summary>
    History,

    /// <summary>The Trust step, while a folder is untrusted. Key: the folder key. Shows that Trust terminal.</summary>
    Trust,

    /// <summary>A live folder group. Key: the folder key. Shows the folder's setup page.</summary>
    Folder,

    /// <summary>A heading or a failed start. It shows nothing and only collapses.</summary>
    Heading,

    /// <summary>The History section at the bottom of the rail. It holds the 🔄 mark that reloads the history.</summary>
    HistorySection,
}

/// <summary>The meaning of one rail item. Detail carries the folder path for folder items.</summary>
public sealed record RailTag(RailKind Kind, string Key, SessionDigest? Digest = null, string? Detail = null);

/// <summary>
/// What the rail shows (owner, 2026-11-15). All: the live folders and the history, without the temporary folders. Running:
/// the live folders only. History: the history only, without the temporary folders. Temporary: only the temporary folders.
/// Archived (session library, D-L5): only the archived past sessions.
/// </summary>
public enum RailFilter
{
    All,
    Running,
    History,
    Temporary,
    Archived,
}

/// <summary>
/// The rail (S3, S7): live sessions grouped by folder at the top, and one History section at the bottom with past sessions
/// grouped by folder. The History section is read when the window opens, and again when the owner presses 🔄.
/// Live sessions are grouped by the folder of their spec. Records that match no spec go in "Other sessions", so they are not dropped.
/// </summary>
public partial class SessionWindow
{
    private const string OtherKey = "other-sessions";
    private const string HistoryKey = "history";
    private const int MaxHistoryPerFolder = 200;

    /// <summary>The past sessions read from the transcripts. Null until the first read has finished.</summary>
    private List<SessionDigest>? _history;

    private bool _historyLoading;

    /// <summary>The cache facts of the running sessions, read in the pass (off the UI thread). Replaced on every pass.</summary>
    private Dictionary<string, TailFacts> _runningFacts = new(StringComparer.Ordinal);

    /// <summary>The cache facts of the past sessions, read with the history.</summary>
    private Dictionary<string, TailFacts> _historyFacts = new(StringComparer.Ordinal);

    /// <summary>The ui.json key of the context window the ctx meter uses (Settings, General). Missing gives the assumed 200,000.</summary>
    private const string ContextWindowKey = "contextWindow";

    /// <summary>The context window in tokens, read from ui.json on each rail build (SessionWindow.Settings.cs). UI thread only.</summary>
    private long _contextWindow = ContextMeter.DefaultWindow;

    /// <summary>The plan counts of the sessions' plan files, kept between rail builds (SessionWindow.Library.cs). UI thread only.</summary>
    private readonly PlanProgressCache _planProgress = new();

    /// <summary>Whether each folder, heading or section is expanded, kept across rebuilds.</summary>
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);

    private RailTag? _selected;
    private bool _rebuilding;
    private RailTheme _theme = RailTheme.Dark;

    private RailPalette Palette => RailPalette.For(_theme);

    /// <summary>The filter the owner picked. It is not kept between launches: every start shows All.</summary>
    private RailFilter _filter = RailFilter.All;

    private bool ShowsLive => _filter is RailFilter.All or RailFilter.Running;
    private bool ShowsHistory => _filter != RailFilter.Running;

    /// <summary>
    /// The tag the rail shows rows for, or null (tags plan, Part A). One at a time, held for the window run, as the view is (not kept between
    /// launches). It hides rows only: the Needs you group is never filtered by it.
    /// </summary>
    private string? _tagFilter;

    /// <summary>
    /// Whether the History section groups its sessions by tag rather than by folder (features batch G4). One at a time, held for the window run,
    /// as the tag filter is (not kept between launches). UI thread only.
    /// </summary>
    private bool _groupByTag;

    /// <summary>The tag filter in force. Nothing is filtered while library.json cannot be read: no tag is known then (D-L3).</summary>
    private string? ActiveTagFilter => _libraryError is null ? _tagFilter : null;

    private void UpdateFilterMark()
    {
        var tag = ActiveTagFilter;
        var grouping = _groupByTag ? " · by tag" : string.Empty;
        FilterMark.Text = $"show: {_filter.ToString().ToLowerInvariant()}{(tag is null ? string.Empty : " · #" + tag)}{grouping} ▾";
        // The label may be cut with an ellipsis when the tag is long, so the tooltip names the tag in full.
        FilterBox.ToolTip = tag is null ? "Choose what the rail shows" : $"Choose what the rail shows (tag filter #{tag})";
    }

    private ContextMenu? _filterMenu;

    /// <summary>The dropdown of views: all four are listed, with a tick on the one in use. Styled in the rail's palette.</summary>
    private void InitFilterMenu()
    {
        _filterMenu = new ContextMenu { Placement = PlacementMode.Bottom, HasDropShadow = true, PlacementTarget = FilterBox };
        foreach (var option in Enum.GetValues<RailFilter>())
        {
            var item = new MenuItem { Header = FilterLabel(option), Tag = option, IsCheckable = true, Padding = new Thickness(10, 4, 16, 4) };
            item.Click += (_, _) => ChooseFilter(option);
            _filterMenu.Items.Add(item);
        }
    }

    /// <summary>
    /// Rebuilds the part of the dropdown below the views: a separator, the tags in use with their counts (a tick on the one in use), and
    /// "Clear tag filter" when one is set. It is rebuilt each time the dropdown opens, so it lists the tags as the marks are now.
    /// </summary>
    private void RebuildTagMenu()
    {
        if (_filterMenu is null) return;
        var views = Enum.GetValues<RailFilter>().Length;
        while (_filterMenu.Items.Count > views) _filterMenu.Items.RemoveAt(views);

        var tags = TagIndex.Counts(_marks.Values);
        if (tags.Count > 0 || ActiveTagFilter is not null)
        {
            _filterMenu.Items.Add(new Separator());
            foreach (var tag in tags)
            {
                var name = tag.Tag;
                var item = new MenuItem
                {
                    Header = $"#{name} ({tag.Count})",
                    IsCheckable = true,
                    IsChecked = SameTag(name, ActiveTagFilter),
                    Padding = new Thickness(10, 4, 16, 4),
                };
                // A click on the tag in use clears it; a click on another tag makes it the one in use.
                item.Click += (_, _) => ChooseTag(SameTag(name, ActiveTagFilter) ? null : name);
                _filterMenu.Items.Add(item);
            }
            if (ActiveTagFilter is not null)
            {
                var clear = new MenuItem { Header = "Clear tag filter", Padding = new Thickness(10, 4, 16, 4) };
                clear.Click += (_, _) => ChooseTag(null);
                _filterMenu.Items.Add(clear);
            }
        }

        // The grouping of the History section (features batch G4): folders or tags, with a tick on the one in use.
        _filterMenu.Items.Add(new Separator());
        _filterMenu.Items.Add(new MenuItem { Header = "Group by", IsEnabled = false, Padding = new Thickness(10, 4, 16, 4) });
        foreach (var (label, byTag) in new[] { ("Folder", false), ("Tag", true) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _groupByTag == byTag, Padding = new Thickness(10, 4, 16, 4) };
            item.Click += (_, _) => ChooseGrouping(byTag);
            _filterMenu.Items.Add(item);
        }
    }

    /// <summary>
    /// Sets how the History section groups its sessions: by folder, or by tag (features batch G4). The filters and the search still apply, and
    /// Needs you is not affected.
    /// </summary>
    private void ChooseGrouping(bool byTag)
    {
        _groupByTag = byTag;
        UpdateFilterMark();
        SetStatus(byTag
            ? "The History section groups its sessions by tag. A session with several tags is under each of them."
            : "The History section groups its sessions by folder.");
        RefreshRail();
    }

    private static bool SameTag(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the tag the rail shows, or clears it (null). The view and the search still apply. The status line says what the rail shows now.
    /// </summary>
    private void ChooseTag(string? tag)
    {
        var wanted = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();
        _tagFilter = wanted;
        UpdateFilterMark();
        SetStatus(wanted is null ? "Tag filter cleared." : $"Showing the rows tagged #{wanted}. Needs you is never filtered.");
        RefreshRail();
    }

    /// <summary>
    /// A tag that no session holds any more (after an edit or a file change) clears the tag filter, with a status line (tags plan, Part A2).
    /// Nothing is cleared while library.json cannot be read.
    /// </summary>
    private void DropMissingTagFilter()
    {
        if (_tagFilter is null || _libraryError is not null) return;
        if (TagIndex.Counts(_marks.Values).Any(tag => SameTag(tag.Tag, _tagFilter))) return;
        _tagFilter = null;
        UpdateFilterMark();
        SetStatus("The tag filter is cleared: no session has that tag any more.");
    }

    private static string FilterLabel(RailFilter filter) => filter switch
    {
        RailFilter.Running => "Running",
        RailFilter.History => "History",
        RailFilter.Temporary => "Temporary",
        RailFilter.Archived => "Archived",
        _ => "All",
    };

    private void ChooseFilter(RailFilter filter)
    {
        _filter = filter;
        UpdateFilterMark();
        RefreshRail();
    }

    /// <summary>Opens the dropdown under the filter box, with the view in use ticked.</summary>
    private void FilterMark_Click(object sender, MouseButtonEventArgs e)
    {
        if (_filterMenu is null) return;
        RebuildTagMenu();
        foreach (var item in _filterMenu.Items.OfType<MenuItem>())
            if (item.Tag is RailFilter view) item.IsChecked = view == _filter;
        _filterMenu.IsOpen = true;
    }

    private sealed class RailGroup(string key, string header, string tooltip)
    {
        public string Key { get; } = key;
        public string Header { get; } = header;
        public string Tooltip { get; } = tooltip;
        public List<(SessionRecord Record, string State)> Sessions { get; } = [];

        /// <summary>The runs of this folder that wait for their start time (gems item 5). They have no sessions yet.</summary>
        public List<RunRecord> Waiting { get; } = [];
    }

    private void InitRail()
    {
        Rail.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnRailExpansionChanged));
        Rail.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(OnRailExpansionChanged));
        _theme = ThemeStore.Load(_options.StatePath);
        InitFilterMenu();
        // A test flag picks the starting filter (all, running, history, temporary). Without it the rail starts on All.
        if (Enum.TryParse<RailFilter>(_options.FilterName, ignoreCase: true, out var start)) _filter = start;
        // The same for the tag filter (--rail-tag): a test flag, the rail starts with that tag shown. Cleared at the first refresh if no session has it.
        if (!string.IsNullOrWhiteSpace(_options.FilterTag)) _tagFilter = _options.FilterTag.Trim().ToLowerInvariant();
        // --rail-group tag starts the History section grouped by tag, for the screenshots only (features batch G4).
        _groupByTag = string.Equals(_options.GroupName, "tag", StringComparison.OrdinalIgnoreCase);
        ApplyTheme();
    }

    /// <summary>Sets every colour the rail uses from the palette. The tree's own resources are set too, so its selection follows the theme.</summary>
    private void ApplyTheme()
    {
        var palette = Palette;
        UiTheme.Apply(palette);
        RailPanel.Background = RailPalette.Brush(palette.Background);
        SessionsTitle.Foreground = RailPalette.Brush(palette.Text);
        ThemeMark.Text = _theme == RailTheme.Dark ? "☀ light" : "☾ dark";
        ThemeMark.Foreground = RailPalette.Brush(palette.Muted);
        FilterMark.Foreground = RailPalette.Brush(palette.Text);
        Resources["ScrollThumb"] = RailPalette.Brush("#55" + palette.Muted.TrimStart('#'));
        Resources["ScrollThumbHover"] = RailPalette.Brush("#99" + palette.Muted.TrimStart('#'));
        TabBar.Background = RailPalette.Brush(palette.Panel);
        TabBarB.Background = RailPalette.Brush(palette.Panel);
        PaneB.Background = RailPalette.Brush(palette.Background);
        PaneSplitter.Background = RailPalette.Brush(palette.Muted);
        RailToggle.Foreground = RailPalette.Brush(palette.Muted);
        SearchBox.Background = RailPalette.Brush(palette.Panel);
        SearchBox.Foreground = RailPalette.Brush(palette.Text);
        SearchBox.CaretBrush = RailPalette.Brush(palette.Text);
        SearchBox.BorderBrush = RailPalette.Brush(palette.Muted);
        SearchHint.Foreground = RailPalette.Brush(palette.Muted);
        UpdateRailToggle();
        Background = RailPalette.Brush(palette.Background);
        StatusBar.Background = RailPalette.Brush(palette.Panel);
        // The bottom bar's text, icons and hover tint follow the theme (the defaults are in SessionWindow.xaml).
        Resources["BarText"] = RailPalette.Brush(palette.Text);
        Resources["BarMuted"] = RailPalette.Brush(palette.Muted);
        Resources["BarHover"] = RailPalette.Brush(palette.IsLight ? "#22000000" : "#33FFFFFF");
        StatusText.Foreground = RailPalette.Brush(palette.Muted);
        NeedsYouText.Foreground = RailPalette.Brush(palette.Text);
        RailSplitter.Background = RailPalette.Brush(palette.Border);
        Pane.Background = RailPalette.Brush(palette.Background);
        _setup?.ApplyTheme(palette);
        _search.ApplyTheme(palette);
        _transcript.ApplyTheme(palette);
        _changes.ApplyTheme(palette);
        _timeline.ApplyTheme(palette);
        _limits.ApplyTheme(palette);
        _away.ApplyTheme(palette);
        _night.ApplyTheme(palette);
        _storage.ApplyTheme(palette);
        _insights.ApplyTheme(palette);
        _settings.ApplyTheme(palette);
        // The window's own text colour follows the theme too: text with no colour of its own would otherwise stay the dark theme's light grey.
        Foreground = RailPalette.Brush(palette.Text);
        FilterBox.Background = RailPalette.Brush(palette.Panel);
        FilterBox.BorderBrush = RailPalette.Brush(palette.Muted);
        UpdateFilterMark();

        if (_filterMenu is not null)
        {
            _filterMenu.Background = RailPalette.Brush(palette.Panel);
            _filterMenu.BorderBrush = RailPalette.Brush(palette.Muted);
            _filterMenu.Foreground = RailPalette.Brush(palette.Text);
            _filterMenu.Resources[SystemColors.MenuBrushKey] = RailPalette.Brush(palette.Panel);
            _filterMenu.Resources[SystemColors.MenuTextBrushKey] = RailPalette.Brush(palette.Text);
            _filterMenu.Resources[SystemColors.ControlBrushKey] = RailPalette.Brush(palette.Panel);
            _filterMenu.Resources[SystemColors.ControlTextBrushKey] = RailPalette.Brush(palette.Text);
            _filterMenu.Resources[SystemColors.HighlightBrushKey] = RailPalette.Brush(palette.Selection);
            _filterMenu.Resources[SystemColors.HighlightTextBrushKey] = RailPalette.Brush(palette.SelectionText);
        }
        // The grip between the rail and the pane is drawn in the palette's muted colour, so it shows in both themes.
        RailSplitter.Background = RailPalette.Brush(palette.Muted);

        Rail.Background = RailPalette.Brush(palette.Background);
        Rail.Foreground = RailPalette.Brush(palette.Text);
        Rail.Resources[SystemColors.WindowBrushKey] = RailPalette.Brush(palette.Background);
        Rail.Resources[SystemColors.ControlBrushKey] = RailPalette.Brush(palette.Background);
        Rail.Resources[SystemColors.ControlTextBrushKey] = RailPalette.Brush(palette.Text);
        Rail.Resources[SystemColors.HighlightBrushKey] = RailPalette.Brush(palette.Selection);
        Rail.Resources[SystemColors.HighlightTextBrushKey] = RailPalette.Brush(palette.SelectionText);
        Rail.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = RailPalette.Brush(palette.Selection);
        Rail.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = RailPalette.Brush(palette.SelectionText);
    }

    /// <summary>Diagnostics for the rail splitter: whether a drag reaches it, and the rail width after it.</summary>
    private void Splitter_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) =>
        AppLog.Write("splitter: drag started");

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        AppLog.Write($"splitter: drag completed, rail width {RailPanel.ActualWidth:0}");

    /// <summary>The rail's switch between the dark and the light theme. The Settings page's Theme choice does the same (SetTheme).</summary>
    private void ThemeMark_Click(object sender, MouseButtonEventArgs e) => SetTheme(_theme == RailTheme.Dark ? RailTheme.Light : RailTheme.Dark);

    private void OnRailExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { Tag: RailTag { Kind: RailKind.Folder or RailKind.Heading or RailKind.HistorySection } tag } item)
            _expanded[tag.Key] = item.IsExpanded;
    }

    /// <summary>
    /// Rebuilds the rail from the state file, the last agent listing, the cache facts and the history read so far. The pass
    /// passes the records and the runs it read on a pool thread, so the drawing does not read the files again; other callers leave them out.
    /// </summary>
    private void RefreshRail(IReadOnlyDictionary<string, SessionRecord>? known = null, IReadOnlyList<RunRecord>? knownRuns = null)
    {
        // The marks are read again on every refresh: they are small, and another process may have changed them (D-L3 is inside).
        LoadLibrary();
        DropMissingTagFilter();
        // The context window is read with the other settings each build, so a change in Settings shows at the next one (ctx meter, Part 1).
        _contextWindow = ContextMeter.WindowFrom(UiFile.Read(_options.StatePath, ContextWindowKey));
        var stored = known ?? ReadRecords();
        // The palette's session entries are read from what was drawn here, so opening it reads no file (SessionWindow.Palette.cs).
        _railRecords = stored;
        var records = stored.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        // A start that failed has no session, so it gets no terminal (it is listed as a failure below).
        foreach (var record in records.Where(r => !string.IsNullOrEmpty(r.SessionId) && !IsInOwnWindow(r.SessionId))) EnsureTerminal(record);
        _attention = AttentionList.From(records, _agents, _clashes, ConflictsOf(records));
        UpdateNeedsYouCount();
        // The finished coders the owner has not looked at yet. Built from memory: the pass read their transcripts (ready-for-review D1).
        _review = ReviewQueue.From(records, _allAgents, _reviewSources, _reviewed);

        var groups = new Dictionary<string, RailGroup>(StringComparer.OrdinalIgnoreCase);
        RailGroup Folder(string folder)
        {
            var full = FolderKey.Normalise(folder);
            var key = FolderKey.From(full);
            if (!groups.TryGetValue(key, out var group))
            {
                var name = Path.GetFileName(full);
                groups[key] = group = new RailGroup(key, string.IsNullOrEmpty(name) ? full : name, full);
            }
            return group;
        }

        // The folders of this window's sessions come first, so a folder is listed before its sessions start.
        var runs = knownRuns ?? ReadRuns();
        var specs = RunStore.SpecsOf(runs);
        foreach (var spec in specs) Folder(spec.WorkingDirectory);
        // A run that waits for its start time is listed under its folder, with its time, so the owner can start it now (gems item 5).
        var now = DateTimeOffset.UtcNow;
        foreach (var run in runs.Where(r => RunStore.IsWaiting(r, now))) Folder(run.WorkingDirectory).Waiting.Add(run);

        // The folder's name of each live session, for the palette's keywords (SessionWindow.Palette.cs), taken from the runs read above.
        var folderNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var spec = specs.FirstOrDefault(s => string.Equals(s.Name, record.Name, StringComparison.Ordinal));
            var state = _agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase))
                            ?.EffectiveState ?? "not listed";
            if (spec is null)
            {
                if (!groups.TryGetValue(OtherKey, out var other))
                    groups[OtherKey] = other = new RailGroup(OtherKey, "Other sessions",
                        "Recorded sessions that are not part of this window's pattern.");
                other.Sessions.Add((record, state));
            }
            else
            {
                Folder(spec.WorkingDirectory).Sessions.Add((record, state));
                if (!string.IsNullOrEmpty(record.SessionId))
                    folderNames[record.SessionId] = Path.GetFileName(FolderKey.Normalise(spec.WorkingDirectory));
            }
        }
        _railFolders = folderNames;

        _rebuilding = true;
        try
        {
            Rail.Items.Clear();
            // Needs you comes first: a session that waits for the owner is the first thing to see. Shown with the live sessions only.
            if (ShowsLive && (_attention.Count > 0 || _daemonNotice is not null)) Rail.Items.Add(NeedsYouSection(stored));
            // Ready for review sits under Needs you. It is not counted there, and it plays no sound and flashes nothing (ready-for-review D2).
            if (ShowsLive && _review.Count > 0) Rail.Items.Add(ReadySection(stored));
            foreach (var (key, step) in _trustSteps)
                Rail.Items.Add(NewItem(Label($"Trust: {Path.GetFileName(step.Root)} · choose 'Yes, I trust this folder'", wrap: true),
                    new RailTag(RailKind.Trust, key), step.Root));

            var liveGroups = ShowsLive ? groups.Values.ToList() : new List<RailGroup>();
            foreach (var group in liveGroups)
            {
                // With a tag filter, a session row that does not hold the tag is left out; a failed start has no tags and stays. Needs you
                // lists the session anyway. A folder with no row left under the filter is not drawn (tags plan, Part A2).
                var sessions = group.Sessions
                    .Where(s => string.IsNullOrEmpty(s.Record.SessionId) || TagIndex.Has(MarkOf(s.Record.SessionId), ActiveTagFilter))
                    .OrderBy(s => s.Record.Name, StringComparer.Ordinal)
                    .ToList();
                if (ActiveTagFilter is not null && sessions.Count == 0 && group.Waiting.Count == 0) continue;

                var folderItem = NewItem(Label(group.Header, bold: true),
                    new RailTag(RailKind.Folder, group.Key, Detail: group.Tooltip), group.Tooltip);
                folderItem.IsExpanded = _expanded.GetValueOrDefault(group.Key, true);

                foreach (var (record, state) in sessions)
                {
                    if (string.IsNullOrEmpty(record.SessionId))
                    {
                        // A start that failed: shown, with the reason, and not selectable as a terminal.
                        folderItem.Items.Add(NewItem(Label($"{record.Name} · {record.LastAlert}", muted: true),
                            new RailTag(RailKind.Heading, "failed:" + record.Name), record.LastAlert));
                        continue;
                    }
                    folderItem.Items.Add(LiveRow(record, state));
                }
                foreach (var run in group.Waiting) folderItem.Items.Add(WaitingRow(run));
                Rail.Items.Add(folderItem);
            }

            // The names of the live folders, so the History section uses the same name for the same folder.
            var liveNames = groups.Values.ToDictionary(g => g.Key, g => g.Header, StringComparer.OrdinalIgnoreCase);
            if (ShowsHistory) Rail.Items.Add(HistorySection(liveNames, stored));

            RestoreSelection();
            SyncSessionTabs(stored);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>
    /// The History section: one label at its top, with the 🔄 mark that reloads it, and past sessions grouped by folder below.
    /// The folder name is the live folder's name when the folder is one of this window's, and the name Claude gave it otherwise.
    /// </summary>
    private TreeViewItem HistorySection(IReadOnlyDictionary<string, string> liveNames, IReadOnlyDictionary<string, SessionRecord> records)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Label("History", bold: true));
        var reload = Label(_historyLoading ? "⏳" : "🔄", muted: true, emoji: true);
        reload.Margin = new Thickness(8, 0, 0, 0);
        reload.Cursor = Cursors.Hand;
        reload.ToolTip = "Reload the history from the local transcripts (no model)";
        reload.MouseLeftButtonUp += async (_, _) => await LoadHistoryAsync();
        header.Children.Add(reload);
        // The magic rename of the cold sessions: asks first, at most ten a run (SessionWindow.MagicRename.cs).
        header.Children.Add(MagicHeaderMark());

        var section = NewItem(header, new RailTag(RailKind.HistorySection, HistoryKey));
        section.IsExpanded = _expanded.GetValueOrDefault(HistoryKey, true);

        // D-L3: while library.json cannot be read, the rail says so here, where it stays (the status line is overwritten).
        if (_libraryError is not null)
        {
            section.Items.Add(NewItem(Label("library.json cannot be read", muted: true),
                new RailTag(RailKind.Heading, HistoryKey + ":library"),
                "No names, pins, tags or colours are shown, and nothing is written until the file can be read. " + _libraryError));
        }

        if (_history is null)
        {
            section.Items.Add(NewItem(Label(_historyLoading ? "reading the transcripts…" : "not read yet", muted: true),
                new RailTag(RailKind.Heading, HistoryKey + ":none")));
            return section;
        }

        var shown = ShownHistoryFolders();

        // The sessions open now, listed by the agents or recorded. A history row for one of them gets no ⏩: it is already running.
        var openIds = _agents.Select(a => a.SessionId)
            .Concat(records.Values.Select(r => r.SessionId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (PinnedGroupShown && _history.Any(d => IsShownInHistory(d) && InPinnedGroup(d)))
            section.Items.Add(PinnedGroup(openIds));
        for (int i = 0; i < shown.Count; i++)
            section.Items.Add(HistoryFolderItem(shown[i], liveNames, openIds, expandByDefault: i == 0));
        return section;
    }

    /// <summary>
    /// One group the History section lists: a folder (<see cref="Key"/> is the folder, and <see cref="Tag"/> is null), or with Group by tag a tag
    /// (<see cref="Tag"/> is the tag in lower case, or null for the sessions with no tag, and <see cref="Key"/> is the group's own key).
    /// </summary>
    private sealed record HistoryGroup(string Key, string? Tag, IReadOnlyList<SessionDigest> Sessions);

    /// <summary>
    /// The groups the History section lists, in the order it lists them, for the current filter, tag and search: the folders (by their newest
    /// main session), or with Group by tag the tags in use (features batch G4). Test and scratch folders under the Windows temp folder are shown
    /// only by the Temporary filter (owner, 2026-11-15). The rail and the magic rename (which names the top sessions first) both read this, so
    /// they cannot disagree.
    /// </summary>
    private List<HistoryGroup> ShownHistoryFolders()
    {
        if (_history is null) return [];
        var tempPrefix = FolderKey.From(Path.GetTempPath());
        if (_groupByTag)
        {
            // The sessions the filters show, with the temporary rule applied per session (every session of a folder is in the same place).
            var sessions = _history
                .Where(IsShownInHistory)
                .Where(InFolders)
                .Where(d => IsTemporary(d.ProjectFolder, tempPrefix) == (_filter == RailFilter.Temporary))
                .ToList();
            return TagGrouping.Groups(sessions, id => MarkOf(id))
                .Select(group => new HistoryGroup(group.Tag is null ? "tag-none" : "tag:" + group.Tag, group.Tag, group.Sessions))
                .ToList();
        }
        var folders = _history
            .Where(IsShownInHistory)
            .Where(InFolders)
            .GroupBy(d => d.ProjectFolder, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => HistoryOrder.FolderRank(g))
            .ThenByDescending(g => HistoryOrder.FolderRecency(g))
            .ToList();
        return folders
            .Where(f => IsTemporary(f.Key, tempPrefix) == (_filter == RailFilter.Temporary))
            .Select(f => new HistoryGroup(f.Key, null, f.ToList()))
            .ToList();
    }

    /// <summary>
    /// One group of the History section, with its past sessions, newest first. A folder is named as the live folder's name when it is one of
    /// this window's. A tag group is "#tag (N)"; the sessions with no tag are "No tag (N)".
    /// </summary>
    private TreeViewItem HistoryFolderItem(
        HistoryGroup group,
        IReadOnlyDictionary<string, string> liveNames,
        ISet<string> openIds,
        bool expandByDefault = false)
    {
        var key = HistoryKey + ":" + group.Key;
        string header;
        string tooltip;
        if (_groupByTag)
        {
            header = group.Tag is null ? $"No tag ({group.Sessions.Count})" : $"#{group.Tag} ({group.Sessions.Count})";
            tooltip = group.Tag is null
                ? "Sessions with no tag."
                : $"Tag #{group.Tag}. A session with several tags is listed under each of them.";
        }
        else
        {
            header = liveNames.TryGetValue(group.Key, out var live) ? live : group.Key;
            tooltip = "Folder: " + group.Key + "\n(the name Claude gave the transcripts, F44)";
        }
        var folderItem = NewItem(Label(header), new RailTag(RailKind.Heading, key), tooltip);
        // OPENTAIL_BULLSHARK_EXPAND=1 expands the history folders, for screenshots only. Normally they start collapsed.
        // The top (most recent) folder starts expanded, so the sessions are visible at once. A collapse the owner makes is kept.
        folderItem.IsExpanded = _expanded.GetValueOrDefault(key,
            expandByDefault || Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_EXPAND") == "1");

        // Subagent transcripts come last, so the cap cuts them first (owner, 2026-11-18).
        foreach (var digest in HistoryOrder.Sorted(group.Sessions).Take(MaxHistoryPerFolder))
        {
            folderItem.Items.Add(HistoryRow(digest, openIds));
        }
        return folderItem;
    }

    /// <summary>True for the temp folder itself and for any folder under it. Compared on the folder key (F44).</summary>
    private static bool IsTemporary(string folderKey, string tempPrefix) =>
        folderKey.Equals(tempPrefix, StringComparison.OrdinalIgnoreCase)
        || folderKey.StartsWith(tempPrefix + "-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the local transcripts (no model) and the end of each one, off the UI thread. It runs when the window opens and
    /// when the owner presses 🔄. Nothing is written.
    /// </summary>
    private async Task LoadHistoryAsync()
    {
        if (_historyLoading) return;
        _historyLoading = true;
        RefreshRail();
        try
        {
            var index = await Task.Run(() => TranscriptIndex.Build(_options.ProjectsRoot, _options.IndexCachePath, AppLog.Write));
            var facts = await Task.Run(() => ReadHistoryFacts(index.Sessions));
            _history = index.Sessions.ToList();
            _historyFacts = facts;
            // A return that came before the index was read (the Away page, D2) asks again now that it is.
            if (_awayWaitsForHistory)
            {
                _awayWaitsForHistory = false;
                _ = RefreshAwayAsync(onReturn: true);
            }
            AppLog.Write($"history read: {index.Sessions.Count} transcripts, {index.Totals.CountedMessages} messages counted once");
            if (index.Skipped.Count > 0)
                AppLog.Write($"history: {index.Skipped.Count} transcript(s) could not be read and were left out");
        }
        catch (Exception ex)
        {
            AppLog.Write($"history read failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus($"history not read: {ex.Message}");
        }
        finally
        {
            _historyLoading = false;
            RefreshRail();
        }
    }

    /// <summary>The cache label of a session (short for the row, full for the tooltip). Recomputed from the facts on every call.</summary>
    private (string? Short, string? Full) CacheTextFor(string sessionId)
    {
        var facts = _runningFacts.TryGetValue(sessionId, out var running) ? running
                  : _historyFacts.TryGetValue(sessionId, out var past) ? past
                  : null;
        if (facts is null) return (null, null);
        var verdict = CacheRule.Classify(facts.LastRequest, DateTimeOffset.UtcNow, facts.Ttl, facts.ContextTokens);
        return (CacheRule.Short(verdict), CacheRule.Label(verdict));
    }

    /// <summary>
    /// Keeps the owner's selection across a rebuild, so the pane does not change under them. If the selected item is gone,
    /// the first session is chosen, then the Trust step, then the first folder. The pane is updated only in that case.
    /// </summary>
    private void RestoreSelection()
    {
        var kept = _selected is null ? null : FindItem(Rail.Items, _selected);
        if (kept is not null)
        {
            kept.IsSelected = true;
            return;
        }

        var fallback = AllItems(Rail.Items).FirstOrDefault(i => i.Tag is RailTag { Kind: RailKind.Session })
                    ?? AllItems(Rail.Items).FirstOrDefault(i => i.Tag is RailTag { Kind: RailKind.Trust })
                    ?? AllItems(Rail.Items).FirstOrDefault(i => i.Tag is RailTag { Kind: RailKind.Folder });
        if (fallback is null)
        {
            _selected = null;

            // With no live session in the rail, nothing is selected, and every refresh came here and showed the setup over a search
            // or a transcript opened from one. Those keep the pane, and so do Insights and Settings (found 2026-11-19 on real data: with no live
            // session, the history read finishing showed the setup over the Away page).
            if (_activeTab?.Kind is TabKind.Search or TabKind.Transcript or TabKind.Changes or TabKind.Insights or TabKind.Settings) return;
            ShowOnly(_setup);
            return;
        }

        _selected = (RailTag)fallback.Tag;
        fallback.IsSelected = true;

        // A refresh must not take the pane away from a search, or from a transcript the owner opened from a result: those have no
        // row selected in the rail, so the fallback would show the setup over them.
        if (_activeTab?.Kind is TabKind.Search or TabKind.Transcript or TabKind.Changes) return;
        _ = ShowRailItemAsync(_selected);
    }

    private async void Rail_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_rebuilding) return;
        if (e.NewValue is not TreeViewItem { Tag: RailTag tag }) return;
        _selected = tag;
        NoteRailSelection(tag);
        await ShowRailItemAsync(tag);
    }

    /// <summary>
    /// Opens a past session's transcript by its full id or the start of it (the test hook <c>--open-transcript</c>, TA4).
    /// A session that is not in the history is reported in the status line.
    /// </summary>
    private async Task OpenHistorySessionAsync(string sessionId)
    {
        var digest = _history?.FirstOrDefault(d =>
            d.SessionId.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase));
        if (digest is null)
        {
            AppLog.Write($"open transcript: no session {sessionId}");
            SetStatus($"No transcript for session {sessionId}.");
            return;
        }
        AppLog.Write($"open transcript: {digest.SessionId}");
        await ShowRailItemAsync(new RailTag(RailKind.History, digest.SessionId, digest));
    }

    private async Task ShowRailItemAsync(RailTag tag)
    {
        switch (tag.Kind)
        {
            case RailKind.Session when _terminals.TryGetValue(tag.Key, out var terminal):
                ShowOnly(terminal);
                break;

            case RailKind.Trust when _trustSteps.TryGetValue(tag.Key, out var step):
                ShowOnly(step.Terminal);
                break;

            case RailKind.History when tag.Digest is not null:
                ShowOnly(_transcript);
                await _transcript.ShowAsync(tag.Digest, title: DisplayTitle(tag.Digest));
                if (_transcript.IsShowing(tag.Digest)) ShowLineageFor(tag.Digest);
                break;

            case RailKind.Folder:
                ShowOnly(_setup);
                // Selecting a folder sets the folder field only. The other fields keep what the owner typed.
                if (tag.Detail is { } folder && Directory.Exists(folder)) _setup.ShowFolder(folder);
                break;
        }
    }

    /// <summary>Reads the end of each past transcript (64 KB at a time). Runs off the UI thread.</summary>
    private static Dictionary<string, TailFacts> ReadHistoryFacts(IEnumerable<SessionDigest> sessions)
    {
        var facts = new Dictionary<string, TailFacts>(StringComparer.Ordinal);
        foreach (var digest in sessions)
        {
            try
            {
                var tail = TranscriptTail.Read(digest.FilePath);
                if (tail is not null) facts.TryAdd(digest.SessionId, tail);
            }
            // Any failure is this transcript's alone (round 12 B1, the owner's segmented-load decision): the rest of the history loads.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write($"history tail not read: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return facts;
    }

    /// <summary>
    /// A text block for a rail row. A name that is too long for the rail is cut off with an ellipsis, so the rail never needs
    /// a sideways scrollbar; the full text is in the row's tooltip. The rail's width follows the splitter. A tree row's header is not held
    /// to the rail's width by the tree, so the label's width is bound to the rail: the Trust row ran past the edge without an ellipsis
    /// (round 5 item 15). A label that <paramref name="wrap"/>s wraps onto the next line instead.
    /// </summary>
    private TextBlock Label(string text, bool bold = false, bool muted = false, bool emoji = false, bool wrap = false)
    {
        var label = new TextBlock
        {
            Text = text,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = RailPalette.Brush(muted ? Palette.Muted : Palette.Text),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = new FontFamily(emoji ? "Segoe UI Emoji" : "Segoe UI"),
            FontSize = 13,
            Margin = new Thickness(0, 2, 0, 2),
        };
        if (Rail is not null)
            BindingOperations.SetBinding(label, FrameworkElement.MaxWidthProperty,
                new Binding("ActualWidth") { Source = Rail, Converter = RailRowWidth.Instance });
        return label;
    }

    /// <summary>
    /// A row in the style of the Claude app: the title on the first line with the time on the right, and the details below,
    /// muted. A long title or detail is cut off with an ellipsis, like every rail row.
    /// </summary>
    /// <summary>
    /// The start of a row's second line: the mark, then how long ago the session was last active, then (for a live session) its
    /// state. The mark is 🚀 for a running session, otherwise the cache's temperature. The time comes from the cache when it is
    /// known, otherwise from the fallback time. Then the context figure ("ctx 142k", with ⚠ at 80 % of the window) and the plan figure ("3/7"),
    /// each after a " · " when there is one.
    /// </summary>
    private string ActivityLine(string sessionId, string? state, DateTimeOffset? fallback, bool running)
    {
        var facts = _runningFacts.TryGetValue(sessionId, out var live) ? live
                  : _historyFacts.TryGetValue(sessionId, out var past) ? past
                  : null;
        var verdict = facts is null ? null : CacheRule.Classify(facts.LastRequest, DateTimeOffset.UtcNow, facts.Ttl, facts.ContextTokens);
        var known = verdict is { State: not CacheState.Unknown } ? verdict : null;

        var mark = CacheRule.Mark(known?.State ?? CacheState.Unknown, running);
        var ago = known is { } seen ? RelativeTime(DateTimeOffset.UtcNow - seen.Idle) : RelativeTime(fallback);
        var when = ago.Length == 0 ? string.Empty : ago + " ago";
        var head = string.Join(" ", new[] { mark, when }.Where(part => part.Length > 0));
        var line = string.IsNullOrEmpty(state) ? head : head + " · " + DaemonStatus.DisplayState(state);

        if (facts is { ContextTokens: > 0 } withTokens)
        {
            // The figure and its mark are one unit when the second line wraps: their spaces are no-break spaces (2026-11-18). The text is not changed.
            var ctx = (ContextMeter.Short(withTokens.ContextTokens)
                       + (ContextMeter.IsHigh(withTokens.ContextTokens, ContextMeter.EffectiveWindow(withTokens.ContextTokens, _contextWindow)) ? " ⚠" : string.Empty))
                      .Replace(' ', ' ');
            line = line.Length == 0 ? ctx : line + " · " + ctx;
        }
        if (PlanFigure(sessionId) is { } plan) line = line.Length == 0 ? plan : line + " · " + plan;
        return line;
    }

    /// <summary>
    /// The ⏩ at the start of a history row's second line. A click resumes that session through the resume prompt, which shows the
    /// choices and their cost before anything runs. The click is handled before the row sees it, so the row is not selected and
    /// the transcript stays as it is.
    /// </summary>
    /// <summary>The width of the slot at the start of a history row's second line, which holds the ⏩ when the row has one.</summary>
    private const double LeadSlotWidth = 22;

    private UIElement ResumeMark(SessionDigest digest)
    {
        var mark = new TextBlock
        {
            Text = "⏩",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Resume this session. The resume prompt shows the choices first.",
        };
        mark.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        mark.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = ResumeAsync(digest);
        };
        return mark;
    }

    /// <summary>
    /// The mark of a row that matched the search: a magnifier and the count of its matching messages, at the right of its title. Null
    /// when there is no match.
    /// </summary>
    private UIElement? MatchBadge(SessionMatch? match)
    {
        if (match is null) return null;
        return new TextBlock
        {
            Text = $"🔎 {match.MatchingMessages}",
            FontSize = 11,
            Foreground = RailPalette.Brush(Palette.IsLight ? "#1F5FA8" : "#9CDCFE"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Messages in this session that match the search",
        };
    }

    /// <summary>A row's tooltip: the newest matching message first, when the row matched the search.</summary>
    private static string SearchTip(SessionMatch? match, string tip) =>
        match?.Hits.LastOrDefault() is { } newest ? $"Newest match: {newest.Snippet}\n{tip}" : tip;

    /// <summary>
    /// The facts a history row's tooltip adds (session library, L2): the recap with its age, the custom title when there is one,
    /// and how often the context was compacted. A session with none of them adds nothing.
    /// </summary>
    private static string FactsTip(SessionDigest digest)
    {
        var lines = new List<string>();
        if (digest.LastRecap is { Length: > 0 } recap)
            lines.Add($"Recap, {AgeOf(digest.LastRecapAt)}: {(recap.Length <= 400 ? recap : recap[..400] + "…")}");
        if (digest.CustomTitle is { Length: > 0 } custom)
            lines.Add($"Custom title: {custom}");
        if (digest.Compactions > 0)
            lines.Add($"Compacted {digest.Compactions} time{(digest.Compactions == 1 ? string.Empty : "s")}");
        if (digest.FailedToolCalls > 0)
            lines.Add($"{digest.FailedToolCalls} tool call{(digest.FailedToolCalls == 1 ? string.Empty : "s")} failed");
        return lines.Count == 0 ? string.Empty : string.Join("\n", lines) + "\n";
    }

    /// <summary>A row of two lines. The optional trailing element sits at the right end of the second line; the badge at the right of the first.</summary>
    private UIElement TwoLine(string title, string details, string time, UIElement? trailing = null, UIElement? badge = null,
        bool leadSlot = false)
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = Label(title);
        titleText.Margin = new Thickness(0);
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(titleText, 0);

        var timeText = new TextBlock
        {
            Text = time,
            Foreground = RailPalette.Brush(Palette.Muted),
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(timeText, 1);
        top.Children.Add(titleText);
        top.Children.Add(timeText);
        if (badge is not null)
        {
            Grid.SetColumn(badge, 2);
            top.Children.Add(badge);
        }

        // The second line wraps rather than being cut: at the default rail width the figures at its end (ctx, plan, ⚠) were lost (2026-11-18).
        // The full text is also in the tooltip. A long line takes a second visual line; its width is still the rail's.
        var detailText = new TextBlock
        {
            Text = details,
            Foreground = RailPalette.Brush(Palette.Muted),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
        };

        UIElement detailRow = detailText;
        if (leadSlot)
        {
            // The mark sits in a slot of fixed width at the start of the line (owner, 2026-11-16). At the end of the line it
            // landed wherever the row's text ended, so the marks were scattered down the rail. A row with no mark keeps the
            // empty slot, so the second lines of every row start at the same place.
            var second = new Grid();
            second.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeadSlotWidth) });
            second.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (trailing is not null)
            {
                Grid.SetColumn(trailing, 0);
                second.Children.Add(trailing);
            }
            Grid.SetColumn(detailText, 1);
            second.Children.Add(detailText);
            detailRow = second;
        }
        else if (trailing is not null)
        {
            var second = new Grid();
            second.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            second.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(detailText, 0);
            Grid.SetColumn(trailing, 1);
            second.Children.Add(detailText);
            second.Children.Add(trailing);
            detailRow = second;
        }

        // A row is no wider than the rail, so a long title is trimmed and the trailing element stays in view.
        var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        BindingOperations.SetBinding(stack, FrameworkElement.MaxWidthProperty,
            new Binding("ActualWidth") { Source = Rail, Converter = RailRowWidth.Instance });
        stack.Children.Add(top);
        stack.Children.Add(detailRow);
        return stack;
    }

    /// <summary>A relative time for a row: "17m", "3h", "2d", or the date for older items. Empty when unknown.</summary>
    private static string RelativeTime(DateTimeOffset? when)
    {
        if (when is null) return string.Empty;
        var span = DateTimeOffset.UtcNow - when.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m"
             : span.TotalHours < 24 ? $"{(int)span.TotalHours}h"
             : span.TotalDays < 30 ? $"{(int)span.TotalDays}d"
             : when.Value.ToLocalTime().ToString("dd MMM", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The row of a live session: its title, its activity line and its marks. The same row is used under its folder and under
    /// Needs you, so both show the same session the same way.
    /// </summary>
    private TreeViewItem LiveRow(SessionRecord record, string state)
    {
        var cache = CacheTextFor(record.SessionId);
        // A session that shares a file with another of its run, or whose committed work conflicts, is marked (gems items 1 and 2).
        var flagged = IsShared(record.SessionId) || HasConflict(record.SessionId);
        var title = (flagged ? "⚠ " : string.Empty) + LiveTitle(record);
        var item = NewItem(
            ColourEdge(TwoLine(title, ActivityLine(record.SessionId, state, record.StartedAt, state == "working"), string.Empty,
                badge: MatchBadge(MatchOf(record.SessionId))), MarkOf(record.SessionId)?.Colour),
            new RailTag(RailKind.Session, record.SessionId),
            NoteTip(MarkOf(record.SessionId)) + PlanTip(MarkOf(record.SessionId)) + SearchTip(MatchOf(record.SessionId), cache.Full ?? record.SessionId));
        item.ContextMenu = RowMenu(record.SessionId, history: false, title);
        return item;
    }

    private static TreeViewItem NewItem(object header, RailTag tag, string? tooltip = null) =>
        new() { Header = header, Tag = tag, ToolTip = tooltip, Padding = new Thickness(2, 1, 2, 1) };

    private static IEnumerable<TreeViewItem> AllItems(ItemCollection items)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            yield return item;
            foreach (var child in AllItems(item.Items)) yield return child;
        }
    }

    private static TreeViewItem? FindItem(ItemCollection items, RailTag wanted) =>
        AllItems(items).FirstOrDefault(i => i.Tag is RailTag t && t.Kind == wanted.Kind && SameId(t.Key, wanted.Key));
}

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
/// </summary>
public enum RailFilter
{
    All,
    Running,
    History,
    Temporary,
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

    private void UpdateFilterMark() => FilterMark.Text = $"show: {_filter.ToString().ToLowerInvariant()} ▾";

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

    private static string FilterLabel(RailFilter filter) => filter switch
    {
        RailFilter.Running => "Running",
        RailFilter.History => "History",
        RailFilter.Temporary => "Temporary",
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
        foreach (var item in _filterMenu.Items.OfType<MenuItem>()) item.IsChecked = (RailFilter)item.Tag == _filter;
        _filterMenu.IsOpen = true;
    }

    private sealed class RailGroup(string key, string header, string tooltip)
    {
        public string Key { get; } = key;
        public string Header { get; } = header;
        public string Tooltip { get; } = tooltip;
        public List<(SessionRecord Record, string State)> Sessions { get; } = [];
    }

    private void InitRail()
    {
        Rail.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnRailExpansionChanged));
        Rail.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(OnRailExpansionChanged));
        _theme = ThemeStore.Load(_options.StatePath);
        InitFilterMenu();
        // A test flag picks the starting filter (all, running, history, temporary). Without it the rail starts on All.
        if (Enum.TryParse<RailFilter>(_options.FilterName, ignoreCase: true, out var start)) _filter = start;
        ApplyTheme();
    }

    /// <summary>Sets every colour the rail uses from the palette. The tree's own resources are set too, so its selection follows the theme.</summary>
    private void ApplyTheme()
    {
        var palette = Palette;
        RailPanel.Background = RailPalette.Brush(palette.Background);
        SessionsTitle.Foreground = RailPalette.Brush(palette.Text);
        ThemeMark.Text = _theme == RailTheme.Dark ? "☀ light" : "☾ dark";
        ThemeMark.Foreground = RailPalette.Brush(palette.Muted);
        FilterMark.Foreground = RailPalette.Brush(palette.Text);
        Resources["ScrollThumb"] = RailPalette.Brush("#55" + palette.Muted.TrimStart('#'));
        Resources["ScrollThumbHover"] = RailPalette.Brush("#99" + palette.Muted.TrimStart('#'));
        TabBar.Background = RailPalette.Brush(palette.Panel);
        RailToggle.Foreground = RailPalette.Brush(palette.Muted);
        SearchBox.Background = RailPalette.Brush(palette.Panel);
        SearchBox.Foreground = RailPalette.Brush(palette.Text);
        SearchBox.CaretBrush = RailPalette.Brush(palette.Text);
        SearchBox.BorderBrush = RailPalette.Brush(palette.Muted);
        SearchHint.Foreground = RailPalette.Brush(palette.Muted);
        UpdateRailToggle();
        Background = RailPalette.Brush(palette.Background);
        StatusBar.Background = RailPalette.Brush(palette.Panel);
        StatusText.Foreground = RailPalette.Brush(palette.Muted);
        RailSplitter.Background = RailPalette.Brush(palette.Border);
        Pane.Background = RailPalette.Brush(palette.Background);
        _setup?.ApplyTheme(palette);
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

    private void ThemeMark_Click(object sender, MouseButtonEventArgs e)
    {
        _theme = _theme == RailTheme.Dark ? RailTheme.Light : RailTheme.Dark;
        ThemeStore.Save(_options.StatePath, _theme);
        ApplyTheme();
        RefreshRail();
    }

    private void OnRailExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { Tag: RailTag { Kind: RailKind.Folder or RailKind.Heading or RailKind.HistorySection } tag } item)
            _expanded[tag.Key] = item.IsExpanded;
    }

    /// <summary>Rebuilds the rail from the state file, the last agent listing, the cache facts and the history read so far.</summary>
    private void RefreshRail()
    {
        var records = ReadRecords().Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        // A start that failed has no session, so it gets no terminal (it is listed as a failure below).
        foreach (var record in records.Where(r => !string.IsNullOrEmpty(r.SessionId))) EnsureTerminal(record);

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
        var specs = Specs;
        foreach (var spec in specs) Folder(spec.WorkingDirectory);

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
            }
        }

        _rebuilding = true;
        try
        {
            Rail.Items.Clear();
            foreach (var (key, step) in _trustSteps)
                Rail.Items.Add(NewItem(Label($"Trust: {Path.GetFileName(step.Root)} · choose 'Yes, I trust this folder'"),
                    new RailTag(RailKind.Trust, key), step.Root));

            var liveGroups = ShowsLive ? groups.Values.ToList() : new List<RailGroup>();
            foreach (var group in liveGroups)
            {
                var folderItem = NewItem(Label(group.Header, bold: true),
                    new RailTag(RailKind.Folder, group.Key, Detail: group.Tooltip), group.Tooltip);
                folderItem.IsExpanded = _expanded.GetValueOrDefault(group.Key, true);

                foreach (var (record, state) in group.Sessions.OrderBy(s => s.Record.Name, StringComparer.Ordinal))
                {
                    if (string.IsNullOrEmpty(record.SessionId))
                    {
                        // A start that failed: shown, with the reason, and not selectable as a terminal.
                        folderItem.Items.Add(NewItem(Label($"{record.Name} · {record.LastAlert}", muted: true),
                            new RailTag(RailKind.Heading, "failed:" + record.Name), record.LastAlert));
                        continue;
                    }
                    var cache = CacheTextFor(record.SessionId);
                    folderItem.Items.Add(NewItem(
                        TwoLine(record.Name, ActivityLine(record.SessionId, state, record.StartedAt, state == "working"), string.Empty,
                            badge: MatchBadge(MatchOf(record.SessionId))),
                        new RailTag(RailKind.Session, record.SessionId),
                        SearchTip(MatchOf(record.SessionId), cache.Full ?? record.SessionId)));
                }
                Rail.Items.Add(folderItem);
            }

            // The names of the live folders, so the History section uses the same name for the same folder.
            var liveNames = groups.Values.ToDictionary(g => g.Key, g => g.Header, StringComparer.OrdinalIgnoreCase);
            if (ShowsHistory) Rail.Items.Add(HistorySection(liveNames));

            RestoreSelection();
            SyncSessionTabs();
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
    private TreeViewItem HistorySection(IReadOnlyDictionary<string, string> liveNames)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Label("History", bold: true));
        var reload = Label(_historyLoading ? "⏳" : "🔄", muted: true, emoji: true);
        reload.Margin = new Thickness(8, 0, 0, 0);
        reload.Cursor = Cursors.Hand;
        reload.ToolTip = "Reload the history from the local transcripts (no model)";
        reload.MouseLeftButtonUp += async (_, _) => await LoadHistoryAsync();
        header.Children.Add(reload);

        var section = NewItem(header, new RailTag(RailKind.HistorySection, HistoryKey));
        section.IsExpanded = _expanded.GetValueOrDefault(HistoryKey, true);

        if (_history is null)
        {
            section.Items.Add(NewItem(Label(_historyLoading ? "reading the transcripts…" : "not read yet", muted: true),
                new RailTag(RailKind.Heading, HistoryKey + ":none")));
            return section;
        }

        var folders = _history
            .Where(IsShownInHistory)
            .GroupBy(d => d.ProjectFolder, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(d => d.LastActivity ?? DateTimeOffset.MinValue))
            .ToList();

        // Test and scratch folders under the Windows temp folder are shown only by the Temporary filter (owner, 2026-11-15).
        var tempPrefix = FolderKey.From(Path.GetTempPath());
        var shown = folders.Where(f => IsTemporary(f.Key, tempPrefix) == (_filter == RailFilter.Temporary)).ToList();

        // The sessions open now, listed by the agents or recorded. A history row for one of them gets no ⏩: it is already running.
        var openIds = _agents.Select(a => a.SessionId)
            .Concat(ReadRecords().Values.Select(r => r.SessionId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < shown.Count; i++)
            section.Items.Add(HistoryFolderItem(shown[i], liveNames, openIds, expandByDefault: i == 0));
        return section;
    }

    /// <summary>One folder of the History section, with its past sessions, newest first.</summary>
    private TreeViewItem HistoryFolderItem(
        IGrouping<string, SessionDigest> folder,
        IReadOnlyDictionary<string, string> liveNames,
        ISet<string> openIds,
        bool expandByDefault = false)
    {
        var key = HistoryKey + ":" + folder.Key;
        var name = liveNames.TryGetValue(folder.Key, out var live) ? live : folder.Key;
        var folderItem = NewItem(Label(name),
            new RailTag(RailKind.Heading, key),
            "Folder: " + folder.Key + "\n(the name Claude gave the transcripts, F44)");
        // OPENTAIL_BULLSHARK_EXPAND=1 expands the history folders, for screenshots only. Normally they start collapsed.
        // The top (most recent) folder starts expanded, so the sessions are visible at once. A collapse the owner makes is kept.
        folderItem.IsExpanded = _expanded.GetValueOrDefault(key,
            expandByDefault || Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_EXPAND") == "1");

        foreach (var digest in folder
                     .OrderByDescending(d => d.LastActivity ?? DateTimeOffset.MinValue)
                     .Take(MaxHistoryPerFolder))
        {
            var cache = CacheTextFor(digest.SessionId);
            // The ⏩ resumes a past session. It is not offered for a subagent, nor for a session that is already open.
            var resume = digest.IsSubagent || openIds.Contains(digest.SessionId) ? null : ResumeMark(digest);
            var match = MatchOf(digest.SessionId);
            folderItem.Items.Add(NewItem(
                TwoLine(digest.Title + (digest.IsSubagent ? " · subagent" : string.Empty),
                    ActivityLine(digest.SessionId, null, digest.LastActivity, running: false)
                        + $" · {digest.SessionId[..Math.Min(8, digest.SessionId.Length)]} · {digest.AssistantMessages} msgs",
                    string.Empty, resume, MatchBadge(match)),
                new RailTag(RailKind.History, digest.SessionId, digest),
                SearchTip(match, cache.Full is null ? digest.FilePath : cache.Full + "\n" + digest.FilePath)));
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
            var index = await Task.Run(() => TranscriptIndex.Build(TranscriptReader.DefaultProjectsRoot));
            var facts = await Task.Run(() => ReadHistoryFacts(index.Sessions));
            _history = index.Sessions.ToList();
            _historyFacts = facts;
            AppLog.Write($"history read: {index.Sessions.Count} transcripts, {index.Totals.CountedMessages} messages counted once");
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
            // or a transcript opened from one. Those keep the pane.
            if (_activeTab?.Kind is TabKind.Search or TabKind.Transcript) return;
            ShowOnly(_setup);
            return;
        }

        _selected = (RailTag)fallback.Tag;
        fallback.IsSelected = true;

        // A refresh must not take the pane away from a search, or from a transcript the owner opened from a result: those have no
        // row selected in the rail, so the fallback would show the setup over them.
        if (_activeTab?.Kind is TabKind.Search or TabKind.Transcript) return;
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
                await _transcript.ShowAsync(tag.Digest);
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
            catch (IOException ex)
            {
                AppLog.Write($"history tail not read: {ex.Message}");
            }
        }
        return facts;
    }

    /// <summary>
    /// A text block for a rail row. A name that is too long for the rail is cut off with an ellipsis, so the rail never needs
    /// a sideways scrollbar; the full text is in the row's tooltip. The rail's width follows the splitter.
    /// </summary>
    private TextBlock Label(string text, bool bold = false, bool muted = false, bool emoji = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = RailPalette.Brush(muted ? Palette.Muted : Palette.Text),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        FontFamily = new FontFamily(emoji ? "Segoe UI Emoji" : "Segoe UI"),
        FontSize = 13,
        Margin = new Thickness(0, 2, 0, 2),
    };

    /// <summary>
    /// A row in the style of the Claude app: the title on the first line with the time on the right, and the details below,
    /// muted. A long title or detail is cut off with an ellipsis, like every rail row.
    /// </summary>
    /// <summary>
    /// The start of a row's second line: the mark, then how long ago the session was last active, then (for a live session) its
    /// state. The mark is 🚀 for a running session, otherwise the cache's temperature. The time comes from the cache when it is
    /// known, otherwise from the fallback time.
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
        return string.IsNullOrEmpty(state) ? head : head + " · " + state;
    }

    /// <summary>
    /// The ⏩ at the end of a history row's second line. A click resumes that session through the resume prompt, which shows the
    /// choices and their cost before anything runs. The click is handled before the row sees it, so the row is not selected and
    /// the transcript stays as it is.
    /// </summary>
    private UIElement ResumeMark(SessionDigest digest)
    {
        var mark = new TextBlock
        {
            Text = "⏩",
            FontSize = 11,
            Margin = new Thickness(10, 0, 2, 0),
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
            Foreground = RailPalette.Brush("#9CDCFE"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Messages in this session that match the search",
        };
    }

    /// <summary>A row's tooltip: the newest matching message first, when the row matched the search.</summary>
    private static string SearchTip(SessionMatch? match, string tip) =>
        match?.Hits.LastOrDefault() is { } newest ? $"Newest match: {newest.Snippet}\n{tip}" : tip;

    /// <summary>A row of two lines. The optional trailing element sits at the right end of the second line; the badge at the right of the first.</summary>
    private UIElement TwoLine(string title, string details, string time, UIElement? trailing = null, UIElement? badge = null)
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

        var detailText = new TextBlock
        {
            Text = details,
            Foreground = RailPalette.Brush(Palette.Muted),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        };

        UIElement detailRow = detailText;
        if (trailing is not null)
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

    private static TreeViewItem NewItem(object header, RailTag tag, string? tooltip = null) =>
        new() { Header = header, Tag = tag, ToolTip = tooltip, Padding = new Thickness(2, 1, 2, 1) };

    private static string HistoryLabel(SessionDigest digest) =>
        $"{digest.LastActivity?.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {digest.AssistantMessages} msgs  ·  " +
        $"{digest.SessionId[..Math.Min(8, digest.SessionId.Length)]}" + (digest.IsSubagent ? "  ·  subagent" : string.Empty);

    private static IEnumerable<TreeViewItem> AllItems(ItemCollection items)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            yield return item;
            foreach (var child in AllItems(item.Items)) yield return child;
        }
    }

    private static TreeViewItem? FindItem(ItemCollection items, RailTag wanted) =>
        AllItems(items).FirstOrDefault(i => i.Tag is RailTag t && t.Kind == wanted.Kind && t.Key == wanted.Key);
}

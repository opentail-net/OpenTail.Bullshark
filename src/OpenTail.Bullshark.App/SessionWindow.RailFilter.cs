using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Search;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>The rail's filter, tag and grouping menus. Moved out of SessionWindow.Rail.cs without a change to the code (2026-11-23).</summary>
public partial class SessionWindow
{
    private void UpdateFilterMark()
    {
        // Under Codex the label shows Codex's own view and tag (parity P11, tags P5): the Claude view does not apply to the Codex rail. The grouping does (G-D4).
        var codex = IsCodexActive;
        var copilot = IsCopilotActive;
        var opencode = IsOpenCodeActive;
        var tag = codex ? ActiveCodexTagFilter : copilot ? ActiveCopilotTagFilter : opencode ? ActiveOpenCodeTagFilter : ActiveTagFilter;
        var view = (codex ? _codexView : copilot ? _copilotView : opencode ? _openCodeView : _filter).ToString().ToLowerInvariant();
        var grouping = _groupByTag ? " · by tag" : string.Empty;
        FilterMark.Text = $"show: {view}{(tag is null ? string.Empty : " · #" + tag)}{grouping} ▾";
        // The label may be cut with an ellipsis when the tag is long, so the tooltip names the tag in full.
        FilterBox.ToolTip = tag is null ? "Choose what the rail shows" : $"Choose what the rail shows (tag filter #{tag})";
    }

    private ContextMenu? _filterMenu;

    /// <summary>The view entries of the dropdown, built once. Claude's dropdown lists them all; Codex's lists the views Codex has (parity P11).</summary>
    private readonly List<MenuItem> _viewItems = [];

    /// <summary>The dropdown of views: all five are listed, with a tick on the one in use (Codex lists three of them, see <see cref="IsCodexView"/>). Styled in the rail's palette.</summary>
    private void InitFilterMenu()
    {
        _filterMenu = new ContextMenu { Placement = PlacementMode.Bottom, HasDropShadow = true, PlacementTarget = FilterBox };
        foreach (var option in Enum.GetValues<RailFilter>())
        {
            var item = new MenuItem { Header = FilterLabel(option), Tag = option, IsCheckable = true, Padding = new Thickness(10, 4, 16, 4) };
            item.Click += (_, _) => ChooseFilter(option);
            _viewItems.Add(item);
            _filterMenu.Items.Add(item);
        }
    }

    /// <summary>
    /// Rebuilds the dropdown each time it opens, so it lists the tags as the marks are now. Under Claude: the views, then a separator, the tags in
    /// use with their counts (a tick on the one in use), "Clear tag filter" when one is set, and the grouping. Under Codex: Codex's views (All,
    /// History and Archived, parity P11), a separator, Codex's tags (<see cref="AddCodexTagItems"/>, tags P5), then the same grouping.
    /// </summary>
    private void RebuildTagMenu()
    {
        if (_filterMenu is null) return;
        _filterMenu.Items.Clear();
        if (IsCodexActive)
        {
            foreach (var view in _viewItems.Where(item => item.Tag is RailFilter codexView && IsCodexView(codexView))) _filterMenu.Items.Add(view);
            _filterMenu.Items.Add(new Separator());
            AddCodexTagItems(_filterMenu);
            AddGroupingItems(_filterMenu);
            return;
        }
        if (IsCopilotActive)
        {
            foreach (var view in _viewItems.Where(item => item.Tag is RailFilter copilotView && IsCopilotView(copilotView))) _filterMenu.Items.Add(view);
            _filterMenu.Items.Add(new Separator());
            AddCopilotTagItems(_filterMenu);
            AddGroupingItems(_filterMenu);
            return;
        }
        // Under opencode the dropdown is opencode's own views and tags, as Copilot's are (OC2d).
        if (IsOpenCodeActive)
        {
            foreach (var view in _viewItems.Where(item => item.Tag is RailFilter openCodeView && IsOpenCodeView(openCodeView))) _filterMenu.Items.Add(view);
            _filterMenu.Items.Add(new Separator());
            AddOpenCodeTagItems(_filterMenu);
            AddGroupingItems(_filterMenu);
            return;
        }
        foreach (var view in _viewItems) _filterMenu.Items.Add(view);

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

        AddGroupingItems(_filterMenu);
    }

    /// <summary>
    /// The grouping entries of the dropdown (features batch G4): folders or tags, with a tick on the one in use. The same under Claude and under
    /// Codex (tags P5), since one setting groups both rails.
    /// </summary>
    private void AddGroupingItems(ContextMenu menu)
    {
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Group by", IsEnabled = false, Padding = new Thickness(10, 4, 16, 4) });
        foreach (var (label, byTag) in new[] { ("Folder", false), ("Tag", true) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _groupByTag == byTag, Padding = new Thickness(10, 4, 16, 4) };
            item.Click += (_, _) => ChooseGrouping(byTag);
            menu.Items.Add(item);
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
        // Under Codex the tag is Codex's own filter (tags P5). Claude's filter is not changed by it.
        if (IsCodexActive) _codexTagFilter = wanted;
        else if (IsCopilotActive) _copilotTagFilter = wanted;
        else if (IsOpenCodeActive) _openCodeTagFilter = wanted;
        else _tagFilter = wanted;
        UpdateFilterMark();
        SetStatus(wanted is null ? "Tag filter cleared."
            : IsCodexActive ? $"Showing the Codex sessions tagged #{wanted}."
            : IsCopilotActive ? $"Showing the Copilot sessions tagged #{wanted}."
            : IsOpenCodeActive ? $"Showing the opencode sessions tagged #{wanted}."
            : $"Showing the rows tagged #{wanted}. Needs you is never filtered.");
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

    /// <summary>The views the Codex rail has (parity P11): All, History and Archived. Running and Temporary are Claude's only.</summary>
    private static bool IsCodexView(RailFilter view) => view is RailFilter.All or RailFilter.History or RailFilter.Archived;

    private void ChooseFilter(RailFilter filter)
    {
        // Under Codex the view is Codex's own (A-D1, parity P11): Claude's filter is not changed by it.
        if (IsCodexActive)
        {
            ChooseCodexView(filter);
            return;
        }
        if (IsCopilotActive)
        {
            ChooseCopilotView(filter);
            return;
        }
        if (IsOpenCodeActive)
        {
            ChooseOpenCodeView(filter);
            return;
        }
        _filter = filter;
        UpdateFilterMark();
        RefreshRail();
    }

    /// <summary>Opens the dropdown under the filter box, with the view in use ticked.</summary>
    private void FilterMark_Click(object sender, MouseButtonEventArgs e)
    {
        if (_filterMenu is null) return;
        RebuildTagMenu();
        var current = IsCodexActive ? _codexView : IsCopilotActive ? _copilotView : IsOpenCodeActive ? _openCodeView : _filter;
        foreach (var item in _filterMenu.Items.OfType<MenuItem>())
            if (item.Tag is RailFilter view) item.IsChecked = view == current;
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
}

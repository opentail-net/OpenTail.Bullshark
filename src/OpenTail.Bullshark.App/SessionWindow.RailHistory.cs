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

/// <summary>The History section of the rail and its folder and session items. Moved out of SessionWindow.Rail.cs without a change to the code (2026-11-23).</summary>
public partial class SessionWindow
{
    /// <summary>
    /// The History section: one label at its top, with the 🔄 mark that reloads it, and past sessions grouped by folder below.
    /// The folder name is the live folder's name when the folder is one of this window's, and the name Claude gave it otherwise.
    /// </summary>
    private TreeViewItem HistorySection(IReadOnlyDictionary<string, FolderLabel> liveNames, IReadOnlyDictionary<string, SessionRecord> records)
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
                "No names, pins, tags or colours are shown, and nothing is written until the file can be read. " + $"The library could not be read ({_libraryError})."));
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
        IReadOnlyDictionary<string, FolderLabel> liveNames,
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
            // The name is the live folder's when this window has the folder. Otherwise it is the leaf of the working folder the transcripts name
            // (P9): Claude's own name, C--Git, is only the key. The group is still the key; the name is only the words on the row.
            var label = liveNames.TryGetValue(group.Key, out var live)
                ? live
                : FolderDisplay.Of(group.Key, group.Sessions.Select(digest => digest.Cwd));
            header = label.Name;
            // The tooltip gives the whole folder, or says that the transcripts do not name it. No internal ids (P8).
            tooltip = label.FullPath is null
                ? $"Folder: {group.Key}\nThe transcripts do not name the working folder."
                : $"Folder: {label.FullPath}";
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
    /// Reads the local transcripts (no model) and the end of each one, off the UI thread. It is the full read: it runs when the window opens,
    /// when the owner presses 🔄, after a Storage move and on return from Away. Nothing is written. The periodic refresh is a scoped read
    /// (SessionWindow.HistoryRefresh.cs). A full read asked for while that runs is made once it has finished.
    /// </summary>
    private async Task LoadHistoryAsync()
    {
        if (_historyLoading) return;
        if (_historyRefreshing)
        {
            _historyFullPending = true;
            return;
        }
        _historyLoading = true;
        RefreshRail();
        try
        {
            var refresh = await Task.Run(() => ActiveClaude.BuildIndexWithMemo(_options.IndexCachePath, AppLog.Write));
            var index = refresh.Index;
            var facts = await Task.Run(() => ReadHistoryFacts(index.Sessions));
            _history = index.Sessions.ToList();
            _historyFacts = facts;
            _historyMemo = refresh.Memo;
            _historyVersion++;
            NoteStartup("history read done");
            // The Storage page's names come from the working folders read here (P9), so its last listing is drawn again with them.
            RedrawStorageNames();
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
            AppLog.Write($"history read failed: {ex.GetType().Name}");
            SetStatus($"history not read: {ex.Message}");
        }
        finally
        {
            _historyReadAt = DateTimeOffset.UtcNow;
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
}

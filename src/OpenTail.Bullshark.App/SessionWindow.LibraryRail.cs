using System.Windows.Controls;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The rail rows that the owner's marks change (session library, L4): the Pinned group (D-L4), the archive (D-L5) and the history
/// row with its name, tags, note and colour (D-L6, D-L10, D-L11).
/// </summary>
public partial class SessionWindow
{
    /// <summary>The Pinned group is shown in All and in History. The folders show the rest.</summary>
    private bool PinnedGroupShown => _filter is RailFilter.All or RailFilter.History;

    /// <summary>
    /// True when a past session is listed in its folder. The Archived filter lists only archived sessions. Archived sessions are not
    /// listed in the folders otherwise, except by a search, where they are muted (D-L5). A pinned session is listed in the Pinned
    /// group instead of its folder, when that group is shown (D-L4).
    /// </summary>
    private bool InFolders(SessionDigest digest)
    {
        var mark = MarkOf(digest.SessionId);
        if (_filter == RailFilter.Archived) return mark?.Archived == true;
        if (mark?.Archived == true) return _searchMatches is not null;
        return !(mark?.Pinned == true && PinnedGroupShown);
    }

    /// <summary>True when a past session belongs in the Pinned group: pinned, not archived, and the group is shown.</summary>
    private bool InPinnedGroup(SessionDigest digest) =>
        PinnedGroupShown && MarkOf(digest.SessionId) is { Pinned: true, Archived: false };

    /// <summary>The Pinned group at the top of the History section, newest first, one row for each pinned session.</summary>
    private TreeViewItem PinnedGroup(ISet<string> openIds)
    {
        var key = HistoryKey + ":pinned";
        var group = NewItem(Label("Pinned", bold: true), new RailTag(RailKind.Heading, key),
            "Sessions you pinned. Each one is listed here only.");
        group.IsExpanded = _expanded.GetValueOrDefault(key, true);
        foreach (var digest in HistoryOrder.Sorted(_history!
                     .Where(IsShownInHistory)
                     .Where(InPinnedGroup)))
        {
            group.Items.Add(HistoryRow(digest, openIds));
        }
        return group;
    }

    /// <summary>Opens a past session from a lineage link: its row is selected, which shows its transcript.</summary>
    private void OpenRailSession(string sessionId)
    {
        var digest = _history?.FirstOrDefault(d => string.Equals(d.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (digest is null)
        {
            SetStatus($"Session {ShortId(sessionId)} is not in the history.");
            return;
        }
        _ = ShowRailItemAsync(new RailTag(RailKind.History, digest.SessionId, digest));
    }

    /// <summary>Shows the links of a past session's transcript: the session it continues, and the sessions that continue it (L5).</summary>
    private void ShowLineageFor(SessionDigest digest)
    {
        var from = digest.ContinuesSessionId is { Length: > 0 } earlier
            ? _history?.FirstOrDefault(d => string.Equals(d.SessionId, earlier, StringComparison.OrdinalIgnoreCase))
            : null;
        var later = (_history ?? new List<SessionDigest>())
            .Where(d => string.Equals(d.ContinuesSessionId, digest.SessionId, StringComparison.OrdinalIgnoreCase))
            .Select(d => (Id: d.SessionId, Title: DisplayTitle(d)))
            .ToList();
        _transcript.ShowLineage(digest.ContinuesSessionId, from is null ? null : DisplayTitle(from), later);
    }

    /// <summary>
    /// One past session's row: its title, the second line with its tags after "N msgs", the note and the facts in the tooltip, its
    /// colour bar, and its right-click menu. An archived row is muted, which only a search or the Archived filter shows.
    /// </summary>
    private TreeViewItem HistoryRow(SessionDigest digest, ISet<string> openIds)
    {
        var mark = MarkOf(digest.SessionId);
        var cache = CacheTextFor(digest.SessionId);
        // The ⏩ resumes a past session. It is not offered for a subagent, nor for a session that is already open.
        var resume = digest.IsSubagent || openIds.Contains(digest.SessionId) ? null : ResumeMark(digest);
        var match = MatchOf(digest.SessionId);
        // The same title as the tab and the transcript heading, including the lineage rule (DisplayTitle).
        var title = DisplayTitle(digest);
        var row = NewItem(
            ColourEdge(TwoLine(MagicPrefix(mark) + title + (digest.IsSubagent ? " · subagent" : string.Empty),
                    ActivityLine(digest.SessionId, null, digest.LastActivity, running: false)
                        + $" · {digest.AssistantMessages} msgs" + LimitSuffix(LimitTextOf(digest.SessionId)) + TagsSuffix(mark),
                    string.Empty, resume, MatchBadge(match), leadSlot: true),
                mark?.Colour),
            new RailTag(RailKind.History, digest.SessionId, digest),
            SearchTip(match, MagicTip(mark) + NoteTip(mark) + PlanTip(mark) + ContinuesTip(digest) + FactsTip(digest)
                + (cache.Full is null ? digest.FilePath : cache.Full + "\n" + digest.FilePath)));
        if (mark?.Archived == true) row.Opacity = 0.55;
        var menu = RowMenu(digest.SessionId, history: true, title);
        AddMagicItems(menu, digest, mark);
        row.ContextMenu = menu;
        return row;
    }
}

using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Where a Codex session is listed under a view (parity P11): in the Pinned group, in the folders or tag groups, in the Archived group, or nowhere.
/// </summary>
internal enum CodexPlace
{
    /// <summary>Not listed under this view.</summary>
    NotListed,

    /// <summary>The Pinned group at the top of the history (All and History).</summary>
    PinnedGroup,

    /// <summary>
    /// The folders, or the tag groups under Group by tag. Under All and History: the sessions that are neither archived nor pinned. Under Archived:
    /// the archived sessions, pinned or not, as Claude's Archived filter lists them in its folders.
    /// </summary>
    FolderGroups,

    /// <summary>The collapsed "Archived (n)" group at the end of the history (All only).</summary>
    ArchivedGroup,
}

/// <summary>
/// The Codex history's groups under Group by tag (tags P5, parity with the Claude rail's tag groups), and the views of the Codex rail (parity P11).
/// The pure part of the Codex History section: the window draws the groups, and this decides which sessions they hold, so the rules can be tested
/// without a window.
/// </summary>
internal static class CodexRailGroups
{
    /// <summary>
    /// Where a Codex session is listed under a view (parity P11). The Codex tag filter applies first, to every view (tags P5). Read from the session's
    /// Codex mark only. The rule is Claude's for the same view as far as the data allows (Rail.cs ShownHistoryFolders; LibraryRail.cs InFolders and
    /// InPinnedGroup): All lists every session, the archived ones in the Archived group; History lists the sessions that are not archived, the pinned
    /// ones in the Pinned group; Archived lists only the archived sessions, in the folders or tag groups.
    /// </summary>
    public static CodexPlace PlaceOf(RailFilter view, LibraryEntry? codexMark, string? tagFilter)
    {
        if (!TagIndex.Has(codexMark, tagFilter)) return CodexPlace.NotListed;
        var archived = codexMark is { Archived: true };
        var pinned = codexMark is { Pinned: true };
        return view switch
        {
            RailFilter.Archived => archived ? CodexPlace.FolderGroups : CodexPlace.NotListed,
            RailFilter.History => archived ? CodexPlace.NotListed : pinned ? CodexPlace.PinnedGroup : CodexPlace.FolderGroups,
            _ => archived ? CodexPlace.ArchivedGroup : pinned ? CodexPlace.PinnedGroup : CodexPlace.FolderGroups,
        };
    }

    /// <summary>
    /// True when a Codex session is listed in the folders or the tag groups of All: not archived (the Archived group lists it), not pinned (the Pinned group
    /// lists it), and holding the Codex tag filter when one is set (tags P5). Read from the session's Codex mark only.
    /// </summary>
    public static bool InHistoryGroups(LibraryEntry? codexMark, string? tagFilter) =>
        PlaceOf(RailFilter.All, codexMark, tagFilter) == CodexPlace.FolderGroups;

    /// <summary>
    /// The Codex tag groups under <paramref name="view"/>: the sessions that <see cref="PlaceOf"/> lists in the folders (the tag filter applies first),
    /// grouped by <see cref="TagGrouping"/> with the Codex marks. A session with several tags is in each of its groups, and the sessions with no tag
    /// form the last group.
    /// </summary>
    public static IReadOnlyList<TagGroup<SessionSummary>> ByTag(
        IEnumerable<SessionSummary> sessions,
        Func<string, LibraryEntry?> codexMarkOf,
        string? tagFilter,
        RailFilter view = RailFilter.All)
    {
        var listed = sessions.Where(session => PlaceOf(view, codexMarkOf(session.Key.SessionId), tagFilter) == CodexPlace.FolderGroups).ToList();
        return TagGrouping.Groups(listed, session => session.Key.SessionId, codexMarkOf, Sorted);
    }

    /// <summary>
    /// The order inside a group: the rule of <see cref="HistoryOrder.Sorted"/>, for a Codex summary. Main sessions first, subagents last, and the
    /// newest first inside each part.
    /// </summary>
    private static IEnumerable<SessionSummary> Sorted(IEnumerable<SessionSummary> sessions) =>
        sessions
            .OrderBy(session => session.IsSubagent == true)
            .ThenByDescending(session => session.LastActivity ?? DateTimeOffset.MinValue);
}

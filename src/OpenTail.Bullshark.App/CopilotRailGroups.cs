using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Where a Copilot session is listed under a view (parity P11): in the Pinned group, in the folders or tag groups, in the Archived group, or nowhere.
/// </summary>
internal enum CopilotPlace
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
/// The Copilot history's groups under Group by tag (tags P5, parity with the Claude rail's tag groups), and the views of the Copilot rail (parity P11).
/// The pure part of the Copilot History section: the window draws the groups, and this decides which sessions they hold, so the rules can be tested
/// without a window.
/// </summary>
internal static class CopilotRailGroups
{
    /// <summary>
    /// Where a Copilot session is listed under a view (parity P11). The Copilot tag filter applies first, to every view (tags P5). Read from the session's
    /// Copilot mark only. The rule is Claude's for the same view as far as the data allows (Rail.cs ShownHistoryFolders; LibraryRail.cs InFolders and
    /// InPinnedGroup): All lists every session, the archived ones in the Archived group; History lists the sessions that are not archived, the pinned
    /// ones in the Pinned group; Archived lists only the archived sessions, in the folders or tag groups.
    /// </summary>
    public static CopilotPlace PlaceOf(RailFilter view, LibraryEntry? copilotMark, string? tagFilter)
    {
        if (!TagIndex.Has(copilotMark, tagFilter)) return CopilotPlace.NotListed;
        var archived = copilotMark is { Archived: true };
        var pinned = copilotMark is { Pinned: true };
        return view switch
        {
            RailFilter.Archived => archived ? CopilotPlace.FolderGroups : CopilotPlace.NotListed,
            RailFilter.History => archived ? CopilotPlace.NotListed : pinned ? CopilotPlace.PinnedGroup : CopilotPlace.FolderGroups,
            _ => archived ? CopilotPlace.ArchivedGroup : pinned ? CopilotPlace.PinnedGroup : CopilotPlace.FolderGroups,
        };
    }

    /// <summary>
    /// True when a Copilot session is listed in the folders or the tag groups of All: not archived (the Archived group lists it), not pinned (the Pinned group
    /// lists it), and holding the Copilot tag filter when one is set (tags P5). Read from the session's Copilot mark only.
    /// </summary>
    public static bool InHistoryGroups(LibraryEntry? copilotMark, string? tagFilter) =>
        PlaceOf(RailFilter.All, copilotMark, tagFilter) == CopilotPlace.FolderGroups;

    /// <summary>
    /// The Copilot tag groups under <paramref name="view"/>: the sessions that <see cref="PlaceOf"/> lists in the folders (the tag filter applies first),
    /// grouped by <see cref="TagGrouping"/> with the Copilot marks. A session with several tags is in each of its groups, and the sessions with no tag
    /// form the last group.
    /// </summary>
    public static IReadOnlyList<TagGroup<SessionSummary>> ByTag(
        IEnumerable<SessionSummary> sessions,
        Func<string, LibraryEntry?> copilotMarkOf,
        string? tagFilter,
        RailFilter view = RailFilter.All)
    {
        var listed = sessions.Where(session => PlaceOf(view, copilotMarkOf(session.Key.SessionId), tagFilter) == CopilotPlace.FolderGroups).ToList();
        return TagGrouping.Groups(listed, session => session.Key.SessionId, copilotMarkOf, Sorted);
    }

    /// <summary>
    /// The order inside a group: the rule of <see cref="HistoryOrder.Sorted"/>, for a Copilot summary. Main sessions first, subagents last, and the
    /// newest first inside each part.
    /// </summary>
    internal static IEnumerable<SessionSummary> Sorted(IEnumerable<SessionSummary> sessions) =>
        sessions
            .OrderBy(session => session.IsSubagent == true)
            .ThenByDescending(session => session.LastActivity ?? DateTimeOffset.MinValue);
}

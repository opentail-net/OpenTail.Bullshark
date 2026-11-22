using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Where a session is listed under a view (parity P11): in the Pinned group, in the folders or tag groups, in the Archived group, or nowhere.
/// </summary>
internal enum ProviderPlace
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
/// The history's groups under Group by tag (tags P5, parity with the Claude rail's tag groups), and the views of a provider's rail (parity P11).
/// The pure part of a provider's History section: the window draws the groups, and this decides which sessions they hold, so the rules can be tested
/// without a window. Codex and Copilot share it; each passes its own marks.
/// </summary>
internal static class ProviderRailGroups
{
    /// <summary>
    /// Where a session is listed under a view (parity P11). The tag filter applies first, to every view (tags P5). Read from the session's
    /// mark only. The rule is Claude's for the same view as far as the data allows (Rail.cs ShownHistoryFolders; LibraryRail.cs InFolders and
    /// InPinnedGroup): All lists every session, the archived ones in the Archived group; History lists the sessions that are not archived, the pinned
    /// ones in the Pinned group; Archived lists only the archived sessions, in the folders or tag groups.
    /// </summary>
    public static ProviderPlace PlaceOf(RailFilter view, LibraryEntry? mark, string? tagFilter)
    {
        if (!TagIndex.Has(mark, tagFilter)) return ProviderPlace.NotListed;
        var archived = mark is { Archived: true };
        var pinned = mark is { Pinned: true };
        return view switch
        {
            RailFilter.Archived => archived ? ProviderPlace.FolderGroups : ProviderPlace.NotListed,
            RailFilter.History => archived ? ProviderPlace.NotListed : pinned ? ProviderPlace.PinnedGroup : ProviderPlace.FolderGroups,
            _ => archived ? ProviderPlace.ArchivedGroup : pinned ? ProviderPlace.PinnedGroup : ProviderPlace.FolderGroups,
        };
    }

    /// <summary>
    /// True when a session is listed in the folders or the tag groups of All: not archived (the Archived group lists it), not pinned (the Pinned group
    /// lists it), and holding the tag filter when one is set (tags P5). Read from the session's mark only.
    /// </summary>
    public static bool InHistoryGroups(LibraryEntry? mark, string? tagFilter) =>
        PlaceOf(RailFilter.All, mark, tagFilter) == ProviderPlace.FolderGroups;

    /// <summary>
    /// The tag groups under <paramref name="view"/>: the sessions that <see cref="PlaceOf"/> lists in the folders (the tag filter applies first),
    /// grouped by <see cref="TagGrouping"/> with the marks. A session with several tags is in each of its groups, and the sessions with no tag
    /// form the last group.
    /// </summary>
    public static IReadOnlyList<TagGroup<SessionSummary>> ByTag(
        IEnumerable<SessionSummary> sessions,
        Func<string, LibraryEntry?> markOf,
        string? tagFilter,
        RailFilter view = RailFilter.All)
    {
        var listed = sessions.Where(session => PlaceOf(view, markOf(session.Key.SessionId), tagFilter) == ProviderPlace.FolderGroups).ToList();
        return TagGrouping.Groups(listed, session => session.Key.SessionId, markOf, Sorted);
    }

    /// <summary>
    /// The order inside a group: the rule of <see cref="HistoryOrder.Sorted"/>, for a session summary. Main sessions first, subagents last, and the
    /// newest first inside each part.
    /// </summary>
    internal static IEnumerable<SessionSummary> Sorted(IEnumerable<SessionSummary> sessions) =>
        sessions
            .OrderBy(session => session.IsSubagent == true)
            .ThenByDescending(session => session.LastActivity ?? DateTimeOffset.MinValue);
}

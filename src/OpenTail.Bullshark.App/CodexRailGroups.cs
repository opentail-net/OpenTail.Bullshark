using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Codex history's groups under Group by tag (tags P5, parity with the Claude rail's tag groups). The pure part of the Codex History section:
/// the window draws the groups, and this decides which sessions they hold, so the rules can be tested without a window.
/// </summary>
internal static class CodexRailGroups
{
    /// <summary>
    /// True when a Codex session is listed in the folders or the tag groups: not archived (the Archived group lists it), not pinned (the Pinned group
    /// lists it), and holding the Codex tag filter when one is set (tags P5). Read from the session's Codex mark only.
    /// </summary>
    public static bool InHistoryGroups(LibraryEntry? codexMark, string? tagFilter) =>
        codexMark is not { Archived: true } and not { Pinned: true } && TagIndex.Has(codexMark, tagFilter);

    /// <summary>
    /// The Codex tag groups: the sessions <see cref="InHistoryGroups"/> keeps (the tag filter applies first), grouped by <see cref="TagGrouping"/>
    /// with the Codex marks. A session with several tags is in each of its groups, and the sessions with no tag form the last group.
    /// </summary>
    public static IReadOnlyList<TagGroup<SessionSummary>> ByTag(
        IEnumerable<SessionSummary> sessions,
        Func<string, LibraryEntry?> codexMarkOf,
        string? tagFilter)
    {
        var listed = sessions.Where(session => InHistoryGroups(codexMarkOf(session.Key.SessionId), tagFilter)).ToList();
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

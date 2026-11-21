using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// One group of the rail's History section when it is grouped by tag (features batch G4): the tag in lower case, or null for the sessions with
/// no tag, and the group's sessions in the order the grouping was given (for the Claude rail, newest first, main sessions before subagents, as
/// <see cref="HistoryOrder"/> orders them). Generic so the Codex rail can group its own session summaries (tags P5).
/// </summary>
public sealed record TagGroup<T>(string? Tag, IReadOnlyList<T> Sessions);

/// <summary>
/// The past sessions grouped by their tags (features batch G4). Pure: the marks are looked up by session id. A session with several tags is in
/// each of its groups, so it appears more than once in the rail. The tags are compared as <see cref="TagIndex"/> compares them (trimmed, without
/// case). Groups: each tag in use, the most sessions first and then by name, then "no tag" last.
/// </summary>
public static class TagGrouping
{
    /// <summary>The groups of the Claude history sessions, ordered as <see cref="HistoryOrder"/> orders them. Empty when there are no sessions.</summary>
    public static IReadOnlyList<TagGroup<SessionDigest>> Groups(IEnumerable<SessionDigest> sessions, Func<string, LibraryEntry?> marksOf) =>
        Groups(sessions, digest => digest.SessionId, marksOf, HistoryOrder.Sorted);

    /// <summary>
    /// The groups of any sessions. <paramref name="idOf"/> gives the id that keys a session's mark, and <paramref name="order"/> orders the sessions
    /// before they are grouped, so each group keeps that order. Empty when there are no sessions. A group is listed only when it has a session.
    /// </summary>
    public static IReadOnlyList<TagGroup<T>> Groups<T>(
        IEnumerable<T> sessions,
        Func<T, string> idOf,
        Func<string, LibraryEntry?> marksOf,
        Func<IEnumerable<T>, IEnumerable<T>> order)
    {
        var byTag = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var untagged = new List<T>();
        foreach (var session in order(sessions))
        {
            var tags = TagsOf(marksOf(idOf(session)));
            if (tags.Count == 0)
            {
                untagged.Add(session);
                continue;
            }
            foreach (var tag in tags)
            {
                if (!byTag.TryGetValue(tag, out var list)) byTag[tag] = list = [];
                list.Add(session);
            }
        }

        var groups = byTag
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TagGroup<T>(pair.Key, pair.Value))
            .ToList();
        if (untagged.Count > 0) groups.Add(new TagGroup<T>(null, untagged));
        return groups;
    }

    /// <summary>The tags a mark holds, trimmed and in lower case, each once. A null mark, or one with no usable tag, holds none.</summary>
    public static IReadOnlyList<string> TagsOf(LibraryEntry? mark)
    {
        if (mark?.Tags is null) return [];
        var tags = new List<string>();
        foreach (var raw in mark.Tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tag = raw.Trim().ToLowerInvariant();
            if (!tags.Contains(tag, StringComparer.Ordinal)) tags.Add(tag);
        }
        return tags;
    }
}

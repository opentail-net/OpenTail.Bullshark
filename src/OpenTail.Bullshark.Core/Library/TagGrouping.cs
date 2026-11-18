using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// One group of the rail's History section when it is grouped by tag (features batch G4): the tag in lower case, or null for the sessions with
/// no tag, and the group's sessions, newest first (main sessions before subagents, as <see cref="HistoryOrder"/> orders them).
/// </summary>
public sealed record TagGroup(string? Tag, IReadOnlyList<SessionDigest> Sessions);

/// <summary>
/// The past sessions grouped by their tags (features batch G4). Pure: the marks are looked up by session id. A session with several tags is in
/// each of its groups, so it appears more than once in the rail. The tags are compared as <see cref="TagIndex"/> compares them (trimmed, without
/// case). Groups: each tag in use, the most sessions first and then by name, then "no tag" last.
/// </summary>
public static class TagGrouping
{
    /// <summary>The groups of the sessions. Empty when there are no sessions. A group is listed only when it has a session.</summary>
    public static IReadOnlyList<TagGroup> Groups(IEnumerable<SessionDigest> sessions, Func<string, LibraryEntry?> marksOf)
    {
        var byTag = new Dictionary<string, List<SessionDigest>>(StringComparer.Ordinal);
        var untagged = new List<SessionDigest>();
        foreach (var digest in HistoryOrder.Sorted(sessions))
        {
            var tags = TagsOf(marksOf(digest.SessionId));
            if (tags.Count == 0)
            {
                untagged.Add(digest);
                continue;
            }
            foreach (var tag in tags)
            {
                if (!byTag.TryGetValue(tag, out var list)) byTag[tag] = list = [];
                list.Add(digest);
            }
        }

        var groups = byTag
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TagGroup(pair.Key, pair.Value))
            .ToList();
        if (untagged.Count > 0) groups.Add(new TagGroup(null, untagged));
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

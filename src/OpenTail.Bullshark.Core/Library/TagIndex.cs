namespace OpenTail.Bullshark.Core.Library;

/// <summary>A tag in use, and how many sessions hold it.</summary>
public sealed record TagCount(string Tag, int Count);

/// <summary>
/// The tags the owner has put on past sessions (session library, D-L6), counted for the tag filter and the palette (tags plan, Part A).
/// Pure: the marks are passed in. A tag is compared without case, and a session counts once for each tag it holds.
/// </summary>
public static class TagIndex
{
    /// <summary>The tags in use, most used first, then by name. Each tag is in lower case.</summary>
    public static IReadOnlyList<TagCount> Counts(IEnumerable<LibraryEntry> entries)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Tags is null) continue;
            var held = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in entry.Tags)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var tag = raw.Trim().ToLowerInvariant();
                if (held.Add(tag)) counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }
        return counts
            .Select(pair => new TagCount(pair.Key, pair.Value))
            .OrderByDescending(tag => tag.Count)
            .ThenBy(tag => tag.Tag, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// True when the mark holds the tag, compared without case. A null or blank tag is no filter, so every mark passes it, a session with
    /// no mark included. A null mark holds no tag.
    /// </summary>
    public static bool Has(LibraryEntry? mark, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return true;
        var wanted = tag.Trim();
        return mark?.Tags is { } tags && tags.Any(held => string.Equals(held?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }
}

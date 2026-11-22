namespace OpenTail.Bullshark.App;

/// <summary>
/// The search of past Copilot sessions (parity P6, first step, decisions S6-D1). A session matches a query when every word of the query appears, without
/// case, in one of its fields: its title (the index name or the first message), the owner's name for it, one of its tags (with or without the leading
/// '#'), or its note. The words may be in different fields. Message text is not searched: that is the second step, which waits for owner decision O3.
/// Pure: the window passes the fields in, so the rule is tested without a window (CopilotSearchTests).
/// </summary>
internal static class CopilotSearch
{
    /// <summary>
    /// True when every word of <paramref name="query"/> is in one of the fields. The query is split at white space, and a query with no word (null, empty or
    /// blank) matches nothing. A tag is compared as the library keeps it (trimmed, in lower case, see TagIndex): the leading '#' is taken off, and both
    /// the bare tag and the tag with its '#' are searched, so "auth" and "#auth" each find a session tagged "auth" or "#auth".
    /// </summary>
    public static bool Matches(string? query, string? title, string? name, IEnumerable<string?>? tags, string? note)
    {
        string[] words = query?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (words.Length == 0) return false;

        var fields = new List<string>(4);
        AddField(fields, title);
        AddField(fields, name);
        AddField(fields, note);
        foreach (var tag in tags ?? Array.Empty<string?>())
        {
            var bare = tag?.Trim().ToLowerInvariant().TrimStart('#') ?? string.Empty;
            if (bare.Length == 0) continue;
            fields.Add(bare);
            fields.Add("#" + bare);
        }
        return words.All(word => fields.Any(field => field.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AddField(List<string> fields, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) fields.Add(text);
    }
}

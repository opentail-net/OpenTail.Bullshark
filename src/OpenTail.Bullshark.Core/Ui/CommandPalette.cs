namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// One line of the command palette (ideas D3, docs/plans/palette-2026-11-18.md). <paramref name="Id"/> names the entry for the caller and is not shown.
/// <paramref name="Hint"/> is a short note drawn at the right, or null. <paramref name="Order"/> breaks ties: a lower number comes first. The caller
/// numbers its groups: waiting sessions, other sessions, pages, actions.
/// </summary>
public sealed record PaletteEntry(string Id, string Title, string? Hint, int Order);

/// <summary>
/// The matching of the command palette (D3). Pure: no window, no clock, no file. The App builds the entries from what is in memory and
/// shows what this returns.
/// </summary>
/// <remarks>
/// The rules, one place for them:
/// <list type="bullet">
/// <item>Case is ignored. The query is split on white space; every word must match, or the entry is left out.</item>
/// <item>A word matches with score 3 when the title starts with it, 2 when a word of the title starts with it, and 1 when its letters
/// appear in the title in order (a subsequence). An entry's score is the lowest of its words' scores.</item>
/// <item>Equal scores keep the entries' <see cref="PaletteEntry.Order"/>, then the order they were given in.</item>
/// <item>A query that is empty, or only white space, lists the entries in <see cref="PaletteEntry.Order"/> with no matching.</item>
/// <item>Accents are not folded: "cafe" does not match "café". Nothing here throws on any text.</item>
/// </list>
/// </remarks>
public static class CommandPalette
{
    /// <summary>How many rows the palette lists at most (D4).</summary>
    public const int DefaultMax = 12;

    private const int PrefixScore = 3;
    private const int WordStartScore = 2;
    private const int SubsequenceScore = 1;

    /// <summary>The entries the query matches, best first, at most <paramref name="max"/> of them.</summary>
    public static IReadOnlyList<PaletteEntry> Filter(IReadOnlyList<PaletteEntry> entries, string? query, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (max <= 0) return [];

        var indexed = entries.Select((entry, index) => (Entry: entry, Index: index));
        var words = WordsOf(query);
        if (words.Count == 0)
        {
            return indexed
                .OrderBy(item => item.Entry.Order)
                .ThenBy(item => item.Index)
                .Take(max)
                .Select(item => item.Entry)
                .ToList();
        }

        return indexed
            .Select(item => (item.Entry, item.Index, Score: ScoreOf(item.Entry.Title, words)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Order)
            .ThenBy(item => item.Index)
            .Take(max)
            .Select(item => item.Entry)
            .ToList();
    }

    /// <summary>The query's words in lower case. Empty when the query is null, empty or only white space.</summary>
    private static List<string> WordsOf(string? query) =>
        (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToLowerInvariant())
            .ToList();

    /// <summary>The lowest word score of the title, or 0 when one word does not match at all.</summary>
    private static int ScoreOf(string title, IReadOnlyList<string> words)
    {
        var text = (title ?? string.Empty).ToLowerInvariant();
        var starts = WordStarts(text);
        var lowest = int.MaxValue;
        foreach (var word in words)
        {
            var score = WordScore(text, starts, word);
            if (score == 0) return 0;
            lowest = Math.Min(lowest, score);
        }
        return lowest;
    }

    private static int WordScore(string text, IReadOnlyList<int> starts, string word)
    {
        if (text.StartsWith(word, StringComparison.Ordinal)) return PrefixScore;
        foreach (var start in starts)
        {
            if (text.AsSpan(start).StartsWith(word, StringComparison.Ordinal)) return WordStartScore;
        }
        return IsSubsequence(word, text) ? SubsequenceScore : 0;
    }

    /// <summary>The positions where a word of the title begins: a letter or digit after a character that is neither.</summary>
    private static List<int> WordStarts(string text)
    {
        var starts = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLetterOrDigit(text[i])) continue;
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) starts.Add(i);
        }
        return starts;
    }

    private static bool IsSubsequence(string word, string text)
    {
        var at = 0;
        foreach (var c in word)
        {
            at = text.IndexOf(c, at);
            if (at < 0) return false;
            at++;
        }
        return true;
    }
}

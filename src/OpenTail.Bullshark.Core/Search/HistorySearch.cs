using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Search;

/// <summary>One message that matched: its line number as the transcript view shows it, who wrote it, when, and a piece of its text.</summary>
public sealed record LineHit(int LineNumber, string Kind, DateTimeOffset? Time, string Snippet);

/// <summary>One session that matched: how many of its messages matched any word, and the newest of those messages.</summary>
public sealed record SessionMatch(SessionDigest Session, int MatchingMessages, IReadOnlyList<LineHit> Hits);

/// <summary>
/// Search over what was said in past sessions (owner, 2026-11-16), the way ChatGPT and Claude.ai search their chats. The owner's
/// messages and Claude's text replies are searched; tool calls and results are not. A session matches when every word appears
/// somewhere in it, on any line. Results are newest first and capped. The transcripts are read each time: there is no index yet
/// (docs/plans/history-search.md).
/// </summary>
public static class HistorySearch
{
    /// <summary>The fewest letters or digits a word may have. Shorter words match nearly every message.</summary>
    public const int MinWordLength = 3;

    /// <summary>The most sessions one search returns.</summary>
    public const int MaxResults = 100;

    /// <summary>The newest matching messages kept for each session.</summary>
    public const int HitsPerSession = 3;

    /// <summary>The characters shown on each side of the matched word in a snippet.</summary>
    private const int SnippetContext = 60;

    /// <summary>The words of a query, in the order typed. Empty when the query is blank.</summary>
    public static IReadOnlyList<string> WordsOf(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// True when the query can be searched: at least one word, and every word has <see cref="MinWordLength"/> letters or digits.
    /// Shorter words are refused, because they match almost every message.
    /// </summary>
    public static bool IsUsable(string query)
    {
        var words = WordsOf(query);
        return words.Count > 0 && words.All(word => word.Count(char.IsLetterOrDigit) >= MinWordLength);
    }

    /// <summary>
    /// Finds the sessions whose messages contain every word. Sessions are read newest first, and reading stops once
    /// <see cref="MaxResults"/> are found. A session whose transcript cannot be read is left out. Throws
    /// OperationCanceledException when the token is cancelled.
    /// <paramref name="extraText"/> gives a session's own text that is searched as well, such as the owner's name, tags and note
    /// (D-L7). A word found there counts as found, so a session can match with no matching message; its
    /// <see cref="SessionMatch.MatchingMessages"/> is then 0.
    /// </summary>
    public static IReadOnlyList<SessionMatch> Run(
        IEnumerable<SessionDigest> sessions,
        string query,
        bool includeSubagents = false,
        CancellationToken cancellationToken = default,
        Func<SessionDigest, string?>? extraText = null)
    {
        var words = WordsOf(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new List<SessionMatch>();
        if (words.Count == 0) return results;

        var newestFirst = sessions
            .Where(digest => includeSubagents || !digest.IsSubagent)
            .OrderByDescending(digest => digest.LastActivity ?? DateTimeOffset.MinValue);

        foreach (var digest in newestFirst)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = MatchSession(digest, words, extraText?.Invoke(digest), cancellationToken);
            if (match is not null) results.Add(match);
            if (results.Count >= MaxResults) break;
        }
        return results;
    }

    /// <summary>A piece of a message around its first match, on one line, with "…" where it was cut.</summary>
    public static string Snippet(string text, string word)
    {
        var flat = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        var at = flat.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return flat.Length <= 2 * SnippetContext ? flat : flat[..(2 * SnippetContext)] + "…";

        var start = Math.Max(0, at - SnippetContext);
        var end = Math.Min(flat.Length, at + word.Length + SnippetContext);
        return (start > 0 ? "…" : string.Empty) + flat[start..end].Trim() + (end < flat.Length ? "…" : string.Empty);
    }

    private static SessionMatch? MatchSession(
        SessionDigest digest,
        IReadOnlyList<string> words,
        string? extra,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(extra))
        {
            foreach (var word in words)
            {
                if (extra.Contains(word, StringComparison.OrdinalIgnoreCase)) found.Add(word);
            }
        }
        var hits = new List<LineHit>();
        var matchingMessages = 0;
        var number = 0;

        try
        {
            foreach (var raw in File.ReadLines(digest.FilePath))
            {
                number++;
                cancellationToken.ThrowIfCancellationRequested();

                // Cheap first: most lines contain no word at all, so they are never parsed.
                if (!words.Any(word => raw.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;

                var shown = TranscriptLine.From(number, raw);
                if (shown is null || shown.Kind is not ("you" or "claude")) continue;

                var inThisMessage = words.Where(word => shown.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inThisMessage.Count == 0) continue;

                matchingMessages++;
                foreach (var word in inThisMessage) found.Add(word);
                if (hits.Count == HitsPerSession) hits.RemoveAt(0);
                hits.Add(new LineHit(number, shown.Kind, shown.Time, Snippet(shown.Text, inThisMessage[0])));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return found.Count == words.Count ? new SessionMatch(digest, matchingMessages, hits) : null;
    }
}

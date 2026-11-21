using System.Runtime.ExceptionServices;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Search;

/// <summary>One message that matched: its line number as the transcript view shows it, who wrote it, when, and a piece of its text.</summary>
public sealed record LineHit(int LineNumber, string Kind, DateTimeOffset? Time, string Snippet);

/// <summary>One session that matched: how many of its messages matched any word, and the newest of those messages.</summary>
public sealed record SessionMatch(SessionDigest Session, int MatchingMessages, IReadOnlyList<LineHit> Hits);

/// <summary>The sessions that matched, and how many sessions were left out because their transcript could not be read.</summary>
public sealed record SearchOutcome(IReadOnlyList<SessionMatch> Matches, int Unreadable);

/// <summary>
/// Search over what was said in past sessions (owner, 2026-11-16), the way ChatGPT and Claude.ai search their chats. The owner's
/// messages and Claude's text replies are searched; tool calls and results are not. A session matches when every word appears
/// somewhere in it, on any line. Results are newest first and capped. The transcripts are read each time: there is no index yet
/// (docs/plans/Done/DONE_history-search.md).
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
        Func<SessionDigest, string?>? extraText = null) =>
        RunCounted(sessions, query, includeSubagents, cancellationToken, extraText).Matches;

    /// <summary>
    /// The search, with the count of sessions left out because their transcript could not be read, or because they failed in any other
    /// way. Such a session is left out and counted; it does not stop the search. Only cancellation does.
    /// </summary>
    public static SearchOutcome RunCounted(
        IEnumerable<SessionDigest> sessions,
        string query,
        bool includeSubagents = false,
        CancellationToken cancellationToken = default,
        Func<SessionDigest, string?>? extraText = null)
    {
        var words = WordsOf(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var probes = words.Select(RawProbe).ToList();
        var results = new List<SessionMatch>();
        var unreadable = 0;
        if (words.Count == 0) return new SearchOutcome(results, unreadable);

        var newestFirst = sessions
            .Where(digest => includeSubagents || !digest.IsSubagent)
            .OrderByDescending(digest => digest.LastActivity ?? DateTimeOffset.MinValue);

        // The sessions are read a batch at a time, on several threads (round 10 item 2). Their results are taken in the newest-first order, one
        // by one, so the matches, the count of unreadable sessions and the stop at MaxResults are the ones reading them one by one gives.
        var ordered = newestFirst.ToList();
        var start = 0;
        while (start < ordered.Count)
        {
            // A batch is no larger than the matches still needed, so near the cap a batch holds fewer sessions that can be read past the stop
            // (round 11 item 4). The results do not change: the stop is taken where it was.
            var size = Math.Min(BatchSize, MaxResults - results.Count);
            var batch = ordered.GetRange(start, Math.Min(size, ordered.Count - start));
            start += batch.Count;
            var read = ReadBatch(batch, words, probes, extraText, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < batch.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (match, error) = read[i];
                if (error is OperationCanceledException) ExceptionDispatchInfo.Capture(error).Throw();
                if (error is not null) unreadable++;
                else if (match is not null) results.Add(match);
                if (results.Count >= MaxResults) return new SearchOutcome(results, unreadable);
            }
        }
        return new SearchOutcome(results, unreadable);
    }

    /// <summary>The sessions read together by one batch of a search.</summary>
    private const int BatchSize = 32;

    /// <summary>
    /// Matches each session of a batch, on as many threads as there are processors. Each session's outcome is its match, or the error it failed
    /// with (a cancellation is kept as an error too, and the caller rethrows it in order).
    /// </summary>
    private static (SessionMatch? Match, Exception? Error)[] ReadBatch(
        IReadOnlyList<SessionDigest> batch,
        IReadOnlyList<string> words,
        IReadOnlyList<string> probes,
        Func<SessionDigest, string?>? extraText,
        CancellationToken cancellationToken)
    {
        var read = new (SessionMatch? Match, Exception? Error)[batch.Count];
        Parallel.For(0, batch.Count, TranscriptIndex.ReadOptions(cancellationToken), i =>
        {
            // Checked before each session, not left to Parallel.For, which may still run bodies after a cancel. Measured on 2026-11-18 (a
            // probe under CPU load): the batch test read all 32 sessions after a cancel at the 10th. With this check, a session that starts
            // after the cancel is not read; at most one session per thread can be past the check when the cancel lands.
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                read[i] = (MatchSession(batch[i], words, probes, extraText?.Invoke(batch[i]), cancellationToken), null);
            }
            catch (Exception ex)
            {
                read[i] = (null, ex);
            }
        });
        return read;
    }

    /// <summary>
    /// True when the word is in the raw JSON line exactly as typed, so a plain substring test on the line is a safe pre-filter. That holds
    /// for ASCII letters, digits, '-', '_' and '.', which no JSON writer escapes. Any other character may be written escaped in the file
    /// (a backslash is <c>\\</c>, a quote is <c>\"</c>), so such a word is looked for by its <see cref="RawProbe"/> instead.
    /// </summary>
    public static bool SameInRawLine(string word) => word.All(IsRawSafe);

    /// <summary>The characters that are the same in the raw JSON line as in the text (see <see cref="SameInRawLine"/>).</summary>
    private static bool IsRawSafe(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.';

    /// <summary>
    /// The longest run of a word's characters that are the same in the raw line (see <see cref="SameInRawLine"/>), or empty when there is
    /// none. A message that contains the word also contains each such run, unescaped, in its raw line, so a line without the run cannot hold
    /// the word. This is the pre-filter of a word with an escaped character: "C:\Windows\System32" gives "System32" (round 10 item 2).
    /// </summary>
    public static string RawProbe(string word)
    {
        var best = string.Empty;
        var start = -1;
        for (var i = 0; i <= word.Length; i++)
        {
            if (i < word.Length && IsRawSafe(word[i]))
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0 && i - start > best.Length) best = word[start..i];
            start = -1;
        }
        return best;
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

    /// <summary>
    /// True when a raw line may hold a word: it holds the probe (see <see cref="RawProbe"/>) of some word, or some word has no probe, so
    /// the line cannot be ruled out. A line that fails this holds no word, so it is never parsed.
    /// </summary>
    private static bool MayHoldAWord(string raw, IReadOnlyList<string> probes)
    {
        foreach (var probe in probes)
        {
            if (probe.Length == 0 || raw.Contains(probe, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static SessionMatch? MatchSession(
        SessionDigest digest,
        IReadOnlyList<string> words,
        IReadOnlyList<string> probes,
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

        // A transcript that cannot be read throws here. The search counts it and goes on with the next session (RunCounted).
        foreach (var raw in TranscriptFile.ReadLines(digest.FilePath))
        {
            number++;
            cancellationToken.ThrowIfCancellationRequested();

            // Cheap first: most lines contain no word at all, so they are never parsed. Each word is looked for by its probe (see RawProbe).
            if (!MayHoldAWord(raw, probes)) continue;

            var shown = TranscriptLine.From(number, raw);
            if (shown is null || shown.Kind is not ("you" or "claude")) continue;

            var inThisMessage = words.Where(word => shown.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inThisMessage.Count == 0) continue;

            matchingMessages++;
            foreach (var word in inThisMessage) found.Add(word);
            if (hits.Count == HitsPerSession) hits.RemoveAt(0);
            hits.Add(new LineHit(number, shown.Kind, shown.Time, Snippet(shown.Text, inThisMessage[0])));
        }

        return found.Count == words.Count ? new SessionMatch(digest, matchingMessages, hits) : null;
    }
}

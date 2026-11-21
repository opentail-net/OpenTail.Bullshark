using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Lines of one Codex rollout, a page at a time (stage C2 Core part, C-D4). The rollout is found by its session id: first by the UUID in the
/// file name, then by the id in its session record. A compressed rollout is decoded as a stream and is not copied to disk. Zstandard has no
/// random access, so each page reads from the start of the file (B1b); an in-memory line index is left for the view stage. The lines are the
/// file's physical lines, verbatim (a line that is not JSON is returned too), so <c>FirstLine</c> counts them.
/// </summary>
public sealed class CodexTranscriptReader(ProviderPaths paths) : ITranscriptReader
{
    /// <summary>Characters a session id may not hold: wildcards, path separators, a drive colon and the other characters a file name refuses.</summary>
    private static readonly char[] UnsafeIdCharacters = ['*', '?', '/', '\\', ':', '"', '<', '>', '|'];

    /// <summary>
    /// The lines from <paramref name="firstLine"/> (zero-based), at most <paramref name="count"/> of them. <c>FirstLine</c> is clamped to the file's
    /// length, and a negative count gives no lines. <c>TotalLines</c> is null: the count is not taken for a page. An id with a wildcard, a path
    /// separator, a colon or <c>..</c> is refused with <see cref="ArgumentException"/>; an id with no rollout throws <see cref="FileNotFoundException"/>.
    /// </summary>
    public async Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct)
    {
        RequireSafeId(sessionId);
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var path = FindRollout(sessionId)
                ?? throw new FileNotFoundException($"No rollout for session {sessionId}.", sessionId + ".jsonl");
            return ReadLines(path, firstLine, count, ct);
        }, ct);
    }

    /// <summary>Refuses an id that could widen the search (see the class summary). Internal so a summary of a session checks its id the same way (S3).</summary>
    internal static void RequireSafeId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.IndexOfAny(UnsafeIdCharacters) >= 0
            || sessionId.Contains("..", StringComparison.Ordinal)
            || sessionId.Any(char.IsControl))
        {
            throw new ArgumentException("The session id is empty or has a wildcard, a path separator, a colon or '..'.", nameof(sessionId));
        }
    }

    /// <summary>
    /// The rollout for the id: a file whose name ends with it, else the first rollout whose session record has it. Null when none. Internal so the
    /// summaries find a rollout the same way as the transcript reader (S3); the caller checks the id with <see cref="RequireSafeId"/> first.
    /// </summary>
    internal string? FindRollout(string sessionId)
    {
        if (!Directory.Exists(paths.HistoryRoot)) return null;
        var files = RolloutReader.Files(paths.HistoryRoot);
        var byName = files.FirstOrDefault(path =>
            RolloutReader.StemOf(Path.GetFileName(path)).EndsWith("-" + sessionId, StringComparison.OrdinalIgnoreCase));
        return byName ?? files.FirstOrDefault(path => SessionIdOf(path) == sessionId);
    }

    /// <summary>The id of the first session record in a rollout, read up to that record. Null when the file has none or cannot be read.</summary>
    private static string? SessionIdOf(string path)
    {
        try
        {
            using var reader = RolloutReader.Open(path);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                using var document = RolloutReader.TryParseObject(line);
                if (document is null || RolloutReader.StringOf(document.RootElement, "type") != "session_meta") continue;
                return document.RootElement.TryGetProperty("payload", out var payload) ? RolloutReader.StringOf(payload, "id") : null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        return null;
    }

    /// <summary>
    /// One pass over the file. The page stops as soon as it is full; a page that starts past the end reads the whole file to clamp
    /// <c>FirstLine</c> to its length, and a page of no lines does the same.
    /// </summary>
    private static TranscriptPage ReadLines(string path, int firstLine, int count, CancellationToken ct)
    {
        var start = Math.Max(0, firstLine);
        var take = Math.Max(0, count);
        var lines = new List<string>();
        var seen = 0;
        using var reader = RolloutReader.Open(path);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (seen >= start && lines.Count < take) lines.Add(line);
            seen++;
            if (take > 0 && lines.Count == take) break;
        }
        return new TranscriptPage(lines, Math.Min(start, seen), null);
    }
}

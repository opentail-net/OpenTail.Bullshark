using System.Globalization;
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
    /// An id that is a UUIDv7 is first looked for in the dated folders of its start day and the days either side of it (see
    /// <see cref="InDayFolders"/>). Only the full search, which checks every rollout under the root, can return null.
    /// </summary>
    internal string? FindRollout(string sessionId)
    {
        if (!Directory.Exists(paths.HistoryRoot)) return null;
        if (CodexSessionTime.TryStartFromId(sessionId, out var start))
        {
            var near = InDayFolders(sessionId, start);
            if (near is not null) return near;
        }
        var files = RolloutReader.Files(paths.HistoryRoot);
        var byName = files.FirstOrDefault(path => NamedForId(path, sessionId));
        return byName ?? files.FirstOrDefault(path => SessionIdOf(path) == sessionId);
    }

    /// <summary>
    /// The first rollout whose name ends with the id, looked for only in the folders of the start day, the day before and the day after (the
    /// local calendar day, as Codex names its folders; the neighbours cover a day boundary). Null when none is there, which proves nothing about
    /// the rest of the archive: the caller then runs the full search.
    /// </summary>
    private string? InDayFolders(string sessionId, DateTimeOffset start)
    {
        if (!TryDaysNear(start, out var days)) return null;
        foreach (var day in days)
        {
            var folder = DayFolder(day);
            if (!Directory.Exists(folder)) continue;
            var match = RolloutReader.FilesIn(folder).FirstOrDefault(path => NamedForId(path, sessionId));
            if (match is not null) return match;
        }
        return null;
    }

    /// <summary>The start day (local calendar date) and the days either side of it, the start day first. False when a neighbour cannot be named.</summary>
    private static bool TryDaysNear(DateTimeOffset start, out DateOnly[] days)
    {
        try
        {
            var day = DateOnly.FromDateTime(start.ToLocalTime().DateTime);
            days = [day, day.AddDays(-1), day.AddDays(1)];
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            // A time at the very edge of the calendar: no neighbouring folder can be named, so the caller does the full search.
            days = [];
            return false;
        }
    }

    /// <summary>The dated folder of a day under the history root: <c>YYYY\MM\DD</c>, zero-padded.</summary>
    private string DayFolder(DateOnly day) => Path.Combine(
        paths.HistoryRoot,
        day.ToString("yyyy", CultureInfo.InvariantCulture),
        day.ToString("MM", CultureInfo.InvariantCulture),
        day.ToString("dd", CultureInfo.InvariantCulture));

    /// <summary>True when the rollout's file name, without its extension, ends with <c>-</c> and the id (case-insensitive, as the file-name search always was).</summary>
    private static bool NamedForId(string path, string sessionId) =>
        RolloutReader.StemOf(Path.GetFileName(path)).EndsWith("-" + sessionId, StringComparison.OrdinalIgnoreCase);

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

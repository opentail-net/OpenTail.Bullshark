using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// What one session's transcript says it wrote: each file once, and the time of its last write to each file. It is read from the
/// transcript itself, not from the index, so the writes of a live session are seen as they are now.
/// </summary>
public sealed record SessionWrites(
    string SessionId,
    IReadOnlyList<string> Files,
    IReadOnlyDictionary<string, DateTimeOffset> LastWrites,
    TokenUsage? Usage = null);

/// <summary>One session in a clash: the spec's name, its session id, and when it last wrote the file (null when the transcript has no time for it).</summary>
public sealed record ClashSession(string Name, string SessionId, DateTimeOffset? LastWrite);

/// <summary>A file that two or more sessions of the same run have written. <see cref="Path"/> is the full path.</summary>
public sealed record FileClash(string Path, IReadOnlyList<ClashSession> Sessions);

/// <summary>
/// Files that more than one coder (or the planner) of one run has written (item 1 of the gems plan). Two sessions editing the same
/// file is the usual way parallel work loses changes, so the owner is told when it happens. Pure: the transcripts are read by the caller.
/// </summary>
public static class FileOverlap
{
    /// <summary>
    /// The clashes among the sessions of <paramref name="run"/>. A session is in the run when its spec's name has a record with a session id.
    /// <see cref="RunRecord"/> holds no session ids, so the state records give them. A relative path is taken from the run's folder.
    /// A session that wrote a file twice counts once. A file is a clash only when two different sessions wrote it.
    /// </summary>
    public static IReadOnlyList<FileClash> Find(
        RunRecord run,
        IReadOnlyDictionary<string, SessionRecord> records,
        IEnumerable<SessionWrites> writes)
    {
        var writeList = writes.ToList();
        var sessions = new List<(string Name, SessionWrites Writes)>();
        foreach (var spec in run.Specs.DistinctBy(s => s.Name, StringComparer.Ordinal))
        {
            if (!records.TryGetValue(spec.Name, out var record) || string.IsNullOrEmpty(record.SessionId)) continue;
            var found = writeList.FirstOrDefault(w => string.Equals(w.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
            if (found is not null) sessions.Add((spec.Name, found));
        }

        var clashes = new Dictionary<string, (string Path, Dictionary<string, ClashSession> ByName)>(PathComparer);
        foreach (var (name, session) in sessions)
        {
            foreach (var file in session.Files)
            {
                var full = Normalise(file, run.WorkingDirectory);
                if (full is null) continue;
                if (!clashes.TryGetValue(full, out var entry))
                {
                    entry = (full, new Dictionary<string, ClashSession>(StringComparer.Ordinal));
                    clashes[full] = entry;
                }

                var when = session.LastWrites.TryGetValue(file, out var written) ? written : (DateTimeOffset?)null;
                // Two spellings of one file in one session: the later write is kept.
                if (entry.ByName.TryGetValue(name, out var earlier) && earlier.LastWrite is { } kept && when is { } later && kept > later)
                    continue;
                entry.ByName[name] = new ClashSession(name, session.SessionId, when);
            }
        }

        return clashes.Values
            .Where(entry => entry.ByName.Count > 1)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => new FileClash(entry.Path, entry.ByName.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>
    /// The full path of a file as written in a transcript. A relative path is joined to <paramref name="workingDirectory"/> first,
    /// and "/" and "\" are made the same. Null when the path is not a valid path.
    /// </summary>
    public static string? Normalise(string path, string workingDirectory)
    {
        try
        {
            var joined = Path.IsPathRooted(path) || string.IsNullOrEmpty(workingDirectory) ? path : Path.Combine(workingDirectory, path);
            return Path.GetFullPath(joined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The records of the sessions that are live now: listed in <paramref name="agents"/>, and not `done`, `stopped` or `failed`. The
    /// clashes are found among these only (round 5 item 7). A finished session's shared file is no longer something the owner must act
    /// on, and kept in the list it would stay under "Needs you" for as long as its run is kept. A session that is not listed (lost, and
    /// about to be resumed) is not live either.
    /// </summary>
    public static IReadOnlyDictionary<string, SessionRecord> LiveRecords(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyList<AgentInfo> agents)
    {
        var live = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        foreach (var (name, record) in records)
        {
            if (record.SessionId.Length == 0) continue;
            var listed = agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
            if (listed is null) continue;
            var state = listed.EffectiveState;
            if (SupervisorPolicy.IsStoppedOrFailed(state) || string.Equals(state, "done", StringComparison.OrdinalIgnoreCase)) continue;
            live[name] = record;
        }
        return live;
    }

    /// <summary>
    /// Paths are compared without case on Windows, where C:\A.cs and c:\a.cs are one file, and exactly elsewhere. The comparer is
    /// the one the keys of <see cref="Find"/> use, and the one a caller uses to match a clash to a path it holds.
    /// </summary>
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The detail line of a clash as one session sees it: the file, and the other sessions that wrote it with their last write.</summary>
    public static string Detail(FileClash clash, string name)
    {
        var others = clash.Sessions
            .Where(s => !string.Equals(s.Name, name, StringComparison.Ordinal))
            .Select(s => s.LastWrite is { } when
                ? $"{s.Name} (last write {when.ToLocalTime():HH:mm})"
                : s.Name);
        return $"{clash.Path} is also written by {string.Join(", ", others)}";
    }
}

/// <summary>
/// Reads each live transcript's writes, and reads a transcript again only when its length or its last write time changed. A
/// pass runs every 15 seconds, so an idle session costs one file lookup, not a full read. Not thread-safe: one pass uses it at a time.
/// </summary>
public sealed class SessionWritesCache
{
    private readonly Dictionary<string, Entry> _entries = new(FileOverlap.PathComparer);

    private sealed record Entry(long Length, DateTime Stamp, SessionWrites Writes);

    /// <summary>The writes of one transcript. Throws the same IO exceptions as the file read does; the caller handles them per session.</summary>
    public SessionWrites Read(string sessionId, string transcriptPath)
    {
        var info = new FileInfo(transcriptPath);
        var stamp = info.LastWriteTimeUtc;
        var length = info.Length;
        if (_entries.TryGetValue(transcriptPath, out var known) && known.Length == length && known.Stamp == stamp)
            return known.Writes;

        var summary = TranscriptReader.ReadFile(transcriptPath);
        // The usage rides along, so a run's token limit is checked from the same read as the overlap (additions item 4).
        var writes = new SessionWrites(
            sessionId,
            summary.FilesWritten ?? [],
            summary.FileLastWrites ?? new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal),
            summary.TotalUsage);
        _entries[transcriptPath] = new Entry(length, stamp, writes);
        return writes;
    }
}

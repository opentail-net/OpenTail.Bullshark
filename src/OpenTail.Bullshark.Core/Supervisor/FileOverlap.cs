using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    TokenUsage? Usage = null,
    string? Folder = null,
    /// <summary>
    /// The folder each relative file was written from, by the cwd of the record that carries its latest write (round 8 item 8). A file not in
    /// it is taken from <see cref="Folder"/>, the session's latest folder.
    /// </summary>
    IReadOnlyDictionary<string, string>? FileFolders = null);

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
    /// <see cref="RunRecord"/> holds no session ids, so the state records give them. A relative path is taken from the folder it was written
    /// from (round 8 item 8), else the session's folder (the latest cwd of its transcript, which can be a worktree), else the run's folder.
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
                // A relative path is taken from the folder it was written from (round 8 item 8), else the session's folder (round 7 item 4), else the run's.
                var from = session.FileFolders is not null && session.FileFolders.TryGetValue(file, out var fromFolder) ? fromFolder : session.Folder;
                var full = Normalise(file, from ?? run.WorkingDirectory);
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
    /// Whether a run's transcripts are read at all. A file clash needs two different sessions of the run, and a token limit needs the
    /// run's spend. A run with one session and no limit reads none (round 5 item 9).
    /// </summary>
    public static bool NeedsTranscripts(RunRecord run) =>
        run.Limits is not null || run.Specs.DistinctBy(spec => spec.Name, StringComparer.Ordinal).Count() > 1;

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
/// Reads each live transcript's writes. A transcript is read at all only when its length or its last write time changed. When it grew
/// (the normal case for a working session), only the bytes appended since the last read are read and added to what is kept (round 5
/// item 9), so a pass does not parse a whole transcript every 15 seconds. A line counts once its newline is written: a line still being
/// written waits for the next read. A transcript that shrank or was rewritten in place is read from the start. Not thread-safe: one pass
/// uses it at a time.
/// </summary>
public sealed class SessionWritesCache
{
    private readonly Dictionary<string, Entry> _entries = new(FileOverlap.PathComparer);
    private readonly Func<string, Stream> _open;

    /// <param name="open">Opens a transcript for reading. The default is the shared rule for transcripts; a test counts the bytes it reads.</param>
    public SessionWritesCache(Func<string, Stream>? open = null)
    {
        _open = open ?? TranscriptFile.Open;
    }

    /// <summary>The number of leading bytes of a transcript that are hashed to tell whether it is still the file that was read (round 6 item 7).</summary>
    private const int HeadBytes = 4 * 1024;

    /// <summary>
    /// What is kept for one transcript: the file's size and write time when it was last read, how far into the file the complete lines
    /// reach, the hash of its first bytes at that read, and the writes so far. The ledger keeps each message's usage, so a message split across
    /// two reads is still counted once.
    /// </summary>
    private sealed class Entry
    {
        public long Length;
        public DateTime Stamp;
        public long Offset;
        public int Lines;
        /// <summary>How many leading bytes were hashed at the last read (less than <see cref="HeadBytes"/> for a shorter file).</summary>
        public int HeadLength;
        /// <summary>The SHA-256 of the first <see cref="HeadLength"/> bytes at the last read.</summary>
        public byte[]? HeadHash;
        public readonly UsageLedger Ledger = new();
        public readonly List<string> Files = [];
        public readonly HashSet<string> FileSet = new(StringComparer.Ordinal);
        public readonly Dictionary<string, DateTimeOffset> LastWrites = new(StringComparer.Ordinal);
        /// <summary>The folder each relative file was last written from (round 8 item 8). Kept with the last write of each file.</summary>
        public readonly Dictionary<string, string> FileFolders = new(StringComparer.Ordinal);
        public TokenUsage Usage = TokenUsage.Zero;
        /// <summary>The working folder the transcript last named (round 7 item 4): the folder a relative file path is taken from.</summary>
        public string? Cwd;
        public SessionWrites? Writes;
    }

    /// <summary>How far a read has got: the bytes of the complete lines before it, and how many lines those are.</summary>
    private sealed class Progress
    {
        public long Offset;
        public int Lines;
    }

    /// <summary>The writes of one transcript. Throws the same IO exceptions as the file read does; the caller handles them per session.</summary>
    public SessionWrites Read(string sessionId, string transcriptPath)
    {
        var info = new FileInfo(transcriptPath);
        var stamp = info.LastWriteTimeUtc;
        var length = info.Length;
        if (_entries.TryGetValue(transcriptPath, out var known) && known.Writes is { } cached
            && known.Length == length && known.Stamp == stamp && string.Equals(cached.SessionId, sessionId, StringComparison.Ordinal))
            return cached;

        // Kept only while the file is the one that was read: shorter than what was consumed, or the same size but written again, means a new file.
        var replaced = known is null || length < known.Offset || (length == known.Length && stamp != known.Stamp);
        var entry = replaced ? new Entry() : known!;

        var progress = new Progress { Offset = entry.Offset, Lines = entry.Lines };
        byte[] head;
        using (var stream = _open(transcriptPath))
        {
            head = ReadHead(stream);
            // A file rewritten in place can be longer than what was read, so its length does not show the change: its first bytes do. A
            // different head means a new file, read from its start (round 6 item 7).
            if (!replaced && !HeadMatches(entry, head))
            {
                entry = new Entry();
                progress = new Progress();
            }

            stream.Seek(progress.Offset, SeekOrigin.Begin);
            var summary = TranscriptReader.Summarize(CompleteLines(stream, progress), entry.Ledger, transcriptPath, firstLine: entry.Lines);
            foreach (var file in summary.FilesWritten ?? [])
                if (entry.FileSet.Add(file)) entry.Files.Add(file);
            foreach (var (file, when) in summary.FileLastWrites ?? new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal))
                entry.LastWrites[file] = entry.LastWrites.TryGetValue(file, out var earlier) && earlier > when ? earlier : when;
            // A later read's folder for a file is the one its latest write names, so it replaces the earlier one (round 8 item 8).
            foreach (var (file, folder) in summary.FileFolders ?? new Dictionary<string, string>(StringComparer.Ordinal))
                entry.FileFolders[file] = folder;
            // The usage of the whole file, from the shared ledger: the usage rides along, so a run's token limit is checked from the same read
            // as the overlap (additions item 4).
            entry.Usage = summary.TotalUsage;
            // A read of only the new lines can name no folder: the folder of the last read that did is kept.
            if (summary.LatestCwd is not null) entry.Cwd = summary.LatestCwd;
        }
        entry.Offset = progress.Offset;
        entry.Lines = progress.Lines;
        entry.Length = length;
        entry.Stamp = stamp;
        entry.HeadLength = head.Length;
        entry.HeadHash = SHA256.HashData(head);
        entry.Writes = new SessionWrites(
            sessionId,
            entry.Files.ToList(),
            new Dictionary<string, DateTimeOffset>(entry.LastWrites, StringComparer.Ordinal),
            entry.Usage,
            entry.Cwd,
            new Dictionary<string, string>(entry.FileFolders, StringComparer.Ordinal));
        _entries[transcriptPath] = entry;
        return entry.Writes;
    }

    /// <summary>
    /// True when the text is one complete JSON object. A record is an object, and a prefix of one is never valid JSON, so a line still being
    /// written does not pass.
    /// </summary>
    private static bool IsWholeObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The first bytes of a transcript, up to <see cref="HeadBytes"/>. The stream is left after them; the caller seeks.</summary>
    private static byte[] ReadHead(Stream stream)
    {
        var buffer = new byte[HeadBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0) break;
            total += read;
        }
        return buffer[..total];
    }

    /// <summary>
    /// True when the first bytes of the file are the ones kept at the last read. A file now shorter than the head that was kept does not match.
    /// </summary>
    private static bool HeadMatches(Entry entry, byte[] head) =>
        entry.HeadHash is { } kept
        && head.Length >= entry.HeadLength
        && SHA256.HashData(head.AsSpan(0, entry.HeadLength)).AsSpan().SequenceEqual(kept);

    /// <summary>
    /// The complete lines from the stream's position, decoded as UTF-8 without their line break. A line is complete only when its newline
    /// has been read, so a line still being written is not returned and <paramref name="progress"/> does not move past it. The byte order
    /// mark at the start of the file is not part of the first line's text. A last line with no newline is returned when it is a whole JSON
    /// object (round 6 item 8): <paramref name="progress"/> does not move past it, so when its newline is written it is read again in the same
    /// place, and the usage ledger counts it once.
    /// </summary>
    private static IEnumerable<string> CompleteLines(Stream stream, Progress progress)
    {
        var buffer = new byte[64 * 1024];
        using var line = new MemoryStream();
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                if (line.Length > 0)
                {
                    var pending = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                    if (progress.Offset == 0) pending = pending.TrimStart('﻿');
                    if (IsWholeObject(pending)) yield return pending.EndsWith('\r') ? pending[..^1] : pending;
                }
                yield break;
            }

            var position = 0;
            while (position < read)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', position, read - position);
                if (newline < 0)
                {
                    line.Write(buffer, position, read - position);
                    break;
                }

                line.Write(buffer, position, newline - position);
                var first = progress.Offset == 0;
                var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                progress.Offset += line.Length + 1;
                progress.Lines++;
                line.SetLength(0);
                if (first) text = text.TrimStart('﻿');
                yield return text.EndsWith('\r') ? text[..^1] : text;
                position = newline + 1;
            }
        }
    }
}

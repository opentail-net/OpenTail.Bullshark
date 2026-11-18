using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>One file a session's Changes tab ticked as viewed, with the stamp the file had then (see <see cref="ViewedFiles.StampOf"/>).</summary>
public sealed record ViewedFile(string Path, string Stamp);

/// <summary>The ticks of one session: when its last tick was made, and the files ticked, the most recent last.</summary>
public sealed record ViewedSession(DateTimeOffset MarkedAt, IReadOnlyList<ViewedFile> Files);

/// <summary>
/// The "viewed" ticks of the Changes tab (features batch G1). A tick is a mark on one file of one session, with the file's stamp at the time: its
/// size and write time, or <see cref="Deleted"/> for a file that is gone. A tick counts only while the file still has that stamp, so a file that
/// changed after it was ticked is unviewed again and shows as new work. The marks are kept in ui.json (key <see cref="Key"/>) and hold paths and
/// numbers only: nothing is written to the reviewed repository or to git, and no file text is stored.
/// </summary>
public static class ViewedFiles
{
    /// <summary>The ui.json key the marks are kept under.</summary>
    public const string Key = "viewedFiles";

    /// <summary>The stamp of a file that is not there any more.</summary>
    public const string Deleted = "deleted";

    /// <summary>The most files one session keeps. Past it, the oldest ticks are dropped.</summary>
    public const int MaxFilesPerSession = 500;

    /// <summary>A session whose last tick is older than this is dropped with its ticks.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);

    /// <summary>The stamp of a file that exists: its size and its write time in UTC ticks.</summary>
    public static string StampOf(long length, long lastWriteTicks) =>
        length.ToString(CultureInfo.InvariantCulture) + ":" + lastWriteTicks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The stamp of a changed file now: <see cref="Deleted"/> when the file is not there, its size and write time when it is, and null when
    /// it cannot be read (then it cannot be ticked). Reads the file's metadata only; never its text.
    /// </summary>
    public static string? CurrentStamp(string folder, string path)
    {
        try
        {
            var full = Path.Combine(folder, path);
            if (!File.Exists(full)) return Deleted;
            var info = new FileInfo(full);
            return StampOf(info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True only when the session ticked the file and the stamp it ticked is the file's current stamp. A null current stamp is never viewed.</summary>
    public static bool IsViewed(IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, string path, string? currentStamp)
    {
        if (currentStamp is null) return false;
        if (!marks.TryGetValue(sessionId, out var session)) return false;
        return session.Files.Any(file => string.Equals(file.Path, path, StringComparison.Ordinal) && file.Stamp == currentStamp);
    }

    /// <summary>The paths of the session that are viewed now: each path with the stamp the file has now (a null stamp is not viewed).</summary>
    public static IReadOnlySet<string> ViewedAmong(IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, IReadOnlyDictionary<string, string?> currentStamps)
    {
        var viewed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, stamp) in currentStamps)
            if (IsViewed(marks, sessionId, path, stamp)) viewed.Add(path);
        return viewed;
    }

    /// <summary>Ticks one file as viewed with its current stamp. A null stamp (a file that could not be read) changes nothing.</summary>
    public static IReadOnlyDictionary<string, ViewedSession> Mark(
        IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, string path, string? stamp, DateTimeOffset now) =>
        MarkAll(marks, sessionId, new Dictionary<string, string?> { [path] = stamp }, now);

    /// <summary>
    /// Ticks several files, each with its own current stamp. Files with a null stamp are left out. A file ticked again moves to the newest place.
    /// The session's last tick becomes <paramref name="now"/>, and the session keeps at most <see cref="MaxFilesPerSession"/> files.
    /// </summary>
    public static IReadOnlyDictionary<string, ViewedSession> MarkAll(
        IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, IReadOnlyDictionary<string, string?> stamps, DateTimeOffset now)
    {
        var added = stamps.Where(pair => pair.Value is not null).ToList();
        if (added.Count == 0) return marks;

        var files = marks.TryGetValue(sessionId, out var known) ? known.Files.ToList() : [];
        foreach (var (path, stamp) in added)
        {
            files.RemoveAll(file => string.Equals(file.Path, path, StringComparison.Ordinal));
            files.Add(new ViewedFile(path, stamp!));
        }
        return WithSession(marks, sessionId, new ViewedSession(now, Capped(files)));
    }

    /// <summary>Removes the tick of one file. A session with no ticks left is dropped.</summary>
    public static IReadOnlyDictionary<string, ViewedSession> Unmark(IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, string path)
    {
        if (!marks.TryGetValue(sessionId, out var known)) return marks;
        var files = known.Files.Where(file => !string.Equals(file.Path, path, StringComparison.Ordinal)).ToList();
        return WithSession(marks, sessionId, files.Count == 0 ? null : known with { Files = files });
    }

    /// <summary>Removes every tick of one session.</summary>
    public static IReadOnlyDictionary<string, ViewedSession> Clear(IReadOnlyDictionary<string, ViewedSession> marks, string sessionId) =>
        marks.ContainsKey(sessionId) ? WithSession(marks, sessionId, null) : marks;

    /// <summary>
    /// Drops the ticks of a session for files that are no longer changed (committed, or put back). Call it only with the complete list of
    /// changed files: a list that was cut at its limit would drop ticks of files that are still changed.
    /// </summary>
    public static IReadOnlyDictionary<string, ViewedSession> DropUnchanged(
        IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, IReadOnlySet<string> changedPaths)
    {
        if (!marks.TryGetValue(sessionId, out var known)) return marks;
        var files = known.Files.Where(file => changedPaths.Contains(file.Path)).ToList();
        if (files.Count == known.Files.Count) return marks;
        return WithSession(marks, sessionId, files.Count == 0 ? null : known with { Files = files });
    }

    /// <summary>
    /// Drops the sessions whose last tick is older than <see cref="MaxAge"/> than <paramref name="now"/>, and caps every session at
    /// <see cref="MaxFilesPerSession"/> files (the oldest ticks go first).
    /// </summary>
    public static IReadOnlyDictionary<string, ViewedSession> Prune(IReadOnlyDictionary<string, ViewedSession> marks, DateTimeOffset now)
    {
        var kept = new Dictionary<string, ViewedSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sessionId, session) in marks)
        {
            if (now - session.MarkedAt > MaxAge) continue;
            var files = Capped(session.Files.ToList());
            if (files.Count == 0) continue;
            kept[sessionId] = session with { Files = files };
        }
        return kept;
    }

    /// <summary>The marks as ui.json holds them: one object per session, with its last tick ("at") and its files ("files", path to stamp).</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, ViewedSession> marks)
    {
        var root = new JsonObject();
        foreach (var (sessionId, session) in marks)
        {
            var files = new JsonObject();
            foreach (var file in session.Files) files[file.Path] = file.Stamp;
            root[sessionId] = new JsonObject
            {
                ["at"] = session.MarkedAt.ToString("O", CultureInfo.InvariantCulture),
                ["files"] = files,
            };
        }
        return root;
    }

    /// <summary>
    /// The marks read from ui.json. A null node, or a node that is not an object, gives no marks. A session or file that is damaged (not an
    /// object, a time that does not read, a stamp that is not text) is left out, and the rest is kept. Nothing is thrown.
    /// </summary>
    public static IReadOnlyDictionary<string, ViewedSession> Parse(JsonNode? node)
    {
        var marks = new Dictionary<string, ViewedSession>(StringComparer.OrdinalIgnoreCase);
        if (node is not JsonObject root) return marks;
        foreach (var (sessionId, value) in root)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || value is not JsonObject session) continue;
            if (!TextOf(session["at"], out var at) || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var markedAt)) continue;
            if (session["files"] is not JsonObject files) continue;

            var list = new List<ViewedFile>();
            foreach (var (path, stamp) in files)
            {
                if (string.IsNullOrEmpty(path) || !TextOf(stamp, out var text)) continue;
                list.Add(new ViewedFile(path, text));
            }
            if (list.Count > 0) marks[sessionId] = new ViewedSession(markedAt, Capped(list));
        }
        return marks;
    }

    /// <summary>True when the node is a JSON string, and gives its text. Any other kind of node (or none) is false, and nothing is thrown.</summary>
    private static bool TextOf(JsonNode? node, out string text)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var read))
        {
            text = read;
            return true;
        }
        text = string.Empty;
        return false;
    }

    private static List<ViewedFile> Capped(List<ViewedFile> files) =>
        files.Count <= MaxFilesPerSession ? files : files.GetRange(files.Count - MaxFilesPerSession, MaxFilesPerSession);

    /// <summary>A copy of the marks with one session replaced, or removed when <paramref name="session"/> is null.</summary>
    private static IReadOnlyDictionary<string, ViewedSession> WithSession(
        IReadOnlyDictionary<string, ViewedSession> marks, string sessionId, ViewedSession? session)
    {
        var copy = new Dictionary<string, ViewedSession>(marks, StringComparer.OrdinalIgnoreCase);
        if (session is null) copy.Remove(sessionId);
        else copy[sessionId] = session;
        return copy;
    }
}

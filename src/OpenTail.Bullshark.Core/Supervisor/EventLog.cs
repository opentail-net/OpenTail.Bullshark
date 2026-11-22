using System.Text.Json;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One thing the supervisor did or saw (additions item 5): a start, an adoption, a resume, an alert, or a session's state change.
/// <see cref="Event"/> is "start", "adopt", "resume", "state", or "alert:" and the alert kind. <see cref="Session"/> is the spec's name.
/// <see cref="RunId"/> is the run the session belongs to (round 5 item 10). Lines written before it have none, and read as null.
/// </summary>
public sealed record SupervisorEvent(DateTimeOffset At, string Session, string Event, string Detail, string? RunId = null);

/// <summary>Receives the supervisor's events. The runner writes through it; with no sink, nothing is recorded.</summary>
public interface IEventSink
{
    void Write(SupervisorEvent evt);
}

/// <summary>
/// The timeline's file, events.jsonl beside the state file: one JSON object per line, appended under the file lock. When the file reaches
/// <see cref="FileSizeLimit"/> it is moved to events.1.jsonl (replacing the older one), so the log stays bounded. A line cut off at the
/// end (a write that did not finish) is skipped when reading, and the lines before it are kept.
/// </summary>
public sealed class EventLog : IEventSink
{
    /// <summary>The size at which the file is rotated: 5 MB.</summary>
    public const long DefaultFileSizeLimit = 5L * 1024 * 1024;

    private readonly long _rotateBytes;
    private readonly Action<string>? _log;
    private bool _rotationFailureLogged;

    /// <param name="filePath">The current file, events.jsonl.</param>
    /// <param name="rotateBytes">The size at which the file is rotated.</param>
    /// <param name="log">Where a rotation that failed is said, once. Null says nothing.</param>
    public EventLog(string filePath, long rotateBytes = DefaultFileSizeLimit, Action<string>? log = null)
    {
        FilePath = filePath;
        _rotateBytes = rotateBytes;
        _log = log;
    }

    /// <summary>The current file, events.jsonl.</summary>
    public string FilePath { get; }

    /// <summary>The file the current one is rotated into, events.1.jsonl beside it.</summary>
    public string RotatedFilePath
    {
        get
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(FilePath)) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(FilePath);
            return Path.Combine(folder, $"{name}.1{Path.GetExtension(FilePath)}");
        }
    }

    public long FileSizeLimit => _rotateBytes;

    /// <summary>Appends one event. The rotation and the append happen under the file lock, so two writers never interleave a line.</summary>
    public void Write(SupervisorEvent evt)
    {
        var line = JsonSerializer.Serialize(evt) + "\n";
        var folder = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        using var _ = FileLock.Hold(FilePath);
        var current = new FileInfo(FilePath);
        if (current.Exists && current.Length >= _rotateBytes) TryRotate();
        // A write that was cut off leaves a last line with no newline. The new event would be glued onto it and lost with it, so the
        // cut-off line is ended first; the reader then skips that line alone (round 5 item 5).
        var prefix = EndsWithoutNewline(FilePath) ? "\n" : string.Empty;
        File.AppendAllText(FilePath, prefix + line);
    }

    /// <summary>
    /// Moves the current file to the rotated one. When the rotated file cannot be replaced (another program holds it, or it is denied), the
    /// current file stays as it is and goes on taking the events, so none is lost; the failure is said once (round 5 item 18). Before, the
    /// failure stopped every later event, until the owner removed the rotated file.
    /// </summary>
    private void TryRotate()
    {
        try
        {
            File.Move(FilePath, RotatedFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_rotationFailureLogged) return;
            _rotationFailureLogged = true;
            _log?.Invoke($"events: not rotated, so the log keeps growing: {ex.GetType().Name}");
        }
    }

    /// <summary>True when the file has bytes and its last byte is not a newline. A missing or empty file is not.</summary>
    private static bool EndsWithoutNewline(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0) return false;
        using var stream = info.OpenRead();
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() != '\n';
    }

    /// <summary>
    /// All events, the rotated file first, so they are in the order they happened. A line that is not a complete event is skipped:
    /// the last line of a file being written may be cut off, and that is not an error.
    /// </summary>
    public IReadOnlyList<SupervisorEvent> Read()
    {
        using var _ = FileLock.Hold(FilePath);
        var events = new List<SupervisorEvent>();
        foreach (var file in new[] { RotatedFilePath, FilePath }) AddFile(file, events);
        return events;
    }

    /// <summary>
    /// The newest <paramref name="max"/> events from <paramref name="since"/> on, in the order they happened (the away digest, backlog D1). Read
    /// as <see cref="Read"/> reads them: a bad line is skipped. Unlike <see cref="Read"/>, a file that cannot be read (denied, or cut short) gives
    /// the lines it did give, and its failure is said through the log, so the digest still shows the other file. A lock that is not free in time
    /// gives no events, and that is said through the log too.
    /// </summary>
    public IReadOnlyList<SupervisorEvent> ReadSince(DateTimeOffset since, int max)
    {
        var recent = new List<SupervisorEvent>();
        try
        {
            using var _ = FileLock.Hold(FilePath);
            foreach (var file in new[] { RotatedFilePath, FilePath })
            {
                var found = new List<SupervisorEvent>();
                try
                {
                    AddFile(file, found);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log?.Invoke($"events: a file not read for the digest: {ex.GetType().Name}");
                }
                recent.AddRange(found.Where(evt => evt.At >= since));
            }
        }
        catch (TimeoutException ex)
        {
            _log?.Invoke($"events: not read for the digest: {ex.GetType().Name}");
            return [];
        }
        return recent.OrderBy(evt => evt.At).TakeLast(Math.Max(0, max)).ToList();
    }

    /// <summary>Adds the events of one file, in the order of its lines. A missing file adds none; a file that cannot be opened or read throws.</summary>
    private static void AddFile(string file, List<SupervisorEvent> events)
    {
        // Opened directly, not checked with File.Exists first: a file the user cannot open is an error, not an empty timeline.
        StreamReader reader;
        try
        {
            reader = new StreamReader(File.OpenRead(file));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        using (reader)
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<SupervisorEvent>(line) is { } evt) events.Add(evt);
                }
                catch (JsonException)
                {
                    // A line cut off at the end of a file that is still being written.
                }
            }
        }
    }
}

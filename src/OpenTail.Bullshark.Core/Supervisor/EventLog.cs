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

    public EventLog(string filePath, long rotateBytes = DefaultFileSizeLimit)
    {
        FilePath = filePath;
        _rotateBytes = rotateBytes;
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
        if (current.Exists && current.Length >= _rotateBytes)
            File.Move(FilePath, RotatedFilePath, overwrite: true);
        // A write that was cut off leaves a last line with no newline. The new event would be glued onto it and lost with it, so the
        // cut-off line is ended first; the reader then skips that line alone (round 5 item 5).
        var prefix = EndsWithoutNewline(FilePath) ? "\n" : string.Empty;
        File.AppendAllText(FilePath, prefix + line);
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
        foreach (var file in new[] { RotatedFilePath, FilePath })
        {
            // Opened directly, not checked with File.Exists first: a file the user cannot open is an error, not an empty timeline.
            StreamReader reader;
            try
            {
                reader = new StreamReader(File.OpenRead(file));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
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
        return events;
    }
}

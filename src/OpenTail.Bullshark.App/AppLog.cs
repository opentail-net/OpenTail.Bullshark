using System.IO;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Minimal file log for the app. Writes to %LOCALAPPDATA%\OpenTail.Bullshark\logs\app.log. When the file passes 5 MB it is moved to app.log.1,
/// replacing the one there, and a new file starts (round 12 B8). Called from pool threads, so every access takes <see cref="Gate"/>.
/// </summary>
public static class AppLog
{
    /// <summary>The size past which the log moves to its .1 copy.</summary>
    private const long MaxBytes = 5L * 1024 * 1024;

    private static readonly object Gate = new();

    public static string FilePath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTail.Bullshark", "logs", "app.log");

    /// <summary>Puts the log beside a state file: the logs folder next to it, so a run with its own state keeps its own log.</summary>
    public static void FollowStateFile(string statePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(statePath));
        if (string.IsNullOrEmpty(folder)) return;
        lock (Gate) FilePath = Path.Combine(folder, "logs", "app.log");
    }

    /// <summary>Appends one line. Never throws: logging runs inside catch blocks, so a line that cannot be written is lost and the next one tries.</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                TryMoveWhenLarge(path);
                File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>
    /// Moves the log to app.log.1 when it has passed <see cref="MaxBytes"/>, replacing that copy. A move that fails (another program holds the
    /// file) leaves the log as it is, and the line is still written. Called under <see cref="Gate"/>.
    /// </summary>
    private static void TryMoveWhenLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes) return;
            File.Move(path, path + ".1", overwrite: true);
        }
        catch (Exception)
        {
            // Kept as it is; the next write tries again.
        }
    }
}

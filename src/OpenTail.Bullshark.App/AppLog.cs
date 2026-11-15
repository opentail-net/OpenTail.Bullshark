using System.IO;

namespace OpenTail.Bullshark.App;

/// <summary>Minimal file log for the app. Writes to %LOCALAPPDATA%\OpenTail.Bullshark\logs\app.log.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTail.Bullshark", "logs", "app.log");

    /// <summary>Puts the log beside a state file: the logs folder next to it, so a run with its own state keeps its own log.</summary>
    public static void FollowStateFile(string statePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(statePath));
        if (!string.IsNullOrEmpty(folder)) FilePath = Path.Combine(folder, "logs", "app.log");
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}

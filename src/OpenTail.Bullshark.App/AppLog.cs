using System.IO;

namespace OpenTail.Bullshark.App;

/// <summary>Minimal file log for the app. Writes to %LOCALAPPDATA%\OpenTail.Bullshark\logs\app.log.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTail.Bullshark", "logs", "app.log");

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

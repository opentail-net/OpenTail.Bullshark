using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// ui.json, beside the state file: the rail theme and the zoom of the session text (zoom.md). Each save changes one key and keeps the
/// others. Every read and write takes the file's lock and writes atomically (<see cref="FileLock"/>), so two windows never lose each
/// other's key. A file that is not a JSON object (truncated or damaged) is moved aside to <c>ui.json.bad</c>, logged, and read as
/// missing, so the next save starts a new file; without that, every later save would fail. A file that cannot be read because
/// another process holds it is left as it is: it is damaged only when it does not parse.
/// </summary>
public static class UiSettingsFile
{
    public const string FileName = "ui.json";

    /// <summary>The ui.json beside a state file.</summary>
    public static string PathFor(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, FileName);

    /// <summary>The value of one key, or null when the file or the key is missing, or the file cannot be read.</summary>
    public static JsonNode? Read(string path, string key, Action<string>? log = null)
    {
        log ??= _ => { };
        try
        {
            using var _ = FileLock.Hold(path);
            return ReadRoot(path, log)?[key];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            log($"ui.json not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sets one key and writes the file, keeping the other keys. The change is not made when the file cannot be written.</summary>
    /// <summary>Sets one key and writes the file. Returns true when it was written; a failure is logged and gives false (nothing is thrown).</summary>
    public static bool Set(string path, string key, JsonNode? value, Action<string>? log = null)
    {
        log ??= _ => { };
        try
        {
            using var _ = FileLock.Hold(path);
            var root = ReadRoot(path, log) ?? new JsonObject();
            root[key] = value;
            FileLock.WriteAtomic(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            log($"ui.json not saved: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The file as an object: null when there is no file, or when the file does not parse as one (it is then moved aside). A file that
    /// cannot be read at all throws, and is left where it is.
    /// </summary>
    private static JsonObject? ReadRoot(string path, Action<string> log)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new JsonException("it is not a JSON object");
        }
        catch (JsonException ex)
        {
            MoveAside(path, log, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Moves a damaged file to <c>ui.json.bad</c>, replacing an older one there, and logs it once. If the move fails, the error goes to the
    /// caller, which logs it and changes nothing: the damaged file is never overwritten.
    /// </summary>
    private static void MoveAside(string path, Action<string> log, string reason)
    {
        var bad = path + ".bad";
        File.Move(path, bad, overwrite: true);
        log($"ui.json cannot be read ({reason}); it was moved to {bad}, and a new one is started.");
    }
}

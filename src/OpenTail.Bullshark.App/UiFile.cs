using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.App;

/// <summary>
/// ui.json, beside the state file: the rail theme (D-S30) and the zoom of the session text (zoom.md). Each save changes one key and
/// keeps the others. A file that cannot be read gives the defaults. A file that exists but cannot be parsed is not overwritten, so
/// nothing in it is lost; the save is logged instead.
/// </summary>
public static class UiFile
{
    public static string PathFor(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "ui.json");

    /// <summary>The value of one key, or null when the file or the key is missing, or the file cannot be read.</summary>
    public static JsonNode? Read(string stateFile, string key)
    {
        try
        {
            return ReadRoot(PathFor(stateFile))?[key];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write($"ui.json not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sets one key and writes the file, keeping the other keys.</summary>
    public static void Set(string stateFile, string key, JsonNode value)
    {
        try
        {
            var path = PathFor(stateFile);
            var root = ReadRoot(path) ?? new JsonObject();
            root[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write($"ui.json not saved: {ex.Message}");
        }
    }

    /// <summary>The file as an object: null when there is no file. Throws when the file exists and is not a JSON object.</summary>
    private static JsonObject? ReadRoot(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("ui.json is not a JSON object");
    }
}

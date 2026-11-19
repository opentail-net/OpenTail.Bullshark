using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The optional attention file for other tools (firstrun batch B9): <c>attention.json</c> beside the state file, written on each pass when ui.json says
/// <c>attentionFile: true</c>. It is off unless the owner turns it on. It holds the reason codes and the session names of the items that wait for the
/// owner, never their detail text. A reader must check <c>at</c>: the file is deleted on a clean close, but a crash leaves it. Nothing is sent over a
/// network, and nothing reads it back.
/// </summary>
public static class AttentionFile
{
    /// <summary>The file name, beside the state file.</summary>
    public const string FileName = "attention.json";

    /// <summary>The ui.json key of the switch. Missing, or anything but true, means off.</summary>
    public const string Key = "attentionFile";

    /// <summary>True only when ui.json holds the boolean true for the switch.</summary>
    public static bool Enabled(JsonNode? value) =>
        value is JsonValue json && json.TryGetValue<bool>(out var on) && on;

    /// <summary>
    /// The file's text: the time of the pass in UTC, the number of items, and the items in their order, each with its reason code (the enum name)
    /// and its session name. The detail text of an item is never written.
    /// </summary>
    public static string Build(DateTimeOffset at, IReadOnlyList<AttentionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var root = new JsonObject
        {
            ["at"] = at.ToUniversalTime().ToString("O"),
            ["count"] = items.Count,
            ["items"] = new JsonArray(items.Select(item => (JsonNode)new JsonObject
            {
                ["reason"] = item.Reason.ToString(),
                ["name"] = item.Name,
            }).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Writes the text atomically (<see cref="FileLock.WriteAtomic"/>): a reader never sees half a file. The write throws the file errors.</summary>
    public static void Write(string path, string text) => FileLock.WriteAtomic(path, text);

    /// <summary>Deletes the file when it exists. A file that cannot be deleted is left: the next pass that finds it off deletes it again.</summary>
    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left as it is; the reader checks "at" in any case.
        }
    }
}

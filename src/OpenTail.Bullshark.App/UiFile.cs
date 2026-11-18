using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// ui.json for the window (the rail theme, the zoom and the notify setting). The file, its lock, its atomic write and its recovery are in
/// <see cref="UiSettingsFile"/>; the lines it logs go to the app log. A file that cannot be read gives null, and a save that fails is logged.
/// </summary>
public static class UiFile
{
    public static string PathFor(string stateFile) => UiSettingsFile.PathFor(stateFile);

    /// <summary>The value of one key, or null when the file or the key is missing, or the file cannot be read.</summary>
    public static JsonNode? Read(string stateFile, string key) => UiSettingsFile.Read(PathFor(stateFile), key, AppLog.Write);

    /// <summary>Sets one key and writes the file, keeping the other keys. False when the file was not written (the failure is logged).</summary>
    public static bool Set(string stateFile, string key, JsonNode value) => UiSettingsFile.Set(PathFor(stateFile), key, value, AppLog.Write);
}

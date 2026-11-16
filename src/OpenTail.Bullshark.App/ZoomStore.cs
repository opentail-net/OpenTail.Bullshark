using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Zoom;

namespace OpenTail.Bullshark.App;

/// <summary>The zoom of the session text, saved in ui.json as a percent (zoom.md). A missing or unreadable value gives 100.</summary>
public static class ZoomStore
{
    public const string Key = "zoomPercent";

    public static int Load(string stateFile) =>
        UiFile.Read(stateFile, Key) is JsonValue value && value.TryGetValue<int>(out var percent)
            ? ZoomLevel.Nearest(percent)
            : ZoomLevel.Default;

    public static void Save(string stateFile, int percent) =>
        UiFile.Set(stateFile, Key, JsonValue.Create(ZoomLevel.Nearest(percent))!);
}

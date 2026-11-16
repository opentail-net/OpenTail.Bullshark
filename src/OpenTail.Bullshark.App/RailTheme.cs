using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace OpenTail.Bullshark.App;

/// <summary>The rail's theme. Dark is near black; light is a very light grey with a touch of blue (owner, 2026-11-15).</summary>
public enum RailTheme
{
    Dark,
    Light,
}

/// <summary>Every colour the rail uses. Set from one palette, so the text, the background and the selection always agree.</summary>
public sealed record RailPalette(string Panel, string Background, string Text, string Muted, string Selection, string SelectionText, string Border)
{
    public static readonly RailPalette DarkPalette = new("#0C0D10", "#111316", "#E6E6E6", "#9AA3AE", "#2B3A4F", "#FFFFFF", "#2A2F38");
    public static readonly RailPalette LightPalette = new("#E9EEF6", "#F4F6FA", "#1E2430", "#5A6270", "#D6E4F7", "#101418", "#C7CFDB");

    public static RailPalette For(RailTheme theme) => theme == RailTheme.Light ? LightPalette : DarkPalette;

    /// <summary>True for the light palette. The views that colour text by kind use it to pick colours that read on a light background.</summary>
    public bool IsLight => this == LightPalette;

    /// <summary>
    /// The green of a button that launches a session: Start on the setup page and Resume in the resume prompt (owner, 2026-11-16).
    /// The dark theme's green is a little deeper, so it does not glare on near black; the light theme's is a little brighter.
    /// White text reads on both. Hover is a lighter shade and press a darker one.
    /// </summary>
    public string Go => IsLight ? "#23803F" : "#2E7D46";

    public string GoHover => IsLight ? "#2A9449" : "#389655";

    public string GoPressed => IsLight ? "#1C6A33" : "#256639";

    public string GoText => "#FFFFFF";

    /// <summary>
    /// The eight colours a session may have (session library, D-L11): a name, a shade for the dark theme and one for the light.
    /// None of them is green, amber or grey: those are the tab's state dots (working, blocked, other).
    /// </summary>
    public static readonly (string Name, string Dark, string Light)[] SessionColours =
    [
        ("Red", "#E5534B", "#B83227"),
        ("Pink", "#E86AA6", "#B03B76"),
        ("Violet", "#A97BE8", "#6E43B8"),
        ("Indigo", "#7C86E8", "#3F4AAE"),
        ("Blue", "#4DA3FF", "#1B66C9"),
        ("Cyan", "#4DD0E1", "#0C7F93"),
        ("Brown", "#C08A5E", "#7A4C2A"),
        ("Magenta", "#E05BE0", "#9E22A0"),
    ];

    /// <summary>The shade of a session colour for this palette's theme, or null for no colour or an unknown name.</summary>
    public string? ColourHex(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var (colour, dark, light) in SessionColours)
        {
            if (string.Equals(colour, name, StringComparison.Ordinal)) return IsLight ? light : dark;
        }
        return null;
    }

    public static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>
/// Remembers the rail theme in ui.json, beside the state file, so the choice survives a restart. A file that cannot be read
/// gives the default; a file that cannot be written is logged.
/// </summary>
public static class ThemeStore
{
    public static RailTheme Load(string stateFile)
    {
        try
        {
            var path = PathFor(stateFile);
            if (!File.Exists(path)) return RailTheme.Dark;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("railTheme", out var value) && value.GetString() == nameof(RailTheme.Light)
                ? RailTheme.Light
                : RailTheme.Dark;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write($"rail theme not read: {ex.Message}");
            return RailTheme.Dark;
        }
    }

    public static void Save(string stateFile, RailTheme theme)
    {
        try
        {
            // Only the theme key changes; the zoom and any other key in ui.json stay as they are.
            UiFile.Set(stateFile, "railTheme", System.Text.Json.Nodes.JsonValue.Create(theme.ToString())!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"rail theme not saved: {ex.Message}");
        }
    }

    private static string PathFor(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "ui.json");
}

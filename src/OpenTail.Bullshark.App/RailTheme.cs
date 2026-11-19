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

    // The look of buttons and menus (Styles/Controls.xaml, 2026-11-18). A raised face one step off the panel, a brighter border on hover,
    // and one accent colour for focus rings and the checked state.
    public string Face => IsLight ? "#FFFFFF" : "#1C2027";

    public string FaceHover => IsLight ? "#EAF0FA" : "#262C36";

    public string FacePressed => IsLight ? "#DCE4F1" : "#15181D";

    /// <summary>
    /// The edge of a control (the buttons, the boxes, the combo boxes, the checks): UiBorder. Separate from <see cref="Border"/>, which draws the
    /// rail's lines and the setup page's rules. Contrast against the background (P7, computed in docs/plans/ui-polish-batch-2026-11-19.md):
    /// light 3.19:1 (the 3:1 of a component's boundary), dark 2.47:1 (raised from 1.38:1, held modest so the dark theme stays quiet).
    /// </summary>
    public string Edge => IsLight ? "#7F8B9D" : "#4A5566";

    /// <summary>The edge under the pointer. Dark 3.26:1 against the background, so the hover still shows against the raised edge.</summary>
    public string BorderHover => IsLight ? "#5E6B80" : "#5D6779";

    public string Accent => IsLight ? "#2563EB" : "#5B9DF5";

    public string AccentText => IsLight ? "#FFFFFF" : "#08111F";

    public string GhostHover => IsLight ? "#1F000000" : "#33FFFFFF";

    public string GhostPressed => IsLight ? "#33000000" : "#22FFFFFF";

    public string MenuBg => IsLight ? "#FFFFFF" : "#171A20";

    /// <summary>The warning colour: a label to check (the Changes tab's big-deletion flags, features batch G2). The setup check's warning colours.</summary>
    /// <summary>Light 4.85:1 on the light panels (was 4.34:1 at #9A6B00, under the 4.5:1 for small text). Dark 9.5:1 on the background.</summary>
    public string Warn => IsLight ? "#8A5F00" : "#E0B341";

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

    /// <summary>
    /// The frozen brushes made so far, by hex (round 12 B5). A diff asks for one brush per line, so an unfrozen brush per call was an allocation
    /// per line. The keys are the palette's colours and the terminal's, a small fixed set, so the cache does not grow. Frozen: no caller changes a
    /// brush it got (checked in round 12), and a frozen brush throws if one tries.
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> FrozenByHex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A frozen brush of the colour. The same hex gives the same brush; a bad hex throws, as the converter does.</summary>
    public static SolidColorBrush Brush(string hex)
    {
        lock (FrozenByHex)
        {
            if (FrozenByHex.TryGetValue(hex, out var brush)) return brush;
            brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            FrozenByHex[hex] = brush;
            return brush;
        }
    }
}

/// <summary>
/// Remembers the rail theme in ui.json, beside the state file, so the choice survives a restart. A file that cannot be read
/// gives the default; a file that cannot be written is logged.
/// </summary>
public static class ThemeStore
{
    /// <summary>The saved theme, read through ui.json's own reader (locked, and a damaged file is moved aside). Dark when there is none.</summary>
    public static RailTheme Load(string stateFile) =>
        UiFile.Read(stateFile, "railTheme") is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var name) &&
        name == nameof(RailTheme.Light)
            ? RailTheme.Light
            : RailTheme.Dark;

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
}

/// <summary>
/// Puts the palette into the application's resources, for the shared button and menu styles (Styles/Controls.xaml). Called whenever a window
/// applies its theme, so one call restyles every button in every window. Replacing a brush updates all controls that use it.
/// </summary>
public static class UiTheme
{
    public static void Apply(RailPalette palette)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null) return;
        resources["UiText"] = RailPalette.Brush(palette.Text);
        resources["UiMuted"] = RailPalette.Brush(palette.Muted);
        resources["UiFace"] = RailPalette.Brush(palette.Face);
        resources["UiFaceHover"] = RailPalette.Brush(palette.FaceHover);
        resources["UiFacePressed"] = RailPalette.Brush(palette.FacePressed);
        resources["UiBorder"] = RailPalette.Brush(palette.Edge);
        resources["UiBorderHover"] = RailPalette.Brush(palette.BorderHover);
        resources["UiAccent"] = RailPalette.Brush(palette.Accent);
        resources["UiAccentSoft"] = RailPalette.Brush(palette.Selection);
        resources["UiAccentText"] = RailPalette.Brush(palette.AccentText);
        resources["UiGhostHover"] = RailPalette.Brush(palette.GhostHover);
        resources["UiGhostPressed"] = RailPalette.Brush(palette.GhostPressed);
        resources["UiGo"] = RailPalette.Brush(palette.Go);
        resources["UiGoHover"] = RailPalette.Brush(palette.GoHover);
        resources["UiGoPressed"] = RailPalette.Brush(palette.GoPressed);
        resources["UiGoText"] = RailPalette.Brush(palette.GoText);
        resources["UiMenuBg"] = RailPalette.Brush(palette.MenuBg);
        // The scroll bar's thumb is the muted colour, faint at rest and stronger under the pointer (it was set per window before P3).
        resources["ScrollThumb"] = RailPalette.Brush("#55" + palette.Muted.TrimStart('#'));
        resources["ScrollThumbHover"] = RailPalette.Brush("#99" + palette.Muted.TrimStart('#'));
    }
}

/// <summary>A button's content with a small icon before the text (Segoe Fluent Icons, falling back to Segoe MDL2 Assets). The icon is a hint, not the label.</summary>
public static class UiIcon
{
    public const string Refresh = "";
    public const string Copy = "";
    public const string Check = "";
    public const string Play = "";
    public const string Folder = "";
    public const string Archive = "";
    public const string Restore = "";
    public const string Save = "";

    public static System.Windows.UIElement Label(string glyph, string text)
    {
        var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 13,
            Margin = new System.Windows.Thickness(0, 1, 7, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });
        row.Children.Add(new System.Windows.Controls.TextBlock { Text = text, VerticalAlignment = System.Windows.VerticalAlignment.Center });
        return row;
    }
}

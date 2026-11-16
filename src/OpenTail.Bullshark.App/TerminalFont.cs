using System.Windows.Media;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The font of the embedded terminals, for the views that should look like one (the transcript of a past session). The family
/// and the size are read from the terminal control's own defaults, so the two stay alike if the control changes them. The app
/// does not set a font on its terminals, so the defaults are what the terminals use.
/// </summary>
public static class TerminalFont
{
    private const string FallbackFamily = "Cascadia Code";
    private const double FallbackPoints = 12;

    /// <summary>The terminal's font, then fonts close to it, for a PC that lacks the first.</summary>
    public static FontFamily Family { get; } = new(FamilyName() + ", Cascadia Mono, Consolas, Courier New");

    /// <summary>The terminal's size in WPF units. The terminal counts in points; WPF counts in 1/96 inch.</summary>
    public static double Size { get; } = Points() * 96.0 / 72.0;

    private static string FamilyName()
    {
        try
        {
            return ExtendedTerminalControl.FontFamilyWhenSettingThemeProperty.DefaultMetadata.DefaultValue switch
            {
                string name when name.Length > 0 => name,
                FontFamily family => family.Source,
                _ => FallbackFamily,
            };
        }
        catch (Exception)
        {
            return FallbackFamily;
        }
    }

    private static double Points()
    {
        try
        {
            var value = ExtendedTerminalControl.FontSizeWhenSettingThemeProperty.DefaultMetadata.DefaultValue;
            var points = value is IConvertible number ? number.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : 0;
            return points is >= 6 and <= 40 ? points : FallbackPoints;
        }
        catch (Exception)
        {
            return FallbackPoints;
        }
    }
}

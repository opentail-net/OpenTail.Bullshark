using Microsoft.Terminal.Wpf;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The colours a terminal is given with its font size (zoom.md). The library draws a terminal's font size only when the terminal has a
/// theme, and a theme without a colour table draws every ANSI colour black (checked 2026-11-16). So the theme carries the Windows Terminal
/// default palette: Campbell for the first 16 colours, then the xterm 256-colour cube and greys. The library reads each colour as
/// 0x00BBGGRR: with 0x00RRGGBB, red came out blue (checked 2026-11-16), so the values are converted here.
/// </summary>
internal static class TerminalColours
{
    private static readonly uint[] Campbell =
    [
        0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
        0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2,
    ];

    private static readonly uint[] CubeLevels = [0x00, 0x5F, 0x87, 0xAF, 0xD7, 0xFF];

    /// <summary>One of the first 16 colours (0 black to 15 bright white) as "#RRGGBB", for text drawn outside a terminal (the Changes tab).</summary>
    internal static string Hex(int index) => $"#{Campbell[index]:X6}";

    /// <summary>A new theme with the default background, foreground, selection and palette.</summary>
    public static TerminalTheme Create() => new()
    {
        DefaultBackground = Bgr(0x0C0C0C),
        DefaultForeground = Bgr(0xCCCCCC),
        DefaultSelectionBackground = Bgr(0x264F78),
        ColorTable = Palette(),
    };

    private static uint[] Palette()
    {
        var table = new uint[256];
        for (var i = 0; i < 16; i++) table[i] = Bgr(Campbell[i]);
        for (var i = 16; i < 232; i++)
        {
            var n = (uint)(i - 16);
            table[i] = Bgr((CubeLevels[n / 36] << 16) | (CubeLevels[n / 6 % 6] << 8) | CubeLevels[n % 6]);
        }
        for (var i = 232; i < 256; i++)
        {
            var grey = (uint)(8 + 10 * (i - 232));
            table[i] = Bgr((grey << 16) | (grey << 8) | grey);
        }
        return table;
    }

    /// <summary>0x00RRGGBB to the library's 0x00BBGGRR.</summary>
    private static uint Bgr(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
}

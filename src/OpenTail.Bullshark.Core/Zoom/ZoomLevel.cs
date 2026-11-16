namespace OpenTail.Bullshark.Core.Zoom;

/// <summary>
/// The zoom of the session transcripts, in percent. The steps are fixed, as in Word's status bar, so the slider, the buttons and the
/// keys all move one step at a time. The default is 100. (The terminals do not take a zoom yet; zoom.md.)
/// </summary>
public static class ZoomLevel
{
    public const int Default = 100;

    /// <summary>The steps in percent, smallest first.</summary>
    public static IReadOnlyList<int> Steps { get; } = [50, 75, 90, 100, 110, 125, 150, 175, 200, 250, 300];

    /// <summary>The step nearest to a percent. A tie goes to the smaller step, so 105 gives 100.</summary>
    public static int Nearest(int percent)
    {
        var best = Steps[0];
        foreach (var step in Steps)
        {
            if (Math.Abs(step - percent) < Math.Abs(best - percent)) best = step;
        }
        return best;
    }

    /// <summary>The position of the nearest step, for the slider.</summary>
    public static int IndexOf(int percent)
    {
        var nearest = Nearest(percent);
        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i] == nearest) return i;
        }
        return 0;
    }

    /// <summary>The next step up, or the same step at the top.</summary>
    public static int Up(int percent) => Steps[Math.Min(IndexOf(percent) + 1, Steps.Count - 1)];

    /// <summary>The next step down, or the same step at the bottom.</summary>
    public static int Down(int percent) => Steps[Math.Max(IndexOf(percent) - 1, 0)];

    /// <summary>The step at a slider position; a position outside the steps is moved to the nearest end.</summary>
    public static int AtIndex(int index) => Steps[Math.Clamp(index, 0, Steps.Count - 1)];

    /// <summary>The scale of a percent, as a factor: 1.0 at 100.</summary>
    public static double Scale(int percent) => Nearest(percent) / 100.0;
}

/// <summary>A zoom key: Ctrl with plus, minus or zero.</summary>
public enum ZoomKey
{
    None,
    In,
    Out,
    Reset,
}

/// <summary>
/// Reads the zoom keys from Windows virtual-key codes, as WPF gives them (KeyInterop.VirtualKeyFromKey). Only the window reads them:
/// the window's key events still arrive while a terminal has the keyboard (checked 2026-11-16).
/// </summary>
public static class ZoomKeys
{
    public const int VkD0 = 0x30;
    public const int VkNumpad0 = 0x60;
    public const int VkAdd = 0x6B;
    public const int VkSubtract = 0x6D;
    public const int VkOemPlus = 0xBB;
    public const int VkOemMinus = 0xBD;

    /// <summary>The zoom key for a virtual key pressed with Ctrl held; None for any other key.</summary>
    public static ZoomKey FromVirtualKey(int virtualKey, bool ctrl)
    {
        if (!ctrl) return ZoomKey.None;
        return virtualKey switch
        {
            VkOemPlus or VkAdd => ZoomKey.In,
            VkOemMinus or VkSubtract => ZoomKey.Out,
            VkD0 or VkNumpad0 => ZoomKey.Reset,
            _ => ZoomKey.None,
        };
    }
}

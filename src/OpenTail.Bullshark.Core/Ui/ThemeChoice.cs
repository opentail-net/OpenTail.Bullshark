namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// Which rail theme the window starts with (firstrun batch B6). A choice the owner saved in ui.json (<c>railTheme</c>) always wins. With none saved,
/// the window follows Windows' app theme (<c>AppsUseLightTheme</c>: 1 light, 0 dark). When Windows' value cannot be read, the theme is dark, as it was
/// before. Nothing here writes ui.json: the owner's own choice is the one that is saved.
/// </summary>
public static class ThemeChoice
{
    /// <summary>The ui.json key of the saved rail theme.</summary>
    public const string Key = "railTheme";

    /// <summary>The saved name of the light theme.</summary>
    public const string LightName = "Light";

    /// <summary>The saved name of the dark theme.</summary>
    public const string DarkName = "Dark";

    /// <summary>
    /// True when the rail is light. <paramref name="savedName"/> is the ui.json value, or null when there is none (or it is not a theme name); it
    /// decides first. Otherwise <paramref name="appsUseLightTheme"/> decides: 1 is light, and 0, another value, or null (unreadable) is dark.
    /// </summary>
    public static bool IsLight(string? savedName, int? appsUseLightTheme)
    {
        if (savedName == LightName) return true;
        if (savedName == DarkName) return false;
        return appsUseLightTheme == 1;
    }
}

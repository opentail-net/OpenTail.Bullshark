using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>The rail theme at start (firstrun batch B6): a saved choice wins; with none, Windows' app theme; unreadable is dark.</summary>
public sealed class ThemeChoiceTests
{
    [Fact]
    public void ASavedLightWinsOverWindowsDark()
    {
        Assert.True(ThemeChoice.IsLight(ThemeChoice.LightName, appsUseLightTheme: 0));
    }

    [Fact]
    public void ASavedDarkWinsOverWindowsLight()
    {
        Assert.False(ThemeChoice.IsLight(ThemeChoice.DarkName, appsUseLightTheme: 1));
    }

    [Fact]
    public void WithNoSavedChoiceWindowsLightGivesLight()
    {
        Assert.True(ThemeChoice.IsLight(null, appsUseLightTheme: 1));
    }

    [Fact]
    public void WithNoSavedChoiceWindowsDarkGivesDark()
    {
        Assert.False(ThemeChoice.IsLight(null, appsUseLightTheme: 0));
    }

    [Fact]
    public void WithNoSavedChoiceAnUnreadableWindowsValueGivesDarkAsBefore()
    {
        Assert.False(ThemeChoice.IsLight(null, appsUseLightTheme: null));
    }

    [Fact]
    public void AnUnknownSavedNameIsNoChoiceSoWindowsDecides()
    {
        Assert.True(ThemeChoice.IsLight("Blue", appsUseLightTheme: 1));
        Assert.False(ThemeChoice.IsLight("Blue", appsUseLightTheme: null));
    }

    [Fact]
    public void TheKeyAndTheNamesAreTheOnesTheWindowSaves()
    {
        Assert.Equal("railTheme", ThemeChoice.Key);
        Assert.Equal("Light", ThemeChoice.LightName);
        Assert.Equal("Dark", ThemeChoice.DarkName);
    }
}

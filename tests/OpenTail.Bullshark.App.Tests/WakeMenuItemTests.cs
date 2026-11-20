using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The row menu's "Wake after the limit resets" item (limit wake, 2026-11-20), headless: the item is built the way the rows build it, and a click on it
/// passes the new value on. The test writes no library and starts nothing; the window's own write is a call the item makes, which is not made here.
/// WPF objects are read on the WPF thread only.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WakeMenuItemTests
{
    [Fact]
    public void TheItemIsCheckableAndShowsTheOwnersChoice()
    {
        var item = UiThread.Run(() => WakeMenuItem.Create(on: true, change: _ => { }));

        var header = UiThread.Run(() => (string)item.Header);
        var checkable = UiThread.Run(() => item.IsCheckable);
        var isChecked = UiThread.Run(() => item.IsChecked == true);
        var tip = UiThread.Run(() => (string)item.ToolTip!);

        Assert.Equal(WakeMenuItem.Header, header);
        Assert.True(checkable);
        Assert.True(isChecked);
        Assert.Equal(WakeMenuItem.Tip, tip);
    }

    [Fact]
    public void AClickPassesTheOppositeValueOn()
    {
        var passed = new List<bool>();
        var item = UiThread.Run(() => WakeMenuItem.Create(on: false, change: value => passed.Add(value)));

        UiThread.Run(() => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));

        Assert.Equal(new[] { true }, passed.ToArray());
    }

    [Fact]
    public void AClickOnAnOnItemTurnsItOff()
    {
        var passed = new List<bool>();
        var item = UiThread.Run(() => WakeMenuItem.Create(on: true, change: value => passed.Add(value)));

        UiThread.Run(() => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));

        Assert.Equal(new[] { false }, passed.ToArray());
    }
}

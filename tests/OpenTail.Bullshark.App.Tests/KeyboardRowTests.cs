using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// A clicked row's keyboard reach (perf and testability batch, Q7): it takes the keyboard, it has a name for a screen reader, and Enter or Space runs
/// the action a click runs.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class KeyboardRowTests
{
    private static KeyEventArgs KeyDown(Key key, UIElement target)
    {
        var source = PresentationSource.FromVisual(target) ?? new HwndSource(new HwndSourceParameters("bullshark-test"));
        return new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
    }

    [Fact]
    public void ARowCanTakeTheKeyboardAndHasItsName()
    {
        UiThread.Run(() =>
        {
            var row = KeyboardRow.Wrap(new TextBlock { Text = "Session" }, RailPalette.DarkPalette, "Open the session", () => { });

            Assert.True(row.Focusable);
            Assert.Equal("Open the session", AutomationProperties.GetName(row));
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void EnterOrSpaceRunsTheAction(Key key)
    {
        UiThread.Run(() =>
        {
            var ran = 0;
            var row = KeyboardRow.Wrap(new TextBlock(), RailPalette.DarkPalette, "row", () => ran++);

            row.RaiseEvent(KeyDown(key, row));

            Assert.Equal(1, ran);
        });
    }

    [Fact]
    public void OtherKeysDoNothing()
    {
        UiThread.Run(() =>
        {
            var ran = 0;
            var row = KeyboardRow.Wrap(new TextBlock(), RailPalette.DarkPalette, "row", () => ran++);

            row.RaiseEvent(KeyDown(Key.A, row));

            Assert.Equal(0, ran);
        });
    }
}

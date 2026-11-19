using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Keyboard reach for a row that is clicked (keyboard batch T2). The row takes the keyboard, so Tab reaches it in the order it is laid out; it draws the
/// accent edge while it has the keyboard; Enter or Space runs the action a click runs; and it has a name for a screen reader. The content keeps its own
/// mouse handlers, so the mouse does what it did.
/// </summary>
internal static class KeyboardRow
{
    /// <summary>Wraps the content of a clickable row. <paramref name="name"/> is what a screen reader says for it.</summary>
    public static Border Wrap(UIElement content, RailPalette palette, string name, Action activate)
    {
        var row = new Border
        {
            Child = content,
            Focusable = true,
            FocusVisualStyle = null,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
        };
        AutomationProperties.SetName(row, name);
        row.IsKeyboardFocusedChanged += (_, _) => row.BorderBrush = row.IsKeyboardFocused ? RailPalette.Brush(palette.Accent) : Brushes.Transparent;
        row.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            activate();
        };
        return row;
    }
}

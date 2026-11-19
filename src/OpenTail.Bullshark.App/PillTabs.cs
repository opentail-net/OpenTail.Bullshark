using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace OpenTail.Bullshark.App;

/// <summary>
/// A row of pills, one of them chosen: the sub-tabs of Insights and Settings, and the choices on Settings (Insights and Settings plan).
/// Text only, drawn by the PillToggle style from the Ui brushes, so it follows the theme. Left and Right move the keyboard focus along
/// the row; Enter and Space choose the focused pill. <see cref="Chosen"/> is raised for a choice the owner makes; <see cref="Select"/> and
/// <see cref="SetItems"/> change the pill that is chosen without raising it.
/// </summary>
public sealed class PillTabs : UserControl
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<ToggleButton> _pills = [];
    private List<string> _headers = [];
    private int _selected = -1;

    public PillTabs()
    {
        Content = _row;
        Focusable = false;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>The index of the pill that is chosen, or -1 when none is.</summary>
    public int Selected => _selected;

    /// <summary>Raised when the owner chooses a pill (a click, Enter or Space). The index is the pill's place in the row.</summary>
    public event Action<int>? Chosen;

    /// <summary>
    /// Sets the pills. The row is rebuilt only when the labels change, so a pill that has the keyboard keeps it when the same choices are drawn again.
    /// </summary>
    public void SetItems(IReadOnlyList<string> headers, int selected)
    {
        if (!_headers.SequenceEqual(headers))
        {
            _headers = headers.ToList();
            _row.Children.Clear();
            _pills.Clear();
            var style = (Style)FindResource("PillToggle");
            for (var i = 0; i < _headers.Count; i++)
            {
                var index = i;
                var pill = new ToggleButton
                {
                    Content = _headers[i],
                    Style = style,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                pill.Click += (_, _) => Choose(index);
                _pills.Add(pill);
                _row.Children.Add(pill);
            }
        }
        Select(selected);
    }

    /// <summary>Chooses the pill at <paramref name="index"/> without raising <see cref="Chosen"/>. -1 or an index out of range chooses none.</summary>
    public void Select(int index)
    {
        _selected = index >= 0 && index < _pills.Count ? index : -1;
        // One tab stop for the row: the chosen pill, or the first when none is chosen. Left and Right reach the others (P5, 2026-11-19).
        var stop = _selected >= 0 ? _selected : 0;
        for (var i = 0; i < _pills.Count; i++)
        {
            _pills[i].IsChecked = i == _selected;
            _pills[i].IsTabStop = i == stop;
        }
    }

    private void Choose(int index)
    {
        Select(index);
        Chosen?.Invoke(index);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focused = _pills.FindIndex(pill => pill.IsKeyboardFocusWithin);
        if (focused < 0) return;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                var step = e.Key == Key.Right ? 1 : -1;
                _pills[Math.Clamp(focused + step, 0, _pills.Count - 1)].Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                Choose(focused);
                e.Handled = true;
                break;
        }
    }
}

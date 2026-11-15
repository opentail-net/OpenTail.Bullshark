using System.Windows;
using System.Windows.Input;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Hides the sessions rail completely, so the sessions take the whole window, and shows it again. The toggle sits at the left of the
/// tab bar, which is always shown, so the rail can always be brought back with one click. Collapsing keeps the rail's width and the
/// splitter's, so the rail comes back the size it was, including a size set by dragging the splitter. The state lasts for this run
/// of the window; it is not saved.
/// </summary>
public partial class SessionWindow
{
    private GridLength _railWidth = new(0.3, GridUnitType.Star);
    private double _railMinWidth = 180;
    private GridLength _splitterWidth = new(5);
    private bool _railCollapsed;

    private void RailToggle_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_railCollapsed) ExpandRail();
        else CollapseRail();
    }

    private void CollapseRail()
    {
        _railWidth = RailColumn.Width;
        _railMinWidth = RailColumn.MinWidth;
        _splitterWidth = SplitterColumn.Width;

        RailColumn.MinWidth = 0;
        RailColumn.Width = new GridLength(0);
        SplitterColumn.Width = new GridLength(0);
        RailSplitter.Visibility = Visibility.Collapsed;
        RailPanel.Visibility = Visibility.Collapsed;

        _railCollapsed = true;
        UpdateRailToggle();
    }

    private void ExpandRail()
    {
        RailColumn.MinWidth = _railMinWidth;
        RailColumn.Width = _railWidth;
        SplitterColumn.Width = _splitterWidth;
        RailSplitter.Visibility = Visibility.Visible;
        RailPanel.Visibility = Visibility.Visible;

        _railCollapsed = false;
        UpdateRailToggle();
    }

    /// <summary>The toggle shows the arrow for the action it takes: ◀ hides the rail, ▶ shows it.</summary>
    private void UpdateRailToggle()
    {
        RailToggle.Text = _railCollapsed ? "▶" : "◀";
        RailToggle.ToolTip = _railCollapsed ? "Show the sessions rail" : "Hide the sessions rail";
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Tabs;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Dragging a tab (docs/plans/window.md, T1 and T2). A press and release without moving is still a click. Once the pointer
/// has moved a few pixels the tab is being dragged. Along its strip it changes places with a tab when the pointer passes that tab's
/// middle; "+" stays last. Released over the pane, below the strips, a session tab moves to the pane on that side: the right half
/// opens the split. The order of the session tabs, and which are at the right, are saved beside the state file. Released outside the window, a session tab opens its session in its own window (T3).
/// </summary>
public partial class SessionWindow
{
    /// <summary>How far the pointer must move, in pixels, before a press becomes a drag.</summary>
    private const double DragThreshold = 6;

    /// <summary>The mark on a tab's ✕, so a press on it never starts a drag.</summary>
    private const string CloseMark = "close";

    private OpenTab? _dragTab;
    private StackPanel? _dragStrip;
    private Point _dragStart;
    private bool _dragging;
    private bool _dragMoved;
    private IReadOnlyList<string> _savedTabOrder = [];

    private string TabOrderPath => TabOrder.PathBeside(_options.StatePath);

    private void InitTabDrag()
    {
        var layout = TabOrder.Load(TabOrderPath, AppLog.Write);
        // Both forms are read: a bare id of the older file, and a claude:<id> key. The Claude ids are kept (C-D5).
        _savedTabOrder = TabLayoutFile.ClaudeIds(layout.Order);
        _savedRight = TabLayoutFile.ClaudeIds(layout.Right);
        foreach (var strip in new[] { TabStrip, TabStripB })
        {
            strip.PreviewMouseLeftButtonDown += TabStrip_PreviewMouseLeftButtonDown;
            strip.PreviewMouseMove += TabStrip_PreviewMouseMove;
            strip.PreviewMouseLeftButtonUp += TabStrip_PreviewMouseLeftButtonUp;
            strip.LostMouseCapture += (_, _) => EndTabDrag();
        }
    }

    private void TabStrip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragTab = null;
        if (e.OriginalSource is FrameworkElement { Tag: CloseMark }) return;
        _dragStrip = (StackPanel)sender;
        _dragTab = TabUnder(e.OriginalSource as DependencyObject);
        _dragStart = e.GetPosition(_dragStrip);
    }

    private void TabStrip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || _dragStrip is null || e.LeftButton != MouseButtonState.Pressed) return;
        var strip = _dragStrip;
        var at = e.GetPosition(strip);

        if (!_dragging)
        {
            if (Math.Abs(at.X - _dragStart.X) < DragThreshold && Math.Abs(at.Y - _dragStart.Y) < DragThreshold) return;
            _dragging = true;
            _dragMoved = false;
            // The strip takes the mouse for the length of the drag. The strip is rebuilt on every move, so a tab cannot hold it.
            strip.CaptureMouse();
            RefreshTabStrip();
        }

        // Dragged out of the window: a release opens the session in its own window. Nothing is reordered while it is outside.
        if (_dragTab.Kind == TabKind.Session && IsOutsideWindow(e))
        {
            SetStatus($"Release to open {_dragTab.Title} in its own window.");
            return;
        }

        // Below the strips the tab is over a pane: the status line says what a release would do. Nothing can be drawn over a terminal.
        if (DropPane(e) is { } toRight)
        {
            if (_dragTab.Kind != TabKind.Session) SetStatus("Only a session's tab can go to the right pane.");
            else if (toRight != IsRight(_dragTab)) SetStatus($"Release to move {_dragTab.Title} to the {(toRight ? "right" : "left")} pane.");
            return;
        }

        var borders = strip.Children.OfType<Border>().Where(b => b.Tag is OpenTab).ToList();
        var spans = borders
            .Select(b =>
            {
                var left = b.TranslatePoint(new Point(0, 0), strip).X;
                return (Left: left, Right: left + b.ActualWidth);
            })
            .ToList();
        var from = borders.FindIndex(b => ReferenceEquals(b.Tag, _dragTab));
        var to = TabOrder.DropPlace(spans, from, at.X);
        if (to < 0) return;

        // The strip shows the tabs of one pane, in the order of _tabs. The dragged tab takes the other tab's place in _tabs.
        var target = (OpenTab)borders[to].Tag;
        if (!TabOrder.Move(_tabs, _tabs.IndexOf(_dragTab), _tabs.IndexOf(target))) return;
        _dragMoved = true;
        RefreshTabStrip();
        // The new places are needed before the next move is judged.
        strip.UpdateLayout();
    }

    private void TabStrip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || _dragStrip is null)
        {
            _dragTab = null;
            return;
        }

        // The release ends the drag. It is not a click: the tab under the pointer is not activated by it.
        e.Handled = true;
        var tab = _dragTab;
        var toRight = DropPane(e);
        _dragStrip.ReleaseMouseCapture();

        if (tab is not null && tab.Kind == TabKind.Session && IsOutsideWindow(e))
        {
            PopOut(tab);
            return;
        }

        if (tab is null || tab.Kind != TabKind.Session || toRight is null || toRight == IsRight(tab)) return;
        if (toRight.Value) MoveToRight(tab);
        else MoveToLeft(tab);
    }

    /// <summary>
    /// The pane the pointer is over: true for the right, false for the left, null when it is not over a pane (for example over
    /// a strip, or outside the window). With no split yet, the right half of the pane counts as the right pane.
    /// </summary>
    private bool? DropPane(MouseEventArgs e)
    {
        if (Pane.Parent is not FrameworkElement area) return null;
        var at = e.GetPosition(area);
        if (at.X < 0 || at.X > area.ActualWidth || at.Y <= TabBar.ActualHeight || at.Y > area.ActualHeight) return null;
        var edge = SplitOpen ? PaneB.TranslatePoint(new Point(0, 0), area).X : area.ActualWidth / 2;
        return at.X >= edge;
    }

    /// <summary>True when the pointer is outside the window: the drag has taken the tab out of it.</summary>
    private bool IsOutsideWindow(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        return at.X < 0 || at.Y < 0 || at.X > ActualWidth || at.Y > ActualHeight;
    }

    /// <summary>Ends a drag, however it ended, and saves the order when a tab moved.</summary>
    private void EndTabDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragTab = null;
        _dragStrip = null;
        if (_dragMoved) SaveTabLayout();
        RefreshTabStrip();
    }

    /// <summary>The tab whose border holds the element, or null when the element is not in a tab (for example "+").</summary>
    private static OpenTab? TabUnder(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Border { Tag: OpenTab tab }) return tab;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }
}

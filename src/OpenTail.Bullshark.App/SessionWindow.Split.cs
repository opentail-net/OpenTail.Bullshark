using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Tabs;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The split (docs/plans/tabs-and-panes.md, T2): a second pane at the right, so two sessions are in view at once, for example the
/// planner at the left and the coder at the right. Only session tabs go to the right pane: the setup, the transcripts and the search
/// are one view each, and they stay at the left. A tab moves by its menu (right click) or by dragging it onto the other half of the
/// pane. The right pane has its own strip and its own active tab. It closes when its last tab leaves, and a session whose tab is
/// closed goes back to the left first, so no terminal is ever left in a pane with no width (F45). A terminal that moves keeps its
/// process (F55).
/// </summary>
public partial class SessionWindow
{
    private const double MinPaneWidth = 200;

    /// <summary>The sessions whose tab is in the right pane, by session id. Their terminals are children of PaneB.</summary>
    private readonly HashSet<string> _rightSessions = new(StringComparer.Ordinal);

    /// <summary>The sessions that were at the right when the window last closed. A session's tab goes back there when it first opens.</summary>
    private IReadOnlyList<string> _savedRight = [];

    private OpenTab? _activeTabRight;

    /// <summary>The terminal the right pane is showing, or null when the pane is not split.</summary>
    private ExtendedTerminalControl? _shownRight;

    /// <summary>The terminal that was shown or clicked last, in either pane. A paste goes to it.</summary>
    private ExtendedTerminalControl? _focusTerminal;

    private bool SplitOpen => PaneB.Visibility == Visibility.Visible;

    private bool IsRight(OpenTab tab) => tab.Kind == TabKind.Session && _rightSessions.Contains(tab.Key);

    /// <summary>
    /// The terminal a paste goes to: the one whose native window has the keyboard focus, else the one shown last. WPF's own
    /// focus does not follow a click inside an embedded terminal (F55), so the native focus is asked.
    /// </summary>
    private ExtendedTerminalControl? PasteTarget()
    {
        var focus = GetFocus();
        if (focus != IntPtr.Zero)
        {
            foreach (var terminal in AllTerminals().Where(t => t.IsVisible))
            {
                var host = NativeHostOf(terminal);
                if (host != IntPtr.Zero && (host == focus || IsChild(host, focus))) return terminal;
            }
        }
        return (_focusTerminal is { IsVisible: true } last ? last : null) ?? _shown ?? _shownRight;
    }

    /// <summary>The native window of a terminal: the first hosted window in its visual tree. Zero before the terminal is loaded.</summary>
    private static IntPtr NativeHostOf(DependencyObject element)
    {
        if (element is System.Windows.Interop.HwndHost host) return host.Handle;
        var count = element is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(element) : 0;
        for (var i = 0; i < count; i++)
        {
            var found = NativeHostOf(System.Windows.Media.VisualTreeHelper.GetChild(element, i));
            if (found != IntPtr.Zero) return found;
        }
        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    /// <summary>Shows one terminal in the right pane and hides the pane's other terminals (Hidden, not Collapsed: F45).</summary>
    private void ShowInRight(ExtendedTerminalControl terminal)
    {
        foreach (var other in PaneB.Children.OfType<ExtendedTerminalControl>())
            other.Visibility = ReferenceEquals(other, terminal) ? Visibility.Visible : Visibility.Hidden;
        _shownRight = terminal;
        _focusTerminal = terminal;
        Dispatcher.BeginInvoke(new Action(() => terminal.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Moves a session's tab to the right pane and opens the split. The left pane then shows its next tab, or the setup when
    /// it has none, so it is never empty.
    /// </summary>
    private void MoveToRight(OpenTab tab)
    {
        if (tab.Kind != TabKind.Session || IsRight(tab)) return;
        if (!_terminals.TryGetValue(tab.Key, out var terminal))
        {
            SetStatus("Not moved: this session has no terminal yet.");
            return;
        }

        PlaceRight(tab.Key, terminal);
        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = null;
            var next = _tabs.FirstOrDefault(t => !IsRight(t));
            if (next is null) OpenSetupTab(null);
            else ActivateTab(next);
        }

        _activeTabRight = tab;
        ShowInRight(terminal);
        SaveTabLayout();
        RefreshTabStrip();
        SetStatus($"{tab.Title} is in the right pane. Drag its tab back, or right click it, to return it.");
    }

    /// <summary>Puts a session's terminal in the right pane and opens the split. Nothing is activated or saved.</summary>
    private void PlaceRight(string sessionId, ExtendedTerminalControl terminal)
    {
        if (ReferenceEquals(_shown, terminal)) _shown = null;
        (terminal.Parent as Panel)?.Children.Remove(terminal);
        terminal.Visibility = Visibility.Hidden;
        PaneB.Children.Add(terminal);
        _rightSessions.Add(sessionId);
        OpenSplit();
    }

    /// <summary>
    /// Moves a session's tab back to the left pane. The right pane shows its next tab, and closes when it has none.
    /// With <paramref name="activate"/> the tab is shown at the left; without it the terminal goes back hidden (a tab being closed).
    /// </summary>
    private void MoveToLeft(OpenTab tab, bool activate = true)
    {
        if (!_rightSessions.Remove(tab.Key)) return;
        if (_terminals.TryGetValue(tab.Key, out var terminal))
        {
            if (ReferenceEquals(_shownRight, terminal)) _shownRight = null;
            if (ReferenceEquals(_focusTerminal, terminal)) _focusTerminal = null;
            (terminal.Parent as Panel)?.Children.Remove(terminal);
            terminal.Visibility = Visibility.Hidden;
            Pane.Children.Add(terminal);
        }

        if (ReferenceEquals(_activeTabRight, tab)) _activeTabRight = null;
        SettleRightPane();
        if (activate) ActivateTab(tab);
        SaveTabLayout();
        RefreshTabStrip();
    }

    /// <summary>
    /// Makes the right pane consistent after a tab left it or came to it: it shows a tab when it has one, and closes when it has none.
    /// </summary>
    private void SettleRightPane()
    {
        var rightTabs = _tabs.Where(IsRight).ToList();
        if (rightTabs.Count == 0)
        {
            _activeTabRight = null;
            _shownRight = null;
            // A session at the right with no tab would sit in a pane with no width. Its terminal goes back to the left, hidden.
            foreach (var orphan in PaneB.Children.OfType<ExtendedTerminalControl>().ToList())
            {
                PaneB.Children.Remove(orphan);
                orphan.Visibility = Visibility.Hidden;
                Pane.Children.Add(orphan);
            }
            _rightSessions.Clear();
            CloseSplit();
            return;
        }

        if (_activeTabRight is null || !rightTabs.Contains(_activeTabRight)) _activeTabRight = rightTabs[0];
        if (_terminals.TryGetValue(_activeTabRight.Key, out var terminal) && !ReferenceEquals(_shownRight, terminal))
            ShowInRight(terminal);
    }

    private void OpenSplit()
    {
        if (SplitOpen) return;
        PaneColumn.MinWidth = MinPaneWidth;
        PaneBColumn.MinWidth = MinPaneWidth;
        PaneColumn.Width = new GridLength(1, GridUnitType.Star);
        PaneSplitterColumn.Width = new GridLength(5);
        PaneBColumn.Width = new GridLength(1, GridUnitType.Star);
        PaneSplitter.Visibility = Visibility.Visible;
        TabBarB.Visibility = Visibility.Visible;
        PaneB.Visibility = Visibility.Visible;
    }

    private void CloseSplit()
    {
        if (!SplitOpen) return;
        PaneSplitter.Visibility = Visibility.Collapsed;
        TabBarB.Visibility = Visibility.Collapsed;
        PaneB.Visibility = Visibility.Collapsed;
        PaneColumn.MinWidth = 0;
        PaneBColumn.MinWidth = 0;
        PaneColumn.Width = new GridLength(1, GridUnitType.Star);
        PaneSplitterColumn.Width = new GridLength(0);
        PaneBColumn.Width = new GridLength(0);
    }

    /// <summary>Saves the session tabs in strip order, and which of them are at the right. The other tabs are not kept between starts.</summary>
    private void SaveTabLayout()
    {
        var sessions = _tabs.Where(t => t.Kind == TabKind.Session).ToList();
        _savedTabOrder = sessions.Select(t => t.Key).ToList();
        _savedRight = sessions.Where(IsRight).Select(t => t.Key).ToList();
        TabOrder.Save(TabOrderPath, new TabLayout(_savedTabOrder, _savedRight), AppLog.Write);
    }

    /// <summary>The menu of a session tab: move it to the other pane.</summary>
    private ContextMenu PaneMenu(OpenTab tab)
    {
        var palette = Palette;
        var menu = new ContextMenu
        {
            HasDropShadow = true,
            Background = RailPalette.Brush(palette.Panel),
            BorderBrush = RailPalette.Brush(palette.Muted),
            Foreground = RailPalette.Brush(palette.Text),
        };
        menu.Resources[SystemColors.MenuBrushKey] = RailPalette.Brush(palette.Panel);
        menu.Resources[SystemColors.MenuTextBrushKey] = RailPalette.Brush(palette.Text);
        menu.Resources[SystemColors.ControlBrushKey] = RailPalette.Brush(palette.Panel);
        menu.Resources[SystemColors.ControlTextBrushKey] = RailPalette.Brush(palette.Text);
        menu.Resources[SystemColors.HighlightBrushKey] = RailPalette.Brush(palette.Selection);
        menu.Resources[SystemColors.HighlightTextBrushKey] = RailPalette.Brush(palette.SelectionText);

        var right = IsRight(tab);
        var item = new MenuItem
        {
            Header = right ? "Move to the left pane" : "Move to the right pane",
            Padding = new Thickness(10, 4, 16, 4),
        };
        item.Click += (_, _) =>
        {
            if (right) MoveToLeft(tab);
            else MoveToRight(tab);
        };
        menu.Items.Add(item);

        var popOut = new MenuItem { Header = "Open in its own window", Padding = new Thickness(10, 4, 16, 4) };
        popOut.Click += (_, _) => PopOut(tab);
        menu.Items.Add(popOut);
        return menu;
    }
}

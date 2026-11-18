using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTail.Bullshark.App;

/// <summary>The four sub-tabs of Insights (Insights and Settings plan): Away, Usage (the Limits page), Timeline and Storage.</summary>
public enum InsightsTab
{
    Away,
    Usage,
    Timeline,
    Storage,
}

/// <summary>
/// The Insights page: one tab with a row of sub-tabs above its content, in place of four tabs. It hosts the four views the window owns, so
/// their events and their refresh calls stay where they are (the window subscribes to them). Only the chosen sub-tab is shown; the others
/// are collapsed. The window refreshes the page it shows when <see cref="SubTabShown"/> is raised. The last sub-tab stays chosen in memory
/// for the life of the window.
/// </summary>
public sealed class InsightsView : UserControl
{
    private static readonly string[] Headers = ["Away", "Usage", "Timeline", "Storage"];

    private readonly PillTabs _tabs = new();
    private readonly Dictionary<InsightsTab, UIElement> _pages;
    private InsightsTab _current = InsightsTab.Away;

    /// <param name="away">The Away page (the digest of the period).</param>
    /// <param name="usage">The Limits page, shown as Usage.</param>
    /// <param name="timeline">The run timeline.</param>
    /// <param name="storage">The Storage page.</param>
    public InsightsView(AwayView away, LimitsView limits, TimelineView timeline, StorageView storage)
    {
        _pages = new Dictionary<InsightsTab, UIElement>
        {
            [InsightsTab.Away] = away,
            [InsightsTab.Usage] = limits,
            [InsightsTab.Timeline] = timeline,
            [InsightsTab.Storage] = storage,
        };

        var host = new Grid();
        foreach (var page in _pages.Values)
        {
            page.Visibility = Visibility.Collapsed;
            host.Children.Add(page);
        }

        _tabs.SetItems(Headers, (int)_current);
        _tabs.Chosen += index => Show((InsightsTab)index);
        _tabs.Margin = new Thickness(12, 8, 12, 0);

        var root = new DockPanel();
        DockPanel.SetDock(_tabs, Dock.Top);
        root.Children.Add(_tabs);
        root.Children.Add(host);
        Content = root;
    }

    /// <summary>The sub-tab that is shown, or was shown last.</summary>
    public InsightsTab Current => _current;

    /// <summary>
    /// Raised when a sub-tab is shown with <c>refresh</c> (the default): the window reads that page again. A sub-tab chosen with <c>refresh</c>
    /// false is drawn without it, for a page the window has just built.
    /// </summary>
    public event Action<InsightsTab>? SubTabShown;

    /// <summary>Shows a sub-tab, and the window refreshes it unless <paramref name="refresh"/> is false.</summary>
    public void Show(InsightsTab tab, bool refresh = true)
    {
        _current = tab;
        _tabs.Select((int)tab);
        foreach (var (key, page) in _pages) page.Visibility = key == tab ? Visibility.Visible : Visibility.Collapsed;
        if (refresh) SubTabShown?.Invoke(tab);
    }

    /// <summary>The sub-tab after <paramref name="tab"/>, wrapping after Storage to Away.</summary>
    public static InsightsTab Next(InsightsTab tab) => (InsightsTab)(((int)tab + 1) % Headers.Length);

    /// <summary>Takes the rail's palette. The sub-tabs take the Ui brushes, which follow the theme; the text colour follows here.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        Foreground = RailPalette.Brush(palette.Text);
    }
}

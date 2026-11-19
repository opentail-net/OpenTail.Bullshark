using System.Windows;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Insights tab (Insights and Settings plan, 2026-11-18): Away, Usage, Timeline and Storage as sub-tabs of one tab. The four views keep their
/// code and their events; <see cref="InsightsView"/> hosts them, and this part decides which one the window refreshes and when. The status bar's
/// Insights button, the command palette, the --open-tab test flag and Away's return all open the tab through <see cref="OpenInsights"/>.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The key of the Insights tab in the tab strip.</summary>
    private const string InsightsKey = "insights";

    private readonly InsightsView _insights;

    /// <summary>
    /// True while Insights was opened for the digest of a return (D2) and the owner has not opened a sub-tab since. Got it then closes the tab (D4).
    /// Written on the UI thread only.
    /// </summary>
    private bool _insightsForDigest;

    private void InitInsights()
    {
        Pane.Children.Add(_insights);
        _insights.SubTabShown += RefreshInsightsPage;
    }

    /// <summary>
    /// Shows the Insights tab on a sub-tab. With <paramref name="refresh"/> the page is read again (the owner's opening); a return that has just built
    /// the digest passes false, so the digest is not built twice.
    /// </summary>
    private void OpenInsights(InsightsTab tab, bool refresh = true)
    {
        SetActive(TabFor(TabKind.Insights, InsightsKey, () => "Insights"));
        ShowOnly(_insights);
        _insights.Show(tab, refresh);
    }

    /// <summary>The status bar's Insights button: the sub-tab that was shown last.</summary>
    private void InsightsButton_Click(object sender, RoutedEventArgs e) => OpenInsights(_insights.Current);

    /// <summary>
    /// Reads again the page that is shown. Every owner-visible opening of a sub-tab ends the digest's Got it (D4), so the flag is cleared here, and
    /// the return that opens the tab sets it after this runs.
    /// </summary>
    private void RefreshInsightsPage(InsightsTab tab)
    {
        _insightsForDigest = false;
        // The Night page refreshes every 30 seconds while it is the one shown; every other sub-tab stops that (night batch N6).
        SetNightLive(tab == InsightsTab.Night);
        switch (tab)
        {
            case InsightsTab.Away:
                _ = RefreshAwayAsync(onReturn: false);
                break;
            case InsightsTab.Night:
                _ = RefreshNightAsync(full: true);
                break;
            case InsightsTab.Usage:
                _ = RefreshLimitsAsync();
                break;
            case InsightsTab.Timeline:
                _ = RefreshTimelineAsync();
                break;
            case InsightsTab.Storage:
                _ = RefreshStorageAsync();
                break;
        }
    }
}

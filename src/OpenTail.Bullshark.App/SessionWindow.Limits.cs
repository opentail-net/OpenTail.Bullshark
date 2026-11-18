using System.Windows;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Limits page (A4), opened from the status bar next to Storage: the usage-limit hits and the usage per day of the last 30 days, read-only. The transcripts are
/// read on a pool thread, and the page only draws what it is given.
/// </summary>
public partial class SessionWindow
{
    private readonly LimitsView _limits = new() { Visibility = Visibility.Hidden };

    private void InitLimits()
    {
        Pane.Children.Add(_limits);
        _limits.RefreshRequested += () => _ = RefreshLimitsAsync();
    }

    private void LimitsButton_Click(object sender, RoutedEventArgs e)
    {
        SetActive(TabFor(TabKind.Limits, "limits", () => "Limits"));
        ShowOnly(_limits);
        _ = RefreshLimitsAsync();
    }

    /// <summary>Reads the limit hits and the usage report on a pool thread, then draws them. A read that fails is said on the page.</summary>
    private async Task RefreshLimitsAsync()
    {
        try
        {
            var report = await Task.Run(() =>
                LimitHistory.ReadReportWithCache(_options.ProjectsRoot, DateTimeOffset.Now, _options.LimitHistoryCachePath, log: AppLog.Write));
            _limits.Show(report);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"limit history not read: {ex.Message}");
            _limits.ShowProblem($"The limit history cannot be read: {ex.Message}");
        }
    }
}

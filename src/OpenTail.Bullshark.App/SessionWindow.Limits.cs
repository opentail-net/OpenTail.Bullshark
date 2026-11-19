using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Limits page (A4), the Usage sub-tab of Insights (2026-11-18): the usage-limit hits and the usage per day of the last 30 days, read-only. The
/// transcripts are read on a pool thread, and the page only draws what it is given.
/// </summary>
public partial class SessionWindow
{
    private readonly LimitsView _limits = new() { Visibility = Visibility.Hidden };

    private void InitLimits()
    {
        _limits.RefreshRequested += () => _ = RefreshLimitsAsync();
    }

    /// <summary>Reads the limit hits and the usage report on a pool thread, then draws them. A read that fails is said on the page.</summary>
    private async Task RefreshLimitsAsync()
    {
        try
        {
            var report = await Task.Run(() =>
                LimitHistory.ReadReportWithCache(_options.ProjectsRoot, DateTimeOffset.Now, _options.LimitHistoryCachePath, log: AppLog.Write));
            // The usage keeps its folder keys; the page shows each key's folder name (keyboard and copy batch, T1).
            var projects = report.Usage.Projects.Select(project => project.Project);
            var tops = report.Usage.Days.Select(day => day.TopProject).OfType<string>();
            _limits.Show(report, FolderLabelsFor(projects.Concat(tops)));
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"limit history not read: {ex.Message}");
            _limits.ShowProblem($"The limit history cannot be read: {ex.Message}");
        }
    }

    /// <summary>
    /// The folder names for the history's folders and the extra keys (the usage report's projects): one label per key, the same in Search and on the
    /// Usage page (FolderDisplay). A key with no known folder keeps its key as the name. Only names change: the groups and the usage are by key.
    /// </summary>
    private IReadOnlyDictionary<string, FolderLabel> FolderLabelsFor(IEnumerable<string> extraKeys)
    {
        var history = _history ?? new List<SessionDigest>();
        var keys = history.Select(digest => digest.ProjectFolder).Concat(extraKeys);
        return FolderDisplay.LabelsFor(keys, history.Select(digest => (digest.ProjectFolder, digest.Cwd)));
    }
}

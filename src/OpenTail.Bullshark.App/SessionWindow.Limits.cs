using OpenTail.Bullshark.Core.Copilot;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Codex;
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

    /// <summary>
    /// The Codex rollouts read for the usage report, kept between reads (R8 part 2). One per window: the Limits page and the Away digest share it.
    /// The memo locks its own state, since the two reads run on pool threads and may overlap.
    /// </summary>
    private readonly CodexUsageMemo _codexUsage = new();

    /// <summary>
    /// The Codex history folder the usage report reads (R8 part 2): the Codex provider's own folder, whether or not Codex is the provider the rail
    /// shows. Read on the window's thread and given to the pool thread.
    /// </summary>
    private string CodexHistoryRoot => (_codex ?? new CodexProvider()).Paths.HistoryRoot;

    private void InitLimits()
    {
        _limits.RefreshRequested += () => _ = RefreshLimitsAsync();
    }

    /// <summary>Reads the limit hits and the usage report on a pool thread, then draws them. A read that fails is said on the page.</summary>
    private async Task RefreshLimitsAsync()
    {
        try
        {
            var usage = _behaviour.Usage;
            var codexRoot = CodexHistoryRoot;
            var codexMemo = _codexUsage;
            var report = await Task.Run(() =>
                LimitHistory.ReadReportWithCache(_options.ProjectsRoot, DateTimeOffset.Now, _options.LimitHistoryCachePath, log: AppLog.Write,
                    usage: usage, codexHistoryRoot: codexRoot, codexMemo: codexMemo));
            // The usage keeps its folder keys; the page shows each key's folder name (keyboard and copy batch, T1).
            var projects = report.Usage.Projects.Select(project => project.Project);
            var tops = report.Usage.Days.Select(day => day.TopProject).OfType<string>();
            _limits.Show(report, FolderLabelsFor(projects.Concat(tops)));

            // The Copilot usage (PP-D5) is read from its own history folder and drawn in its own table: nothing is added to the token totals above.
            var copilotRoot = (_copilot ??= new CopilotProvider()).Paths.HistoryRoot;
            var copilot = await Task.Run(() =>
                CopilotUsageReader.Read(copilotRoot, DateTimeOffset.Now, LimitHistory.DefaultDays, CancellationToken.None));
            _limits.ShowCopilot(copilot);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"limit history not read: {ex.GetType().Name}");
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

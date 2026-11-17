using System.Windows.Controls;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The runs that wait for a start time (gems item 5): the row under the run's folder, and "Start now", which clears the time. The
/// time itself is kept in runs.json, so a run set for later still waits after the window closes and opens again.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The row of a run that waits: its time, and a right-click menu that starts it now.</summary>
    private TreeViewItem WaitingRow(RunRecord run)
    {
        var at = run.StartAt!.Value.ToLocalTime();
        var item = NewItem(Label($"Run {run.RunId} starts at {at:HH:mm}", muted: true),
            new RailTag(RailKind.Heading, "waiting:" + run.RunId),
            "This run waits for its start time. It starts on the first pass after that time, while this window is open. " +
            "Right-click to start it now.");
        var startNow = new MenuItem { Header = "Start now" };
        startNow.Click += async (_, _) => await StartRunNowAsync(run.RunId);
        item.ContextMenu = new ContextMenu { Items = { startNow } };
        return item;
    }

    /// <summary>
    /// Clears the start time of one run in runs.json, then runs a pass, so the run's sessions start now. The file is written on a pool
    /// thread. A file that cannot be read is reported in the status line, and nothing is changed.
    /// </summary>
    private async Task StartRunNowAsync(string runId)
    {
        try
        {
            await Task.Run(() => _runStore.Update(runs => RunStore.StartingNow(runs, runId)));
        }
        catch (StoreUnreadableException ex)
        {
            AppLog.Write($"start now: {ex.Message}");
            SetStatus($"Not started: {ex.Message}");
            return;
        }
        await PassAndRefreshAsync();
    }
}

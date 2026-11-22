using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The run timeline tab (additions item 5), opened from the status bar. The log is read on a pool thread, and the runs are read from
/// runs.json there too. The supervisor writes the log through <see cref="EventLog"/>, beside the state file.
/// </summary>
public partial class SessionWindow
{
    private readonly TimelineView _timeline = new() { Visibility = Visibility.Hidden };

    /// <summary>The event log, events.jsonl beside the state file. The pass writes it, and the timeline reads it.</summary>
    private readonly EventLog _events;

    private void InitTimeline()
    {
        _timeline.RefreshRequested += () => _ = RefreshTimelineAsync();
        // The empty state's next step: the setup tab, the same one the + tab opens (firstrun batch B8).
        _timeline.SetupRequested += () => OpenSetupTab(null);
    }

    /// <summary>Reads the runs and the events on a pool thread, then draws them. A log that cannot be read is shown as a line, not an error.</summary>
    private async Task RefreshTimelineAsync()
    {
        try
        {
            var (runs, events) = await Task.Run(() =>
            {
                var (loaded, _) = LoadRuns(_runStore);
                return (loaded, _events.Read());
            });
            _timeline.Show(runs, events);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"timeline not read: {ex.GetType().Name}");
            _timeline.ShowProblem($"The timeline cannot be read: {ex.Message}");
        }
    }
}

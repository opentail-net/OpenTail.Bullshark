using System.Windows;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The status bar's usage-limit text (A2): the limit in force and when it resets, read from the transcripts on this computer. The scan runs on
/// a pool thread, at most once a minute, from the pass tick (no timer of its own). What is drawn comes from the last scan, and a reset that has
/// passed leaves the text at the next tick without a scan.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The scan of the transcripts for the limit text runs at most this often (A2).</summary>
    private static readonly TimeSpan LimitScanEvery = TimeSpan.FromSeconds(60);

    /// <summary>The limits in force as the last scan read them. Written on the UI thread only.</summary>
    private IReadOnlyList<ActiveLimit> _activeLimits = [];

    /// <summary>When the last scan started, or null before the first one.</summary>
    private DateTimeOffset? _limitScanStarted;

    private bool _limitScanRunning;

    /// <summary>
    /// Called from every pass tick and on load. It draws the limits the last scan read, and starts a scan when none is running and the last one
    /// started at least a minute ago. The scan is not awaited, so it does not hold up the pass.
    /// </summary>
    private void RefreshLimitStatus()
    {
        var now = DateTimeOffset.Now;
        ShowLimitStatus(now);
        if (_limitScanRunning) return;
        if (_limitScanStarted is { } started && now - started < LimitScanEvery) return;
        _limitScanRunning = true;
        _limitScanStarted = now;
        FireAndForget.Observe(ScanLimitStatusAsync(), "limit status scan");
    }

    /// <summary>Reads the limits in force on a pool thread. A read that fails keeps the last list, and the failure goes to the log.</summary>
    private async Task ScanLimitStatusAsync()
    {
        try
        {
            _activeLimits = await Task.Run(() => LimitStatus.Active(_options.ProjectsRoot, DateTimeOffset.Now));
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"limit status not read: {ex.GetType().Name}");
        }
        // Any other failure is not caught here: FireAndForget logs its type and message (reliability batch R10).
        finally
        {
            _limitScanRunning = false;
            ShowLimitStatus(DateTimeOffset.Now);
        }
    }

    /// <summary>
    /// Draws the limits still in force at <paramref name="now"/>: the one that governs first, the others named after it, and every one in the
    /// tooltip. Hidden when none is in force.
    /// </summary>
    private void ShowLimitStatus(DateTimeOffset now)
    {
        var live = _activeLimits.Where(limit => limit.ResetsAt > now).ToList();
        if (live.Count == 0)
        {
            LimitStatusText.Visibility = Visibility.Collapsed;
            LimitStatusText.ToolTip = null;
            return;
        }

        var text = LimitWording(live[0], now);
        if (live.Count > 1)
        {
            var others = string.Join(", ", live.Skip(1).Select(limit => LimitStatus.LabelOf(limit.Type)));
            text += $" ({others} also active)";
        }
        LimitStatusText.Text = text;
        LimitStatusText.ToolTip = string.Join(Environment.NewLine, live.Select(limit => LimitWording(limit, now)));
        // The limits are Claude's: Codex shows none here (stage C4).
        LimitStatusText.Visibility = ClaudeSupervision ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"5-hour limit resets 13:10": the limit's name, and its reset as the start-time text shows it (local time).</summary>
    private static string LimitWording(ActiveLimit limit, DateTimeOffset now) =>
        $"{LimitStatus.LabelOf(limit.Type)} limit resets {StartTime.Describe(limit.ResetsAt, now)}";
}

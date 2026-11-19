using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>One session's plan as the night report shows it: boxes done of all, and the next open item.</summary>
public sealed record NightPlan(string Session, int Done, int Total, string? Next);

/// <summary>
/// The night batch (N1, N5 and N6, 2026-11-19): the supervisor heartbeat, the report file of a return, and the Night page. The window is the only
/// supervisor, so the heartbeat tells the owner in the morning whether the window ran in the night. The Night page is the bedtime preflight:
/// its checks are read here on a pool thread and drawn by <see cref="NightView"/>. Nothing here starts, sends or changes a session.
/// </summary>
public partial class SessionWindow
{
    private readonly NightView _night = new() { Visibility = Visibility.Hidden };

    /// <summary>The Night page's refresh while it is shown (every 30 seconds). Stopped when another sub-tab is shown.</summary>
    private readonly DispatcherTimer _nightTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>True while the Night page's checks are being read, so that a second request does not read them again.</summary>
    private bool _nightRunning;

    /// <summary>The AC sleep timeout in minutes, as the last full read gave it (0 is never); null when unread or unreadable.</summary>
    private int? _sleepMinutes;

    /// <summary>The problems of the last setup check run from the Night page; null when it has not run.</summary>
    private int? _setupProblems;

    /// <summary>The login warning the last supervisor pass read (N2). Not readable before the first pass that reads a live session.</summary>
    private LoginReading _login = new(Readable: false, Remaining: null);

    /// <summary>When the heartbeat was last written; the write is rate-limited (N1).</summary>
    private DateTimeOffset? _lastHeartbeatWrite;

    /// <summary>The supervisor passes this window has run. Written into the heartbeat (N1).</summary>
    private int _passNumber;

    /// <summary>The folder of the night reports: <c>reports</c> beside the state file (N5).</summary>
    private string ReportsFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_options.StatePath))!, NightReport.FolderName);

    /// <summary>What the Night page's refresh reads on a pool thread. The sleep setting and the setup check are read only on a full refresh.</summary>
    private sealed record NightReads(
        DateTimeOffset? LastPass,
        IReadOnlyList<RunRecord>? Runs,
        QuietWindow? Quiet,
        int? SleepMinutes,
        int? SetupProblems);

    private void InitNight()
    {
        _night.RefreshRequested += () => _ = RefreshNightAsync(full: true);
        _night.SettingsRequested += () => OpenSettings(SettingsTab.General);
        _nightTimer.Tick += (_, _) =>
        {
            // The timer stops once the Night page is no longer the one shown; the check here is a second guard.
            if (_insights.Visibility == Visibility.Visible && _insights.Current == InsightsTab.Night) _ = RefreshNightAsync(full: false);
            else _nightTimer.Stop();
        };
    }

    /// <summary>
    /// Writes the heartbeat after a supervisor pass that ran (N1). A write happens at most every <see cref="Heartbeat.MinWriteInterval"/>. A failed
    /// write is logged and changes nothing else: the pass has already run.
    /// </summary>
    private void WriteHeartbeat(DateTimeOffset now)
    {
        _passNumber++;
        if (!Heartbeat.WriteDue(_lastHeartbeatWrite, now)) return;
        _lastHeartbeatWrite = now;
        try
        {
            Heartbeat.Write(Heartbeat.PathFor(_options.StatePath), now, _passNumber);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"heartbeat not written: {ex.GetType().Name}");
        }
    }

    /// <summary>Starts the Night page's 30-second refresh while it is shown, or stops it.</summary>
    private void SetNightLive(bool live)
    {
        if (live)
        {
            if (!_nightTimer.IsEnabled) _nightTimer.Start();
        }
        else if (_nightTimer.IsEnabled)
        {
            _nightTimer.Stop();
        }
    }

    /// <summary>
    /// Reads the Night page's checks and draws them. A full refresh (the page shown, or Refresh) also runs the power query and the setup check; a timer
    /// tick does not, so those two keep their last value. Every read is on a pool thread, and a failure is said on the page.
    /// </summary>
    private async Task RefreshNightAsync(bool full)
    {
        if (_nightRunning) return;
        _nightRunning = true;
        try
        {
            var reads = await ReadNightAsync(full);
            if (full)
            {
                _sleepMinutes = reads.SleepMinutes;
                _setupProblems = reads.SetupProblems;
            }

            var now = DateTimeOffset.Now;
            var input = new PreflightInput(
                Now: now,
                LastPass: reads.LastPass,
                Daemon: _daemon.State,
                StaleSessions: _agents.Count(agent => agent.State == DaemonStatus.StaleState),
                Login: _login,
                SetupProblems: _setupProblems,
                ActiveLimits: _activeLimits,
                Runs: reads.Runs,
                TrustWaiting: _trustSteps.Count,
                SleepMinutes: _sleepMinutes,
                NeedsYou: _attention.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count(),
                Quiet: new QuietState(reads.Quiet is { } window ? QuietHours.Label(window) : null, QuietHours.IsQuietAt(reads.Quiet, now)));
            var report = Preflight.Build(input);
            var lastPass = reads.LastPass is { } last
                ? Heartbeat.Describe(Heartbeat.Age(last, now) ?? TimeSpan.Zero)
                : "no pass recorded yet";
            _night.Show(report, lastPass, input.Quiet);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"night page not read: {ex.GetType().Name}");
            _night.ShowProblem($"The checks cannot be read: {ex.Message}");
        }
        finally
        {
            _nightRunning = false;
        }
    }

    /// <summary>The heartbeat, the runs, the quiet hours, and on a full refresh the sleep setting and the setup check, on pool threads.</summary>
    private async Task<NightReads> ReadNightAsync(bool full)
    {
        var statePath = _options.StatePath;
        var runStore = _runStore;
        var (last, runs, quiet) = await Task.Run(() =>
        {
            var heartbeat = Heartbeat.Read(Heartbeat.PathFor(statePath));
            var (runsRead, runsProblem) = LoadRuns(runStore);
            var quietWindow = QuietHours.Parse(UiFile.ReadInBackground(statePath, QuietHours.Key));
            return (heartbeat, runsProblem is null ? runsRead : null, quietWindow);
        });
        if (!full) return new NightReads(last, runs, quiet, null, null);

        var sleep = await SleepMinutesAsync();
        var setup = await Task.Run(SetupProblemCount);
        return new NightReads(last, runs, quiet, sleep, setup);
    }

    /// <summary>
    /// The AC sleep timeout from the read-only `powercfg /query`, run with a five-second timeout and no window (N3). Null when the command fails, times
    /// out, or its text has no such line. Nothing is changed.
    /// </summary>
    private static async Task<int?> SleepMinutesAsync()
    {
        try
        {
            var result = await new ProcessCliRunner("powercfg").RunAsync(PowerSettings.QueryArguments, null, TimeSpan.FromSeconds(5));
            return result.Succeeded ? PowerSettings.ParseSleepMinutes(result.StdOut) : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>The number of problems (not warnings) in the setup check, which reads Claude Code's files and changes nothing. Null when it cannot run.</summary>
    private static int? SetupProblemCount()
    {
        try
        {
            return SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath).Count(item => item.State == SetupState.Problem);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"setup check not run for the night page: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// Saves the night report for a return with news (N5): the digest as Markdown, with the plans and the tokens, in the reports folder, then
    /// the folder is pruned to its newest reports. Only the file name is logged. A failure is said on the Away page, and nothing is thrown.
    /// </summary>
    private async Task SaveNightReportAsync(AwayBuild build, DateTimeOffset since, DateTimeOffset now)
    {
        var folder = ReportsFolder;
        var plans = build.Plans.Select(plan => (plan.Session, plan.Done, plan.Total, plan.Next)).ToList();
        try
        {
            var name = await Task.Run(() =>
            {
                var text = NightReport.Markdown(build.Digest, since, now, plans, build.Usage);
                var path = NightReport.Save(folder, now, text);
                NightReport.Prune(folder);
                return Path.GetFileName(path);
            });
            AppLog.Write($"night report saved: {name}");
            _away.ShowSaved(name);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"night report not saved: {ex.GetType().Name}");
            _away.ShowSaveFailed(ex.Message);
        }
    }

    /// <summary>"Open reports folder": the folder opens in Explorer. A folder that cannot be opened is said in the status line.</summary>
    private void OpenReportsFolder()
    {
        try
        {
            Directory.CreateDirectory(ReportsFolder);
            Process.Start(new ProcessStartInfo(ReportsFolder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"reports folder not opened: {ex.GetType().Name}");
            SetStatus($"The reports folder cannot be opened: {ex.Message}");
        }
    }
}

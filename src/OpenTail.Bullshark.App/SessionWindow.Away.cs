using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// "While you were away" (backlog D1): the Away sub-tab of Insights (2026-11-18), opened from the Insights tab, and by itself when the window comes
/// back after at least <see cref="AwayDigest.ReturnGap"/> away with something to say (D2). The time the window was last in use is kept in ui.json:
/// it is written when the window is deactivated, when it closes, and on Got it (D1, D4). The digest is built on a pool thread from the window's own
/// transcript index, listing and attention list, and from the supervisor's log and the limit history. Nothing is sent to a session; no sound plays
/// and nothing flashes.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The key of the last-seen time in ui.json.</summary>
    private const string LastSeenKey = "lastSeen";

    private readonly AwayView _away = new() { Visibility = Visibility.Hidden };

    /// <summary>True while a digest is being built, so that a second request does not start another.</summary>
    private bool _awayRunning;

    /// <summary>A return that needed the transcript index before it was read. The index's read asks again when it is done.</summary>
    private bool _awayWaitsForHistory;

    /// <summary>
    /// The digest of a period, with what the night report adds to it (N5): the usage report of the same transcripts, the plans of the sessions that
    /// have a plan file, and the problem when the sessions cannot be read.
    /// </summary>
    private sealed record AwayBuild(AwayDigest Digest, UsageReport? Usage, IReadOnlyList<NightPlan> Plans, string? Problem);

    private void InitAway()
    {
        _away.DismissRequested += DismissAway;
        _away.OpenReportsRequested += OpenReportsFolder;
        Activated += (_, _) => _ = RefreshAwayAsync(onReturn: true);
        Deactivated += (_, _) => WriteLastSeen();
        Closing += (_, _) => WriteLastSeen();
    }

    /// <summary>
    /// Got it: the period counts as seen, so the page does not open again for it (D4). When the return opened Insights for this digest, the Insights
    /// tab closes, as it always did. When the owner opened Insights by hand, Got it moves to the next sub-tab instead (the plan's decision, 2026-11-18).
    /// </summary>
    private void DismissAway()
    {
        WriteLastSeen();
        if (_insightsForDigest)
        {
            _insightsForDigest = false;
            if (_tabs.FirstOrDefault(tab => tab.Kind == TabKind.Insights) is { } tab) CloseTab(tab);
            return;
        }
        _insights.Show(InsightsView.Next(_insights.Current));
    }

    /// <summary>
    /// Builds the digest for the period and draws it. On a return the Insights tab opens on Away only after a gap of <see cref="AwayDigest.ReturnGap"/>
    /// and only when something happened in the period (D2); the Away sub-tab always draws it when the owner opens it. A failure is said on the page
    /// when the owner asked, and is only logged on a return.
    /// </summary>
    private async Task RefreshAwayAsync(bool onReturn)
    {
        if (_awayRunning) return;
        var now = DateTimeOffset.Now;
        var lastSeen = ReadLastSeen();
        if (onReturn && !AwayDigest.IsReturn(lastSeen, now)) return;

        _awayRunning = true;
        try
        {
            // The index is read again first: a session that ran since the last read has written its transcript since then, and the digest is
            // about that. LoadHistoryAsync returns at once when a read is already under way (a return that came at start waits for it, below).
            await LoadHistoryAsync();
            if (_history is null)
            {
                // The transcripts are still being read: a return asks again once they are, and the button says it cannot show the page yet.
                if (onReturn) _awayWaitsForHistory = true;
                else _away.ShowProblem("The transcripts are still being read. Open Insights again in a moment.");
                return;
            }

            var since = AwayDigest.PeriodStart(lastSeen, now);
            var build = await BuildAwayAsync(since, now);
            if (build.Problem is not null)
            {
                AppLog.Write($"away digest: the sessions cannot be read: {build.Problem}");
                if (!onReturn) _away.ShowProblem($"The sessions cannot be read, so the page is not complete: {build.Problem}");
                return;
            }
            if (onReturn && !build.Digest.HasNews) return;

            if (onReturn)
            {
                // The digest is built here, so the page is not read again by the tab (refresh: false). The tab then closes on Got it.
                OpenInsights(InsightsTab.Away, refresh: false);
                _insightsForDigest = true;
            }
            _away.Show(build.Digest);
            // A return with news also leaves a copy of the digest in the reports folder (night batch N5). It never overwrites and never throws.
            if (onReturn) _ = SaveNightReportAsync(build, since, now);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"away digest not built: {ex.Message}");
            if (!onReturn) _away.ShowProblem($"The digest cannot be built: {ex.Message}");
        }
        finally
        {
            _awayRunning = false;
        }
    }

    /// <summary>
    /// The digest for the period, built on a pool thread. The state file is read there; one that cannot be read gives its reason and no rows.
    /// The transcript index, the listing and the attention list are the window's own, taken before the call and not changed by it.
    /// </summary>
    private async Task<AwayBuild> BuildAwayAsync(DateTimeOffset since, DateTimeOffset now)
    {
        var digests = _history ?? new List<SessionDigest>();
        var agents = _agents;
        var attention = _attention;
        var events = _events;
        var store = _store;
        var projectsRoot = _options.ProjectsRoot;
        var limitCachePath = _options.LimitHistoryCachePath;
        // The plan files of the sessions, copied here: the library's marks belong to the window's thread.
        var planFiles = _marks.Where(pair => pair.Value.PlanFile is not null).ToDictionary(pair => pair.Key, pair => pair.Value.PlanFile!, StringComparer.Ordinal);
        return await Task.Run(() =>
        {
            var (records, problem) = LoadRecords(store);
            var log = events.ReadSince(since, AwayDigest.EventLimit);
            // The same read as the Limits page's: the hits are the ones ReadWithCache gave, and the usage report comes from the same files.
            var report = LimitHistory.ReadReportWithCache(projectsRoot, now, limitCachePath, log: AppLog.Write);
            var digest = AwayDigest.Build(records.Values.ToList(), digests, agents, log, report.Hits, attention, since, now);
            return new AwayBuild(digest, report.Usage, PlansOf(records.Values, planFiles), problem);
        });
    }

    /// <summary>The plans of the sessions that have a plan file and whose file can be read, by session name (N5). A plan file is read here, not cached.</summary>
    private static IReadOnlyList<NightPlan> PlansOf(IEnumerable<SessionRecord> records, IReadOnlyDictionary<string, string> planFiles)
    {
        var plans = new List<NightPlan>();
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.SessionId) || !planFiles.TryGetValue(record.SessionId, out var path)) continue;
            if (PlanProgress.ReadFile(path) is not { } count) continue;
            plans.Add(new NightPlan(record.Name, count.Done, count.Total, count.Next));
        }
        return plans.OrderBy(plan => plan.Session, StringComparer.Ordinal).ToList();
    }

    /// <summary>When the window was last in use, from ui.json. Null when there is none, or the file cannot be read: then there is no return.</summary>
    private DateTimeOffset? ReadLastSeen() =>
        UiFile.Read(_options.StatePath, LastSeenKey) is JsonValue value && value.TryGetValue<string>(out var text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    /// <summary>Records that the window is in use now. A file that cannot be written is logged by ui.json, and nothing else changes.</summary>
    private void WriteLastSeen() =>
        UiFile.Set(_options.StatePath, LastSeenKey, JsonValue.Create(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture))!);
}

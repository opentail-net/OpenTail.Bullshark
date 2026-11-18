using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// "While you were away" (backlog D1): opened by the status bar's Away button, and by itself when the window comes back after at least
/// <see cref="AwayDigest.ReturnGap"/> away with something to say (D2). The time the window was last in use is kept in ui.json: it is written when
/// the window is deactivated, when it closes, and on Got it (D1, D4). The digest is built on a pool thread from the window's own transcript index,
/// listing and attention list, and from the supervisor's log and the limit history. Nothing is sent to a session; no sound plays and nothing flashes.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The key of the last-seen time in ui.json.</summary>
    private const string LastSeenKey = "lastSeen";

    private const string AwayKey = "away";

    private readonly AwayView _away = new() { Visibility = Visibility.Hidden };

    /// <summary>True while a digest is being built, so that a second request does not start another.</summary>
    private bool _awayRunning;

    /// <summary>A return that needed the transcript index before it was read. The index's read asks again when it is done.</summary>
    private bool _awayWaitsForHistory;

    private void InitAway()
    {
        Pane.Children.Add(_away);
        _away.DismissRequested += DismissAway;
        Activated += (_, _) => _ = RefreshAwayAsync(onReturn: true);
        Deactivated += (_, _) => WriteLastSeen();
        Closing += (_, _) => WriteLastSeen();
    }

    private void AwayButton_Click(object sender, RoutedEventArgs e)
    {
        SetActive(TabFor(TabKind.Away, AwayKey, () => "Away"));
        ShowOnly(_away);
        _ = RefreshAwayAsync(onReturn: false);
    }

    /// <summary>Got it: the period counts as seen, so the page does not open again for it (D4). Its tab closes.</summary>
    private void DismissAway()
    {
        WriteLastSeen();
        if (_tabs.FirstOrDefault(tab => tab.Kind == TabKind.Away) is { } tab) CloseTab(tab);
    }

    /// <summary>
    /// Builds the digest for the period and draws it. On a return the page opens only after a gap of <see cref="AwayDigest.ReturnGap"/> and only when
    /// something happened in the period (D2); the button and the tab always draw it. A failure is said on the page when the owner asked, and is only
    /// logged on a return.
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
                else _away.ShowProblem("The transcripts are still being read. Press Away again in a moment.");
                return;
            }

            var (digest, problem) = await BuildAwayAsync(AwayDigest.PeriodStart(lastSeen, now), now);
            if (problem is not null)
            {
                AppLog.Write($"away digest: the sessions cannot be read: {problem}");
                if (!onReturn) _away.ShowProblem($"The sessions cannot be read, so the page is not complete: {problem}");
                return;
            }
            if (onReturn && !digest.HasNews) return;

            if (onReturn)
            {
                SetActive(TabFor(TabKind.Away, AwayKey, () => "Away"));
                ShowOnly(_away);
            }
            _away.Show(digest);
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
    private async Task<(AwayDigest Digest, string? Problem)> BuildAwayAsync(DateTimeOffset since, DateTimeOffset now)
    {
        var digests = _history ?? new List<SessionDigest>();
        var agents = _agents;
        var attention = _attention;
        var events = _events;
        var store = _store;
        var projectsRoot = _options.ProjectsRoot;
        return await Task.Run(() =>
        {
            var (records, problem) = LoadRecords(store);
            var log = events.ReadSince(since, AwayDigest.EventLimit);
            var hits = LimitHistory.Read(projectsRoot, now).Hits;
            var digest = AwayDigest.Build(records.Values.ToList(), digests, agents, log, hits, attention, since, now);
            return (digest, problem);
        });
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

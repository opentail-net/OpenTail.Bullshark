using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CommunityToolkit.WinUI.Notifications;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The sessions that need the owner (item 1 of the additions plan): the "Needs you" group at the top of the rail, the count in
/// the status bar, and the announcement when a session starts waiting. The lists are computed in Core; this file only draws them.
/// </summary>
public partial class SessionWindow
{
    private const string NeedsYouKey = "needs-you";

    /// <summary>The ui.json key for the toast shown when the window is not in front (toasts-2026-11-18, D3). Missing or not a boolean means on.</summary>
    private const string ToastKey = "toast";

    /// <summary>The sessions that need the owner, as the last refresh saw them. Shown under Needs you and counted in the status bar.</summary>
    private IReadOnlyList<AttentionItem> _attention = [];

    /// <summary>
    /// The list the last pass announced from. Null until the first pass after the window opens: the sessions that are already
    /// waiting then are in view, so they are not announced.
    /// </summary>
    private IReadOnlyList<AttentionItem>? _announced;

    /// <summary>
    /// The notices the window has shown (overlaps and conflicts), by <see cref="AttentionList.NoticeKeys"/>: the session, the reason and the file or
    /// files. Kept for the life of the window, so a notice that comes back after a listing gap, or after its session left done, is not announced
    /// again (round 5 item 7, round 6 item 6). Written on the UI thread only.
    /// </summary>
    private readonly HashSet<string> _announcedNotices = new(StringComparer.Ordinal);

    /// <summary>The re-alerts for the sessions that wait and are not answered (escalation-2026-11-18). Kept in memory for the life of the window; UI thread only.</summary>
    private readonly AttentionEscalation _escalation = new();

    /// <summary>The ui.json key for the re-alert interval, in minutes: 0 turns re-alerts off, and a missing value is 15.</summary>
    private const string EscalateKey = "escalateMinutes";

    private void UpdateNeedsYouCount()
    {
        // A session can be waiting for more than one reason (gems item 1: a blocked session can also share a file), and it is one session.
        var count = _attention.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count();
        NeedsYouText.Text = count == 1 ? "1 session needs you" : $"{count} sessions need you";
        NeedsYouText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The Needs you group: one row per waiting session, oldest first. A session with a terminal gets the same row as under its
    /// folder, so a click selects it as usual. A start that never got a session has no terminal and shows its reason, as a folder does.
    /// </summary>
    private TreeViewItem NeedsYouSection(IReadOnlyDictionary<string, SessionRecord> records)
    {
        // The header counts the rows listed under it, the daemon note included, so it never says 0 with a row under it (round 8 item 5).
        var rowCount = AttentionList.NeedsYouRowCount(_attention, records, _daemonNotice is not null);
        var section = NewItem(Label($"Needs you ({rowCount})", bold: true),
            new RailTag(RailKind.Heading, NeedsYouKey),
            "Sessions that wait for you: blocked on a permission or a question, stopped and not brought back, a start that failed, " +
            "or a file that another session of the run also wrote.");
        section.IsExpanded = _expanded.GetValueOrDefault(NeedsYouKey, true);

        // The daemon note comes first: the daemon is not running, and listed sessions still claim working or blocked (round 7 item 1, revised).
        // It says so, and blocks nothing.
        if (_daemonNotice is { } daemonProblem)
            section.Items.Add(NewItem(Label(daemonProblem, bold: true, wrap: true),
                new RailTag(RailKind.Heading, NeedsYouKey + ":daemon"), daemonProblem));

        // One row per session: its first reason is the one the row is shown for. The other reasons are in the Changes tab and the announcement.
        foreach (var item in _attention.DistinctBy(i => i.Name, StringComparer.Ordinal))
        {
            if (!records.TryGetValue(item.Name, out var record)) continue;
            if (string.IsNullOrEmpty(record.SessionId))
            {
                var failed = NewItem(Label($"{record.Name} · {item.Detail}", muted: true),
                    new RailTag(RailKind.Heading, NeedsYouKey + ":failed:" + record.Name), item.Detail);
                if (IsEscalating(record.Name)) AddEscalationItems(failed, record.Name);
                section.Items.Add(failed);
                continue;
            }
            var listed = _agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
            var row = LiveRow(record, StateWithWaiting(listed));
            if (IsEscalating(record.Name)) AddEscalationItems(row, record.Name);
            section.Items.Add(row);
        }
        return section;
    }

    /// <summary>
    /// Whether the escalation holds a session: its item is one the owner must act on (D1). A row that is only a notice (overlap, conflict,
    /// limit) has no choices, since it never re-alerts.
    /// </summary>
    private bool IsEscalating(string name) => _attention.Any(i => i.Name == name && AttentionEscalation.Escalates(i));

    /// <summary>
    /// Adds the escalation's choices to the top of a Needs-you row's menu: Acknowledge, and snooze for one hour or four (D3, D4). Nothing is
    /// typed into the session; the choices only decide when the window sounds again. A row with no menu gets one.
    /// </summary>
    private void AddEscalationItems(TreeViewItem row, string name)
    {
        var menu = row.ContextMenu ??= StyledMenu();
        menu.Items.Insert(0, MenuItemFor("Acknowledge", () => AcknowledgeNeed(name)));
        menu.Items.Insert(1, MenuItemFor("Snooze 1 hour", () => SnoozeNeed(name, TimeSpan.FromHours(1))));
        menu.Items.Insert(2, MenuItemFor("Snooze 4 hours", () => SnoozeNeed(name, TimeSpan.FromHours(4))));
    }

    /// <summary>The owner acknowledged a waiting session: no more re-alerts for it until it stops waiting. It stays in the list.</summary>
    private void AcknowledgeNeed(string name)
    {
        _escalation.Acknowledge(name);
        AppLog.Write($"needs you: {name} acknowledged");
    }

    /// <summary>The owner snoozed a waiting session for a span: no re-alert until then, and one at the end if it still waits.</summary>
    private void SnoozeNeed(string name, TimeSpan span)
    {
        var until = DateTimeOffset.Now + span;
        _escalation.Snooze(name, until);
        AppLog.Write($"needs you: {name} snoozed until {until:HH:mm}");
    }

    /// <summary>
    /// Opening a session's tab acknowledges its wait (D3). Called where a tab becomes the active one, so nothing polls. A session the
    /// escalation does not hold has nothing to acknowledge.
    /// </summary>
    private void AcknowledgeSession(string sessionId)
    {
        var name = _attention.FirstOrDefault(i => SameId(i.SessionId, sessionId) && AttentionEscalation.Escalates(i))?.Name;
        if (name is not null) _escalation.Acknowledge(name);
    }

    /// <summary>
    /// The listed state of a session, with what it waits for when the listing says so (round 7 item 3): "blocked · waiting for permission prompt".
    /// A session the listing does not show is "not listed".
    /// </summary>
    private static string StateWithWaiting(AgentInfo? listed) =>
        listed is null ? "not listed"
        : listed.Waiting is { } waiting ? $"{listed.EffectiveState} · waiting for {waiting}"
        : listed.EffectiveState;

    /// <summary>
    /// The coders the last pass had ready for review, by session id, or null before the first pass (toasts by event). UI thread only.
    /// </summary>
    private IReadOnlySet<string>? _readyBefore;

    /// <summary>
    /// The limit types in force at the last pass, or null before the first one (toasts by event). The list is the last scan of the status bar, so
    /// nothing is scanned here. UI thread only.
    /// </summary>
    private IReadOnlySet<string>? _limitTypesBefore;

    /// <summary>
    /// Called after each pass. When a session joined the list since the last pass, the window flashes its taskbar button until it
    /// is in front and plays the notification sound once. Both are off when ui.json says "notify": false. When the window is not in front, a
    /// silent Windows toast is shown too, unless ui.json says "toast": false (toasts-2026-11-18); each kind of toast can be switched off
    /// (toasts by event). The first pass after the window opens only sets the baselines. The escalation runs on every pass: a session that still
    /// waits after its interval sounds again, with the same sound and flash, and is logged as "again" (escalation-2026-11-18). A coder that becomes
    /// ready for review, and a usage limit that resets, get a silent toast each when the window is not in front (toasts by event).
    /// </summary>
    private void AnnounceAttention()
    {
        var previous = _announced;
        _announced = _attention;
        var now = DateTimeOffset.Now;
        // Every pass updates the escalation, so a session that was already waiting when the window opened is counted from the first pass.
        // A re-alert that falls while "notify" is false is used up without a sound: the owner chose silence.
        var due = _escalation.Update(_attention, now, AttentionEscalation.IntervalOf(UiFile.Read(_options.StatePath, EscalateKey)));
        // The ready and limit states are read on every pass, shown or not, so each pass is compared with the one before it.
        var readyNew = ReadyGained();
        var limitsReset = LimitsReset(now);
        if (previous is null)
        {
            // The first pass sets the baseline: the notices in view are remembered, so they are not announced later on.
            _announcedNotices.UnionWith(AttentionList.NoticeKeys(_attention));
            return;
        }

        var gained = AttentionList.Gained(previous, _attention, _announcedNotices);
        // A notice is remembered once it has been in view, so a session that leaves done, or drops out of one listing, and returns with the same
        // files does not sound again.
        _announcedNotices.UnionWith(AttentionList.NoticeKeys(_attention));
        if (gained.Count > 0 || due.Count > 0) AnnounceWaits(gained, due);
        AnnounceQuiet(readyNew, limitsReset);
    }

    /// <summary>
    /// The sound, the flash and the Needs-you toast for the waits and re-alerts of a pass. The sound and the flash are as they were before toasts
    /// by event; the toast is filtered by the switches for its kinds.
    /// </summary>
    private void AnnounceWaits(IReadOnlyList<AttentionItem> gained, IReadOnlyList<ReAlert> due)
    {
        if (!AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, "notify"))) return;

        if (gained.Count > 0) AppLog.Write($"needs you: {string.Join(", ", gained.Select(g => g.Name))}");
        foreach (var again in due) AppLog.Write($"needs you (again, {again.Again}): {again.Item.Name}");
        SystemSounds.Asterisk.Play();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) TaskbarFlash.UntilFocused(handle);

        // A toast only when the window is not in front (toasts D2): minimised, or behind other windows.
        // "notify": false returned above, so it silences toasts too.
        if (!IsActive && AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, ToastKey)))
            ToastNotifier.Show(gained, due, ToastAllowed());
    }

    /// <summary>The reasons whose toast the owner has switched on (toasts by event), read from ui.json now.</summary>
    private Func<AttentionReason, bool> ToastAllowed()
    {
        var events = UiFile.Read(_options.StatePath, ToastCategories.Key);
        return reason => ToastCategories.Enabled(events, ToastCategories.Of(reason));
    }

    /// <summary>
    /// The silent toasts of a pass: a coder newly ready for review, and a usage limit that reset (toasts by event, 2026-11-18). No sound and no flash.
    /// Each is shown only when "notify" and "toast" are on, its own switch is on, and the window is not in front, as the Needs-you toast is.
    /// </summary>
    private void AnnounceQuiet(IReadOnlyList<ReviewItem> readyNew, IReadOnlyList<string> limitsReset)
    {
        if (readyNew.Count == 0 && limitsReset.Count == 0) return;
        if (IsActive) return;
        if (!AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, "notify"))) return;
        if (!AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, ToastKey))) return;

        var events = UiFile.Read(_options.StatePath, ToastCategories.Key);
        if (readyNew.Count > 0 && ToastCategories.Enabled(events, ToastCategory.Ready))
        {
            var names = readyNew.Select(item => item.Name).ToList();
            AppLog.Write($"ready for review: {string.Join(", ", names)}");
            ToastNotifier.ShowReady(names);
        }
        if (limitsReset.Count > 0 && ToastCategories.Enabled(events, ToastCategory.LimitReset))
        {
            AppLog.Write($"usage limit reset: {string.Join(", ", limitsReset)}");
            ToastNotifier.ShowLimitReset(limitsReset);
        }
    }

    /// <summary>
    /// The coders that joined the Ready-for-review list since the last pass. The first call sets the baseline and gives none, so the coders that
    /// were ready when the window opened are not announced.
    /// </summary>
    private IReadOnlyList<ReviewItem> ReadyGained()
    {
        var before = _readyBefore;
        _readyBefore = _review.Select(item => item.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (before is null) return [];
        return _review.Where(item => !before.Contains(item.SessionId)).ToList();
    }

    /// <summary>
    /// The labels of the usage limits that were in force at the last pass and are not in force now: they reset since then. The first call sets
    /// the baseline and gives none. A limit that is still in force, even with a later reset, is not a reset.
    /// </summary>
    private IReadOnlyList<string> LimitsReset(DateTimeOffset now)
    {
        var before = _limitTypesBefore;
        _limitTypesBefore = LimitStatus.TypesInForce(_activeLimits, now);
        if (before is null) return [];
        return LimitStatus.Lifted(before, _activeLimits, now);
    }

    /// <summary>
    /// The toast subscriptions (toasts D5). A click on a toast brings the window to the front; the history of this app's toasts is cleared
    /// when the window is activated, so old toasts do not pile up. The subscription ends with the window. A failure here is logged once and
    /// the window still opens: a toast is never worth a crash at start.
    /// </summary>
    private void InitToasts()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }
        catch (Exception ex)
        {
            ToastNotifier.LogFailure("click not handled", ex);
        }
        Activated += (_, _) => ToastNotifier.ClearHistory();
        Closing += (_, _) => UnsubscribeToasts();
    }

    private void UnsubscribeToasts()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        }
        catch (Exception ex)
        {
            ToastNotifier.LogFailure("click handler not removed", ex);
        }
    }

    /// <summary>
    /// A click on a toast: the window is shown and brought to the front. The library calls this on a background thread,
    /// so the work goes to the UI thread.
    /// </summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }));
}

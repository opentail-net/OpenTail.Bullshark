using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>Whether a change is alerted for the first time (<see cref="Change"/>), or is a re-alert that fell due (<see cref="Repeat"/>).</summary>
public enum AlertTrigger
{
    /// <summary>A change: a session started to wait, a notice came, or a coder became ready. The first alert for it.</summary>
    Change,

    /// <summary>A re-alert of a session that still waits, due under <c>alerts.repeatMinutes</c>.</summary>
    Repeat,
}

/// <summary>
/// The window state a banner and a sound depend on: whether the app window is in front, and the session the rail selects (null for none).
/// </summary>
/// <param name="WindowFocused">The app window is the active window.</param>
/// <param name="SelectedSessionId">The session id of the selected rail row, or null when the selection is not a session.</param>
public sealed record AlertFocus(bool WindowFocused, string? SelectedSessionId);

/// <summary>
/// The rules of an alert (behaviour batch, R2), as pure functions of the change, the window's focus and selection, and the settings. The attention list
/// keeps every change whatever these say; the rules decide only which changes make a banner and a sound.
/// </summary>
public static class AlertRules
{
    /// <summary>The kind of an item of the Needs-you list. Blocked is needs-input; the failures and the stop are errored; the rest are notices.</summary>
    public static AlertGroup KindOf(AttentionReason reason) => reason switch
    {
        AttentionReason.Blocked => AlertGroup.NeedsInput,
        AttentionReason.RestartLoop or AttentionReason.StartFailed or AttentionReason.Stopped or AttentionReason.Failed or AttentionReason.NameTaken
            => AlertGroup.Errored,
        _ => AlertGroup.Notice,
    };

    /// <summary>
    /// Whether one change makes a banner and a sound. It does not when the trigger is a re-alert and the re-alert interval is zero (the cooldown is off,
    /// and notices never repeat), or when the kind is not switched on. Then the focus gate decides. An unfocused window always gets the banner. A focused
    /// window gets none under the gate "unfocused"; under "unfocusedOrUnselected" it still gets one for any session other than the selected one, and none
    /// for the selected session, which the owner is already looking at. <paramref name="sessionId"/> is the session the change is about, or null for a
    /// change that has none (never selected, so a focused window still hears it under that gate).
    /// </summary>
    public static bool Shows(AlertGroup kind, AlertTrigger trigger, AlertFocus focus, string? sessionId, AlertsSettings alerts)
    {
        if (trigger == AlertTrigger.Repeat && (kind == AlertGroup.Notice || alerts.RepeatMinutes <= 0)) return false;
        if (kind != AlertGroup.Notice && !alerts.Kinds.Contains(kind)) return false;
        if (!focus.WindowFocused) return true;
        return alerts.FocusGate == FocusGate.UnfocusedOrUnselected && !IsSelected(focus, sessionId);
    }

    /// <summary>The items of the Needs-you list that alert for a change now. The input is not changed.</summary>
    public static IReadOnlyList<AttentionItem> Changes(IReadOnlyList<AttentionItem> gained, AlertFocus focus, AlertsSettings alerts) =>
        gained.Where(item => Shows(KindOf(item.Reason), AlertTrigger.Change, focus, item.SessionId, alerts)).ToList();

    /// <summary>The re-alerts that alert now. The input is not changed.</summary>
    public static IReadOnlyList<ReAlert> Repeats(IReadOnlyList<ReAlert> due, AlertFocus focus, AlertsSettings alerts) =>
        due.Where(alert => Shows(KindOf(alert.Item.Reason), AlertTrigger.Repeat, focus, alert.Item.SessionId, alerts)).ToList();

    /// <summary>The coders that became ready for review and alert now (kind finished, off by default). The input is not changed.</summary>
    public static IReadOnlyList<ReviewItem> Ready(IReadOnlyList<ReviewItem> ready, AlertFocus focus, AlertsSettings alerts) =>
        ready.Where(item => Shows(AlertGroup.Finished, AlertTrigger.Change, focus, item.SessionId, alerts)).ToList();

    /// <summary>Whether a change's session is the one the rail selects. A change with no session is never selected.</summary>
    private static bool IsSelected(AlertFocus focus, string? sessionId) =>
        sessionId is not null && focus.SelectedSessionId is not null
        && string.Equals(sessionId, focus.SelectedSessionId, StringComparison.OrdinalIgnoreCase);
}

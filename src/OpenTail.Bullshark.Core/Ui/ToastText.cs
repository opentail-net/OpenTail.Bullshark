using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>The title and the one body line of a Windows toast (toasts-2026-11-18, D1).</summary>
public sealed record ToastMessage(string Title, string Body);

/// <summary>
/// The words of the Windows toast that tells the owner a session needs them (toasts-2026-11-18). Pure: no window, no clock, no file.
/// The text is the session name and a short reason only. It never holds a prompt, a path, a file's contents or what a session is waiting
/// for: the reasons are words chosen here, not the detail the window shows.
/// </summary>
public static class ToastText
{
    /// <summary>The title of a toast for a session that started to wait (or a notice that is new).</summary>
    public const string NeedsYouTitle = "Needs you";

    /// <summary>The title of a re-alert: a session that still waits after its interval.</summary>
    public const string StillWaitingTitle = "Still waiting";

    /// <summary>The longest session name a toast shows; a longer one is cut and ends with an ellipsis, so the text stays at this length.</summary>
    public const int MaxNameLength = 60;

    /// <summary>
    /// The toast for one announcement batch: the sessions that joined the Needs-you list (<paramref name="gained"/>) and the re-alerts that fell
    /// due (<paramref name="due"/>), in that order. The body names the first session and its reason, and says "and N more" for the other sessions
    /// (each session counted once, by name). The title is "Needs you" when a session is new, and "Still waiting" for re-alerts only.
    /// Returns null when there is nothing to show, so no toast is made.
    /// <paramref name="allow"/> (toasts by event) keeps only the items whose reason it allows; without it every reason is allowed. An item the
    /// filter drops is not counted in the body or the title, so a batch the owner switched off in full gives null.
    /// </summary>
    public static ToastMessage? Build(IReadOnlyList<AttentionItem> gained, IReadOnlyList<ReAlert> due, Func<AttentionReason, bool>? allow = null)
    {
        ArgumentNullException.ThrowIfNull(gained);
        ArgumentNullException.ThrowIfNull(due);
        var permitted = allow ?? (_ => true);

        var newWaits = gained.Where(item => permitted(item.Reason)).ToList();
        var sessions = newWaits.Select(item => (item.Name, item.Reason))
            .Concat(due.Where(alert => permitted(alert.Item.Reason)).Select(alert => (alert.Item.Name, alert.Item.Reason)))
            .ToList();
        if (sessions.Count == 0) return null;

        var first = sessions[0];
        var count = sessions.Select(session => session.Name).Distinct(StringComparer.Ordinal).Count();
        var body = $"{ShortName(first.Name)} · {ReasonText(first.Reason)}";
        if (count > 1) body += $", and {count - 1} more";

        return new ToastMessage(newWaits.Count > 0 ? NeedsYouTitle : StillWaitingTitle, body);
    }

    /// <summary>The title of the silent toast for coders that are ready for review (toasts by event).</summary>
    public const string ReadyTitle = "Ready for review";

    /// <summary>The title of the silent toast for a usage limit that reset (toasts by event).</summary>
    public const string LimitResetTitle = "Usage limit reset";

    /// <summary>The title of the toast for a supervisor pass that failed (reliability batch R9).</summary>
    public const string ProblemTitle = "Supervisor problem";

    /// <summary>
    /// The toast for coders that newly joined the Ready-for-review list: the first name, and "and N more" for the others (each name counted once).
    /// The names are the session names only, never a path or a detail. Null when there are none.
    /// </summary>
    public static ToastMessage? Ready(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var distinct = names.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0) return null;

        var body = ShortName(distinct[0]);
        if (distinct.Count > 1) body += $", and {distinct.Count - 1} more";
        return new ToastMessage(ReadyTitle, body);
    }

    /// <summary>
    /// The toast for usage limits that reset: the labels of the limits (for example "5-hour"), each once, joined with commas. The labels are
    /// those of <see cref="Setup.LimitStatus.LabelOf"/>. Null when there are none.
    /// </summary>
    /// <summary>The toast for a supervisor pass that failed: its short reason, which names no folder (see <c>PassHealth.Short</c>).</summary>
    public static ToastMessage? Problem(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return string.IsNullOrWhiteSpace(reason) ? null : new ToastMessage(ProblemTitle, reason.Trim());
    }

    public static ToastMessage? LimitReset(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var distinct = labels.Where(label => !string.IsNullOrWhiteSpace(label)).Select(ShortName).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0) return null;
        return new ToastMessage(LimitResetTitle, string.Join(", ", distinct));
    }

    /// <summary>The short words for a reason. Fixed here, so the toast never carries the detail of the item.</summary>
    public static string ReasonText(AttentionReason reason) => reason switch
    {
        AttentionReason.Blocked => "blocked",
        AttentionReason.RestartLoop => "restart loop",
        AttentionReason.StartFailed => "start failed",
        AttentionReason.Stopped => "stopped",
        AttentionReason.Failed => "failed",
        AttentionReason.Overlap => "shares a file with another session",
        AttentionReason.Conflict => "conflicts with the default branch",
        AttentionReason.LimitReached => "run limit reached",
        _ => "needs you",
    };

    /// <summary>The name as shown: a name longer than <see cref="MaxNameLength"/> is cut to that length, and the last character is an ellipsis.</summary>
    public static string ShortName(string name) =>
        name.Length <= MaxNameLength ? name : string.Concat(name.AsSpan(0, MaxNameLength - 1), "…");
}

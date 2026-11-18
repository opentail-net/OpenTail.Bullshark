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
    /// </summary>
    public static ToastMessage? Build(IReadOnlyList<AttentionItem> gained, IReadOnlyList<ReAlert> due)
    {
        ArgumentNullException.ThrowIfNull(gained);
        ArgumentNullException.ThrowIfNull(due);

        var sessions = gained.Select(item => (item.Name, item.Reason))
            .Concat(due.Select(alert => (alert.Item.Name, alert.Item.Reason)))
            .ToList();
        if (sessions.Count == 0) return null;

        var first = sessions[0];
        var count = sessions.Select(session => session.Name).Distinct(StringComparer.Ordinal).Count();
        var body = $"{ShortName(first.Name)} · {ReasonText(first.Reason)}";
        if (count > 1) body += $", and {count - 1} more";

        return new ToastMessage(gained.Count > 0 ? NeedsYouTitle : StillWaitingTitle, body);
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

using CommunityToolkit.WinUI.Notifications;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Windows toasts (toasts-2026-11-18, toasts by event 2026-11-18). The Needs-you toast is one per announcement batch, with the session name and
/// the reason only (<see cref="ToastText"/>). The ready-for-review and usage-limit toasts are silent and name a session or a limit only. Each kind has
/// its own tag, so one kind does not replace another in the action centre. Every toast is silent: the window's own sound is the one ding. A toast
/// that cannot be shown is logged once and never thrown into the pass. Called on the UI thread only.
/// </summary>
internal static class ToastNotifier
{
    /// <summary>The tag and group of the Needs-you toast: a newer one replaces the older one in the action centre, so they do not stack.</summary>
    private const string NeedsTag = "needs-you";

    /// <summary>The tag of the toast for coders ready for review.</summary>
    private const string ReadyTag = "ready-for-review";

    /// <summary>The tag of the toast for a usage limit that reset.</summary>
    private const string LimitTag = "limit-reset";

    /// <summary>The tag of the toast for a limit-reset session that is still running (limit wake).</summary>
    private const string LimitWakeTag = "limit-wake";

    /// <summary>The tag of the toast for a supervisor pass that failed (reliability batch R9).</summary>
    private const string ProblemTag = "supervisor-problem";

    /// <summary>Set after the first failure: later failures are not logged, so a broken toast does not fill the log.</summary>
    private static bool _failureLogged;

    /// <summary>
    /// Shows the Needs-you toast for the items that joined the list and the re-alerts that fell due. <paramref name="allow"/> keeps only the reasons
    /// whose category is on (toasts by event); without it every reason is shown. Nothing is shown when there is no text.
    /// </summary>
    public static void Show(IReadOnlyList<AttentionItem> gained, IReadOnlyList<ReAlert> due, Func<AttentionReason, bool>? allow = null) =>
        Post(() => ToastText.Build(gained, due, allow), NeedsTag);

    /// <summary>The silent toast for coders that newly joined the Ready-for-review list. Names only.</summary>
    public static void ShowReady(IReadOnlyList<string> names) => Post(() => ToastText.Ready(names), ReadyTag);

    /// <summary>The toast for a supervisor pass that failed, once when it starts (reliability batch R9). Its category is the caller's to check.</summary>
    public static void ShowProblem(string reason) => Post(() => ToastText.Problem(reason), ProblemTag);

    /// <summary>The silent toast for usage limits that reset: their labels, such as "5-hour".</summary>
    public static void ShowLimitReset(IReadOnlyList<string> labels) => Post(() => ToastText.LimitReset(labels), LimitTag);

    /// <summary>The silent toast for a session that stopped at the usage limit, whose reset has passed, and that is still running (limit wake).</summary>
    public static void ShowLimitWake(string name) => Post(() => ToastText.LimitWake(name), LimitWakeTag);

    /// <summary>Removes this app's toasts from the action centre, so old ones do not pile up. Called when the window is activated.</summary>
    public static void ClearHistory()
    {
        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch (Exception ex)
        {
            LogFailure("history not cleared", ex);
        }
    }

    /// <summary>Logs a toast failure: the first one of the run only. Callers catch what they touch and pass it here.</summary>
    public static void LogFailure(string what, Exception ex)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        AppLog.Write($"toast: {what} ({ex.GetType().Name}: {ex.Message}). Later toast failures are not logged.");
    }

    /// <summary>Shows one silent toast with its tag. Nothing is shown for a null message.</summary>
    private static void Post(Func<ToastMessage?> build, string tag)
    {
        try
        {
            var message = build();
            if (message is null) return;

            new ToastContentBuilder()
                .AddText(message.Title)
                .AddText(message.Body)
                .AddAudio(new ToastAudio { Silent = true })
                .Show(toast =>
                {
                    toast.Tag = tag;
                    toast.Group = tag;
                });
        }
        catch (Exception ex)
        {
            LogFailure("not shown", ex);
        }
    }
}

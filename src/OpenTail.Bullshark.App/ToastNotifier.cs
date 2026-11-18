using CommunityToolkit.WinUI.Notifications;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Windows toast for a session that needs the owner (toasts-2026-11-18). One toast per announcement batch, with the session name and the
/// reason only (<see cref="ToastText"/>). The toast is silent: the window's own sound is the one ding. A toast that cannot be shown is logged
/// once and never thrown into the pass. Called on the UI thread only.
/// </summary>
internal static class ToastNotifier
{
    /// <summary>The tag and group of every toast: a newer toast replaces the older one in the action centre, so they do not stack.</summary>
    private const string Tag = "needs-you";

    /// <summary>Set after the first failure: later failures are not logged, so a broken toast does not fill the log.</summary>
    private static bool _failureLogged;

    /// <summary>Shows the toast for the items that joined the list and the re-alerts that fell due. Nothing is shown when there is no text.</summary>
    public static void Show(IReadOnlyList<AttentionItem> gained, IReadOnlyList<ReAlert> due)
    {
        try
        {
            var message = ToastText.Build(gained, due);
            if (message is null) return;

            new ToastContentBuilder()
                .AddText(message.Title)
                .AddText(message.Body)
                .AddAudio(new ToastAudio { Silent = true })
                .Show(toast =>
                {
                    toast.Tag = Tag;
                    toast.Group = Tag;
                });
        }
        catch (Exception ex)
        {
            LogFailure("not shown", ex);
        }
    }

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
}

using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>What a pass's waits and re-alerts do: the sound, the taskbar flash and the Needs-you toast (perf and testability batch, Q6, part d).</summary>
/// <param name="Sounds">The notification sound plays.</param>
/// <param name="Flashes">The taskbar button flashes until the window is in front.</param>
/// <param name="Toasts">A Needs-you toast is shown, for the kinds the owner has switched on.</param>
/// <param name="LogsOnly">Nothing sounds, flashes or pops up; the waits are written to the log (quiet hours).</param>
public sealed record WaitAnnouncement(bool Sounds, bool Flashes, bool Toasts, bool LogsOnly);

/// <summary>
/// The rules of the announcements (perf and testability batch, Q6, part d), as pure functions of the pass's settings and the window's state. The
/// window draws what they say. "notify" off silences the sound, the flash and every toast; quiet hours silence them too and only log; a window in front
/// gets no toast; and each kind of toast has its own switch.
/// </summary>
public static class AnnouncePolicy
{
    /// <summary>The sound, the flash and the Needs-you toast of a pass's waits and re-alerts.</summary>
    /// <param name="settings">The pass's ui.json values.</param>
    /// <param name="windowActive">Whether the window is in front.</param>
    /// <param name="quiet">Whether the time is inside quiet hours.</param>
    public static WaitAnnouncement ForWaits(PassSettings settings, bool windowActive, bool quiet)
    {
        if (!settings.Notify) return new WaitAnnouncement(Sounds: false, Flashes: false, Toasts: false, LogsOnly: false);
        if (quiet) return new WaitAnnouncement(Sounds: false, Flashes: false, Toasts: false, LogsOnly: true);
        return new WaitAnnouncement(Sounds: true, Flashes: true, Toasts: !windowActive && settings.Toast, LogsOnly: false);
    }

    /// <summary>
    /// Whether a silent toast of one kind (a coder ready for review, a usage limit that reset) is shown: "notify" and "toast" on, the kind's own switch
    /// on, the window not in front, and not in quiet hours.
    /// </summary>
    public static bool SilentToastShown(PassSettings settings, ToastCategory category, bool windowActive, bool quiet) =>
        settings.Notify && settings.Toast && settings.ToastOn(category) && !windowActive && !quiet;

    /// <summary>Whether the Problems toast of a supervisor pass that started failing is shown (the same switches as <see cref="SilentToastShown"/>).</summary>
    public static bool ProblemToastShown(PassSettings settings, bool windowActive, bool quiet) =>
        SilentToastShown(settings, ToastCategory.Problems, windowActive, quiet);

    /// <summary>Whether a Needs-you toast may name a wait of this reason (toasts by event): the switch of its kind.</summary>
    public static bool WaitToastAllowed(PassSettings settings, AttentionReason reason) =>
        settings.ToastOn(ToastCategories.Of(reason));
}

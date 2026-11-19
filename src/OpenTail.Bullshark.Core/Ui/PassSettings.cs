using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// The ui.json values one supervisor pass uses, read once as a set (perf and testability batch, Q2). The pass reads ui.json on a pool thread and
/// hands this record to the drawing and to the announcement, so the UI thread does not read the file on the pass path. Each value takes the default
/// its own reader gives when the key is missing or not of its type, so one file gives the same answers as the separate reads did.
/// </summary>
/// <param name="Notify">The "notify" switch: sound and flash for new waits (and the toasts with it). On when missing.</param>
/// <param name="Toast">The "toast" switch: a toast when the window is in the background. On when missing.</param>
/// <param name="ToastEvents">Each kind of toast's switch ("toastEvents"). On when missing.</param>
/// <param name="Quiet">The quiet-hours window ("quietHours"), or null for off.</param>
/// <param name="ContextWindow">The ctx meter's window in tokens ("contextWindow"). The assumed default when missing.</param>
/// <param name="SilentAfter">The silent-working threshold ("silentMinutes"). Zero turns it off.</param>
/// <param name="EscalateEvery">The re-alert interval ("escalateMinutes"). Zero turns it off.</param>
/// <param name="AttentionFile">Whether the attention file is written on each pass ("attentionFile"). Off when missing.</param>
public sealed record PassSettings(
    bool Notify,
    bool Toast,
    IReadOnlyDictionary<ToastCategory, bool> ToastEvents,
    QuietWindow? Quiet,
    long ContextWindow,
    TimeSpan SilentAfter,
    TimeSpan EscalateEvery,
    bool AttentionFile)
{
    /// <summary>The ui.json key of the sound and flash switch.</summary>
    public const string NotifyKey = "notify";

    /// <summary>The ui.json key of the background toast switch.</summary>
    public const string ToastKey = "toast";

    /// <summary>The ui.json key of the ctx meter's window.</summary>
    public const string ContextWindowKey = "contextWindow";

    /// <summary>The ui.json key of the silent-working threshold, in minutes.</summary>
    public const string SilentKey = "silentMinutes";

    /// <summary>The ui.json key of the re-alert interval, in minutes.</summary>
    public const string EscalateKey = "escalateMinutes";

    /// <summary>The values when ui.json is missing or cannot be read: what every reader gives for a missing key.</summary>
    public static PassSettings Defaults { get; } = From(null);

    /// <summary>
    /// The settings in a ui.json object, as the file holds them. Null (no file, or a file that is not an object) gives the defaults. Never throws.
    /// </summary>
    public static PassSettings From(JsonObject? root)
    {
        var events = new Dictionary<ToastCategory, bool>();
        var eventsNode = root?[ToastCategories.Key];
        foreach (var category in Enum.GetValues<ToastCategory>())
            events[category] = ToastCategories.Enabled(eventsNode, category);
        return new PassSettings(
            Notify: AttentionList.NotificationsOn(root?[NotifyKey]),
            Toast: AttentionList.NotificationsOn(root?[ToastKey]),
            ToastEvents: events,
            Quiet: QuietHours.Parse(root?[QuietHours.Key]),
            ContextWindow: ContextMeter.WindowFrom(root?[ContextWindowKey]),
            SilentAfter: SilenceCheck.ThresholdOf(root?[SilentKey]),
            EscalateEvery: AttentionEscalation.IntervalOf(root?[EscalateKey]),
            AttentionFile: Supervisor.AttentionFile.Enabled(root?[Supervisor.AttentionFile.Key]));
    }

    /// <summary>Whether the toast of one kind is switched on.</summary>
    public bool ToastOn(ToastCategory category) => ToastEvents.TryGetValue(category, out var on) && on;

    /// <summary>True when the quiet-hours window is set and <paramref name="now"/> is inside it.</summary>
    public bool IsQuietAt(DateTimeOffset now) => QuietHours.IsQuietAt(Quiet, now);
}

using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// A window of quiet hours: from <see cref="From"/> (inclusive) to <see cref="To"/> (exclusive), in local time. A window may cross midnight
/// (22:00 to 07:00). Quiet hours are off when there is no window (night batch N4).
/// </summary>
public sealed record QuietWindow(TimeOnly From, TimeOnly To);

/// <summary>
/// Quiet hours (night batch N4): the window kept as <c>quietHours</c> in ui.json, <c>{"from":"22:00","to":"07:00"}</c>, or null for off. While
/// quiet, the window silences sound, flash and toasts for every alert. It never hides anything from the rail, the log or the digest, and it
/// never changes the re-alert count (the escalation still updates). Off by default.
/// </summary>
public static class QuietHours
{
    /// <summary>The ui.json key that holds the window.</summary>
    public const string Key = "quietHours";

    private const string TimeFormat = "HH:mm";

    /// <summary>The choices the owner picks from in Settings (the Off choice is null and is not listed here).</summary>
    public static readonly IReadOnlyList<QuietWindow> Presets =
    [
        new(new TimeOnly(22, 0), new TimeOnly(7, 0)),
        new(new TimeOnly(23, 0), new TimeOnly(8, 0)),
        new(new TimeOnly(0, 0), new TimeOnly(9, 0)),
    ];

    /// <summary>
    /// The window in a ui.json value. Null, a value that is not an object, a missing or unreadable time, or a window whose two times are equal
    /// (from == to) is off, so junk never turns quiet hours on. Never throws.
    /// </summary>
    public static QuietWindow? Parse(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        if (!TimeOf(obj, "from", out var from) || !TimeOf(obj, "to", out var to)) return null;
        return from == to ? null : new QuietWindow(from, to);
    }

    /// <summary>
    /// True when <paramref name="now"/> is inside the window: from is inclusive, to is exclusive. A window that crosses midnight (from later than
    /// to) runs from from to midnight and from midnight to to. From equal to to is off, so nothing is quiet.
    /// </summary>
    public static bool IsQuiet(TimeOnly now, TimeOnly from, TimeOnly to)
    {
        if (from == to) return false;
        return from < to
            ? now >= from && now < to
            : now >= from || now < to;
    }

    /// <summary>True when the window is set and <paramref name="now"/> (converted to local time) is inside it. Off is never quiet.</summary>
    public static bool IsQuietAt(QuietWindow? window, DateTimeOffset now) =>
        window is { } w && IsQuiet(TimeOnly.FromDateTime(now.LocalDateTime), w.From, w.To);

    /// <summary>"off", or the window as "22:00-07:00".</summary>
    public static string Label(QuietWindow? window) =>
        window is { } w ? $"{Format(w.From)}-{Format(w.To)}" : "off";

    /// <summary>The ui.json value for a window: null for off, else the object <see cref="Parse"/> reads.</summary>
    public static JsonNode? ToNode(QuietWindow? window) => window is { } w
        ? new JsonObject { ["from"] = Format(w.From), ["to"] = Format(w.To) }
        : null;

    private static string Format(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static bool TimeOf(JsonObject obj, string name, out TimeOnly time)
    {
        time = default;
        return obj.TryGetPropertyValue(name, out var value)
            && value is JsonValue json
            && json.TryGetValue<string>(out var text)
            && TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}

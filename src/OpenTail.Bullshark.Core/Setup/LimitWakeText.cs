using System.Globalization;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// The words a session shows when it stopped at the usage limit (limit wake, 2026-11-20): on its row's second line and in Needs you. Pure: the
/// time zone is passed in (the local one by default), so a test can fix it.
/// <para>
/// The head is <c>Limit resets 07:20</c> before the reset, with the day when it is not today (<c>Limit resets Thu 07:20</c>), and <c>Limit has reset</c>
/// after it. The tail comes from the wake decision: <c>· wakes after</c> while it waits, <c>· waking now</c> when it is being started, and the note
/// of a notice. Nothing is shown when there is no stop, or the stop is too old to show.
/// </para>
/// </summary>
public static class LimitWakeText
{
    /// <summary>The text for a session's stop, or null when there is nothing to show.</summary>
    public static string? Line(LimitStop? stop, LimitWakeDecision? decision, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (stop is null || !stop.IsRecent(now)) return null;

        var head = now < stop.ResetsAt ? "Limit resets " + ClockOf(stop.ResetsAt, now, zone) : "Limit has reset";
        var tail = decision switch
        {
            { Action: LimitWakeAction.Wait } => " · wakes after",
            { Action: LimitWakeAction.WakeNow } => " · waking now",
            { Action: LimitWakeAction.Notify, Note: { Length: > 0 } note } => " · " + note,
            _ => string.Empty,
        };
        return head + tail;
    }

    /// <summary>The reset as the owner reads it: the local 24-hour time, with the day when it is not the day of <paramref name="now"/>.</summary>
    public static string ClockOf(DateTimeOffset when, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var tz = zone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(when, tz);
        var today = TimeZoneInfo.ConvertTime(now, tz).Date;
        return local.Date == today
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }
}

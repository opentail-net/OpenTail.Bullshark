using System.Globalization;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// The optional start time of a run (gems item 5). The owner types a 24-hour "HH:mm" on this computer's clock, and the run starts
/// at the next time the clock shows that: today if it is still ahead, otherwise tomorrow. Blank means no start time (the run starts
/// at once). A start time is only kept while the window runs, so the setup page says so.
/// </summary>
public static class StartTime
{
    /// <summary>
    /// The start time for <paramref name="text"/> after <paramref name="now"/>, or the reason the text is not a time. Blank gives no time
    /// and no reason. Hours are 0 to 23, minutes 0 to 59; "9:05" is accepted.
    /// </summary>
    public static (DateTimeOffset? At, string? Error) Parse(string? text, DateTimeOffset now) =>
        Parse(text, now, TimeZoneInfo.Local);

    /// <summary>
    /// The same for a given clock. The next time is worked out as a wall-clock time in <paramref name="zone"/>, and then turned into an
    /// instant with the offset that applies on that day. Moving an instant by a day keeps the old offset, which is one hour out when the
    /// clocks change between the two days, so it is not used (round 5 item 13).
    /// </summary>
    public static (DateTimeOffset? At, string? Error) Parse(string? text, DateTimeOffset now, TimeZoneInfo zone)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return (null, null);

        var parts = trimmed.Split(':');
        int hour = 0, minute = 0;
        var valid = parts.Length == 2
                    && parts[0].Length is >= 1 and <= 2
                    && parts[1].Length == 2
                    && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)
                    && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute)
                    && hour is >= 0 and <= 23
                    && minute is >= 0 and <= 59;
        if (!valid) return (null, $"'{trimmed}' is not a time. Write it as HH:mm, for example 14:30.");

        var today = TimeZoneInfo.ConvertTime(now, zone).DateTime.Date;
        var at = Occurrence(today.AddHours(hour).AddMinutes(minute), zone);
        if (at <= now) at = Occurrence(today.AddDays(1).AddHours(hour).AddMinutes(minute), zone);
        return (at, null);
    }

    /// <summary>The instant of a wall-clock time in the zone, with the offset that applies at that time.</summary>
    private static DateTimeOffset Occurrence(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    /// <summary>
    /// A start time as the rail shows it: the time alone when it falls on today's date in the zone, and the date and the time when it does
    /// not (round 5 item 13). A waiting run may be a day or more away, so its row names the day.
    /// </summary>
    public static string Describe(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var clock = zone ?? TimeZoneInfo.Local;
        var when = TimeZoneInfo.ConvertTime(at, clock).DateTime;
        var today = TimeZoneInfo.ConvertTime(now, clock).DateTime.Date;
        return when.Date == today
            ? when.ToString("HH:mm", CultureInfo.InvariantCulture)
            : when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}

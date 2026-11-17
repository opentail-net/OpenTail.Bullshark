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
    public static (DateTimeOffset? At, string? Error) Parse(string? text, DateTimeOffset now)
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

        var local = now.ToLocalTime().DateTime;
        var date = new DateTime(local.Year, local.Month, local.Day, hour, minute, 0, DateTimeKind.Unspecified);
        var at = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
        if (at <= now) at = at.AddDays(1);
        return (at, null);
    }
}

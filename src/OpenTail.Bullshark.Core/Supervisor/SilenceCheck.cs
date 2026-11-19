using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The silent-working notice (lifecycle batch L3): a session listed as `working` that has made no API request for the threshold. It is a notice,
/// not an alert: nothing changes in the state records, and nothing is sent to the session. A long command (a build, a test run) can trip it, so
/// the notice says so. The request time comes from the transcript facts the window already keeps (<c>LastRequest</c>); this class reads no file.
/// Pure: the time is passed in. The threshold comes from ui.json <c>silentMinutes</c>, read by <see cref="ThresholdOf"/>.
/// </summary>
public static class SilenceCheck
{
    /// <summary>The threshold when ui.json has no usable <c>silentMinutes</c> (lifecycle batch L3, an assumption until the owner tries it).</summary>
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMinutes(45);

    private const double ShortestMinutes = 5;
    private const double LongestMinutes = 1440;

    /// <summary>
    /// The names of the sessions listed as `working` whose last API request is at least <paramref name="threshold"/> old. A session with no known
    /// request time is not flagged (its time is unknown, not silent), and a request time ahead of <paramref name="now"/> is not flagged either. A
    /// zero threshold (the setting is off) flags nothing. A session in any other listed state is not flagged: a blocked one waits for the owner,
    /// and a stale one is not running.
    /// </summary>
    public static IReadOnlyList<string> Find(
        IEnumerable<(string Name, string SessionId, string State, DateTimeOffset? LastRequest)> live,
        DateTimeOffset now,
        TimeSpan threshold)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (threshold <= TimeSpan.Zero) return [];
        return live
            .Where(session => string.Equals(session.State, "working", StringComparison.OrdinalIgnoreCase)
                              && session.LastRequest is { } last && now - last >= threshold)
            .Select(session => session.Name)
            .ToList();
    }

    /// <summary>
    /// The threshold from ui.json's <c>silentMinutes</c>. A missing value, a value that is not a number, or a negative one gives
    /// <see cref="DefaultThreshold"/>. Zero is off (<see cref="TimeSpan.Zero"/>). Any other number is clamped to 5 to 1440 minutes.
    /// </summary>
    public static TimeSpan ThresholdOf(JsonNode? value)
    {
        // The node's JSON text, so a number made in code and one read from the file are read alike; a string or a bool is not a number.
        if (value is not JsonValue json
            || !double.TryParse(json.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || minutes < 0)
            return DefaultThreshold;
        if (minutes == 0) return TimeSpan.Zero;
        return TimeSpan.FromMinutes(Math.Clamp(minutes, ShortestMinutes, LongestMinutes));
    }
}

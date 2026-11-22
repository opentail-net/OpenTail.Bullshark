using OpenTail.Bullshark.Core.Behaviour;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// The one time a session's usage is attributed to, which decides the day it counts on (<c>usage.attribution</c>, R8). The rule is applied to each
/// usage record before the copies of a message are combined, and the window filter is applied after that. Pure: no file is read.
/// </summary>
public static class UsageAttributionRule
{
    /// <summary>
    /// The time a usage record counts at: the session's last update (<see cref="UsageAttribution.LastUpdate"/>, the default), its start
    /// (<see cref="UsageAttribution.Start"/>), or the record's own time (<see cref="UsageAttribution.Event"/>).
    /// </summary>
    /// <param name="attribution">The knob.</param>
    /// <param name="eventTime">The record's own time.</param>
    /// <param name="sessionStart">The time of the session's first usage record.</param>
    /// <param name="sessionLastUpdate">The time of the session's last usage record.</param>
    public static DateTimeOffset TimeOf(UsageAttribution attribution, DateTimeOffset eventTime, DateTimeOffset sessionStart, DateTimeOffset sessionLastUpdate) =>
        attribution switch
        {
            UsageAttribution.LastUpdate => sessionLastUpdate,
            UsageAttribution.Start => sessionStart,
            UsageAttribution.Event => eventTime,
            _ => throw new ArgumentOutOfRangeException(nameof(attribution), attribution, "Not a usage attribution."),
        };
}

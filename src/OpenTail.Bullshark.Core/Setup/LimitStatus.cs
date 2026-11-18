namespace OpenTail.Bullshark.Core.Setup;

/// <summary>One usage limit in force: its type as Claude Code wrote it (for example <c>five_hour</c>), and the time it resets.</summary>
public sealed record ActiveLimit(string Type, DateTimeOffset ResetsAt);

/// <summary>
/// The usage limits in force, for the status bar (A2). A limit is in force when its latest entry is rejected and it resets after the
/// given time. The same reader as <see cref="LimitReset"/> is used, so the two agree on what a limit is.
/// </summary>
public static class LimitStatus
{
    private const string Rejected = "rejected";

    /// <summary>
    /// The limits in force at <paramref name="now"/>, one per type: the latest reset of each type. The list is sorted with the limit that
    /// governs first (the latest reset). A missing or empty folder gives an empty list; a file that cannot be read is skipped.
    /// </summary>
    public static IReadOnlyList<ActiveLimit> Active(string projectsRoot, DateTimeOffset now)
    {
        var latest = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var file in LimitEntries.Recent(projectsRoot, now.UtcDateTime - LimitEntries.LimitWindow))
        {
            foreach (var entry in LimitEntries.Tail(file.FullName))
            {
                if (entry.Status != Rejected || entry.ResetsAt <= now) continue;
                if (!latest.TryGetValue(entry.Type, out var known) || entry.ResetsAt > known) latest[entry.Type] = entry.ResetsAt;
            }
        }
        return latest
            .Select(pair => new ActiveLimit(pair.Key, pair.Value))
            .OrderByDescending(limit => limit.ResetsAt)
            .ThenBy(limit => limit.Type, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The name a person reads for a limit type: "5-hour" for <c>five_hour</c>, "weekly" for <c>weekly</c> and <c>seven_day</c>, and the raw text
    /// for any other type (Claude Code also writes model limits, and the list is not fixed).
    /// </summary>
    public static string LabelOf(string type) => type switch
    {
        "five_hour" => "5-hour",
        "weekly" or "seven_day" => "weekly",
        // seven_day_opus, seven_day_overage_included and the like: a weekly limit, perhaps of one model (owner's screenshot, 2026-11-18).
        _ when type.StartsWith("seven_day", StringComparison.Ordinal) => "weekly",
        _ => type,
    };

    /// <summary>The limit types in force at <paramref name="now"/> (a reset time after it), from a list that <see cref="Active"/> gave.</summary>
    public static IReadOnlySet<string> TypesInForce(IEnumerable<ActiveLimit> limits, DateTimeOffset now) =>
        limits.Where(limit => limit.ResetsAt > now).Select(limit => limit.Type).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The labels of the limit types that were in force (<paramref name="before"/>) and are not in force now: the limits that reset since the last
    /// look. A type that is still in force, even with a later reset, is not listed. Each label is given once, in type order.
    /// </summary>
    public static IReadOnlyList<string> Lifted(IReadOnlySet<string> before, IEnumerable<ActiveLimit> limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(before);
        var stillIn = TypesInForce(limits, now);
        return before.Where(type => !stillIn.Contains(type))
            .OrderBy(type => type, StringComparer.Ordinal)
            .Select(LabelOf)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

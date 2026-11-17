using System.Globalization;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The limits of one run (additions item 4). A null field means no limit on that measure. Output tokens are the tokens the run's
/// sessions wrote out, and the duration is the time since the run was created.
/// </summary>
public sealed record RunLimits(long? MaxOutputTokens, TimeSpan? MaxDuration)
{
    public bool IsEmpty => MaxOutputTokens is null && MaxDuration is null;

    /// <summary>
    /// Reads the two setup-page fields. Blank means no limit. A value that is not a positive whole number is refused with the reason,
    /// and nothing is limited then.
    /// </summary>
    public static (RunLimits? Limits, string? Error) Parse(string? outputTokens, string? minutes)
    {
        long? maxTokens = null;
        TimeSpan? maxDuration = null;

        var tokens = (outputTokens ?? string.Empty).Trim();
        if (tokens.Length > 0)
        {
            if (!long.TryParse(tokens, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
                return (null, $"'{tokens}' is not a whole number of output tokens above zero.");
            maxTokens = count;
        }

        var span = (minutes ?? string.Empty).Trim();
        if (span.Length > 0)
        {
            if (!int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out var mins) || mins <= 0)
                return (null, $"'{span}' is not a whole number of minutes above zero.");
            maxDuration = TimeSpan.FromMinutes(mins);
        }

        var limits = new RunLimits(maxTokens, maxDuration);
        return (limits.IsEmpty ? null : limits, null);
    }
}

/// <summary>What a run has used: the output tokens of its sessions, and the time since it was created.</summary>
public sealed record RunSpend(long OutputTokens, TimeSpan Elapsed)
{
    /// <summary>
    /// The run's spend. A session is in the run when its spec's name has a state record with a session id (the run record holds no
    /// session ids). Each session counts once, from its transcript's usage. A clock that runs backwards gives no time, not a negative one.
    /// </summary>
    public static RunSpend Of(
        RunRecord run,
        IReadOnlyDictionary<string, SessionRecord> records,
        IEnumerable<SessionWrites> writes,
        DateTimeOffset now)
    {
        var list = writes.ToList();
        long output = 0;
        foreach (var spec in run.Specs.DistinctBy(s => s.Name, StringComparer.Ordinal))
        {
            if (!records.TryGetValue(spec.Name, out var record) || string.IsNullOrEmpty(record.SessionId)) continue;
            var found = list.FirstOrDefault(w => string.Equals(w.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
            output += found?.Usage?.Output ?? 0;
        }
        return new RunSpend(output, ElapsedOf(run, now));
    }

    /// <summary>
    /// The time a run has run for at <paramref name="now"/>. It counts from the run's start time once that time has come, and from its
    /// creation when it has none. A run that waits for its start time has run for nothing yet, so it never reaches a limit before it
    /// starts (round 5 item 3). A clock that runs backwards gives no time, not a negative one.
    /// </summary>
    public static TimeSpan ElapsedOf(RunRecord run, DateTimeOffset now)
    {
        if (run.StartAt is { } startAt && startAt > now) return TimeSpan.Zero;
        var origin = run.StartAt is { } start && start > run.CreatedAt ? start : run.CreatedAt;
        var elapsed = now - origin;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}

/// <summary>
/// How far a run is through its limits: the larger of its token and time fractions, where 1 is the limit. The policy alerts at
/// <see cref="NearFraction"/> and stops new starts and resumes at 1.
/// </summary>
public sealed record RunBudget(double Fraction)
{
    /// <summary>The fraction at which the owner is told the run is near its limit (80 %).</summary>
    public const double NearFraction = 0.8;

    public bool Near => Fraction >= NearFraction;

    public bool Reached => Fraction >= 1.0;

    /// <summary>The budget of a run with these limits and this spend, or null when the run has no limit.</summary>
    public static RunBudget? For(RunLimits? limits, RunSpend spend)
    {
        if (limits is null || limits.IsEmpty) return null;

        var fractions = new List<double>();
        if (limits.MaxOutputTokens is { } maxTokens) fractions.Add(spend.OutputTokens / (double)maxTokens);
        if (limits.MaxDuration is { } maxSpan) fractions.Add(spend.Elapsed.TotalSeconds / maxSpan.TotalSeconds);
        return new RunBudget(fractions.Max());
    }
}

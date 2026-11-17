using System.Globalization;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The limits of one run (additions item 4). A null field means no limit on that measure. Output tokens are the tokens the run's
/// sessions wrote out, and the duration is the time since the run's first session started (round 6 item 3).
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
        return new RunSpend(output, ElapsedOf(run, records, now));
    }

    /// <summary>
    /// The time a run has run for at <paramref name="now"/>: from the first session start of the run, the earliest <see cref="SessionRecord.StartedAt"/>
    /// of its sessions that have a session id. A run with no session started has run for nothing, so the time it waited for its start
    /// time, or the time before the window opened, never counts against a limit (round 6 item 3). A run that waits for its start time has
    /// run for nothing yet (round 5 item 3). A clock that runs backwards gives no time, not a negative one.
    /// </summary>
    public static TimeSpan ElapsedOf(RunRecord run, IReadOnlyDictionary<string, SessionRecord> records, DateTimeOffset now)
    {
        if (run.StartAt is { } startAt && startAt > now) return TimeSpan.Zero;
        DateTimeOffset? first = null;
        foreach (var spec in run.Specs.DistinctBy(s => s.Name, StringComparer.Ordinal))
        {
            if (!records.TryGetValue(spec.Name, out var record) || string.IsNullOrEmpty(record.SessionId)) continue;
            if (first is null || record.StartedAt < first) first = record.StartedAt;
        }
        if (first is not { } started) return TimeSpan.Zero;
        var elapsed = now - started;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    /// <summary>
    /// The time a run has run for at <paramref name="now"/> when the caller has no session records: from the run's start time once that time has
    /// come, and from its creation when it has none. This is the rule before round 6 item 3, which counted a run from its start time even
    /// when no session had started. The spend uses <see cref="ElapsedOf(RunRecord, IReadOnlyDictionary{string, SessionRecord}, DateTimeOffset)"/>.
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
public sealed record RunBudget(double Fraction, bool Notify = true)
{
    /// <summary>The fraction at which the owner is told the run is near its limit (80 %).</summary>
    public const double NearFraction = 0.8;

    /// <summary>The level a session record holds once the owner was told the run is near its limit.</summary>
    public const string NearLevel = "near";

    /// <summary>The level a session record holds once the owner was told the run reached its limit.</summary>
    public const string ReachedLevel = "reached";

    public bool Near => Fraction >= NearFraction;

    public bool Reached => Fraction >= 1.0;

    /// <summary>The level this budget is at: <see cref="ReachedLevel"/>, <see cref="NearLevel"/>, or null below 80 %.</summary>
    public string? Level => Reached ? ReachedLevel : Near ? NearLevel : null;

    /// <summary>
    /// The budget each session of a run is held to. Every session gets it, so none starts or resumes at the limit. Only the lead
    /// session, the first one of the run with a session id, is told about the limit: the owner gets one notice per run and level
    /// (round 5 item 8). <see cref="Notify"/> is false for the others.
    /// </summary>
    public static IReadOnlyDictionary<string, RunBudget> ForSessions(
        RunRecord run,
        IReadOnlyDictionary<string, SessionRecord> records,
        RunBudget budget)
    {
        var lead = run.Specs
            .DistinctBy(spec => spec.Name, StringComparer.Ordinal)
            .FirstOrDefault(spec => records.TryGetValue(spec.Name, out var record) && record.SessionId.Length > 0)?.Name;
        var held = new Dictionary<string, RunBudget>(StringComparer.Ordinal);
        foreach (var spec in run.Specs.DistinctBy(s => s.Name, StringComparer.Ordinal))
            held[spec.Name] = budget with { Notify = string.Equals(spec.Name, lead, StringComparison.Ordinal) };
        return held;
    }

    /// <summary>
    /// The budget of every session of every run that has a limit, from the records and the transcripts read for them (round 6 item 1).
    /// The caller reads them before a pass, so the pass is held to them from its first check. A run with no limit has no entry.
    /// <paramref name="writes"/> may hold the transcripts of every run: each spend is counted from its own sessions only.
    /// </summary>
    public static IReadOnlyDictionary<string, RunBudget> ForRuns(
        IEnumerable<RunRecord> runs,
        IReadOnlyDictionary<string, SessionRecord> records,
        IEnumerable<SessionWrites> writes,
        DateTimeOffset now)
    {
        var all = writes.ToList();
        var budgets = new Dictionary<string, RunBudget>(StringComparer.Ordinal);
        foreach (var run in runs)
        {
            if (For(run.Limits, RunSpend.Of(run, records, all, now)) is not { } budget) continue;
            foreach (var pair in ForSessions(run, records, budget)) budgets[pair.Key] = pair.Value;
        }
        return budgets;
    }

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

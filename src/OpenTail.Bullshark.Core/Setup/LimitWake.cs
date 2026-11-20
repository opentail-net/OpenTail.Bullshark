namespace OpenTail.Bullshark.Core.Setup;

/// <summary>What the wake rule says to do for one session on one pass (limit wake, decision table of 2026-11-20).</summary>
public enum LimitWakeAction
{
    /// <summary>Nothing: the owner did not ask, there is no stop, or this limit was already woken.</summary>
    None,

    /// <summary>Wait: the limit has not reset yet (or the reset margin is not over, or another limit is still in force).</summary>
    Wait,

    /// <summary>Start the session again, once, with the fixed line.</summary>
    WakeNow,

    /// <summary>Do not start it; tell the owner the note.</summary>
    Notify,
}

/// <summary>What the listing says about a session: not listed, listed as stopped or failed, or listed alive (working, blocked, busy, done, stale, or any other state).</summary>
public enum WakeListing
{
    NotListed,
    Stopped,
    Alive,
}

/// <summary>Whether the session's folder can take a start, and the reason when it cannot.</summary>
public sealed record WakeFolder(bool Usable, string? Note);

/// <summary>
/// The inputs of <see cref="LimitWake.Decide"/>. <see cref="LimitInForce"/> (the reset of a limit still in force, <c>LimitReset.Find</c>) and
/// <see cref="Folder"/> (the folder check) are functions, so they are called only when the rule reaches them: a reading of the transcripts or of the
/// trust record is not made for a session that is not due.
/// </summary>
public sealed record LimitWakeInput(
    LimitStop? Stop,
    bool AutoWake,
    DateTimeOffset Now,
    WakeListing Listing,
    DateTimeOffset? AutoWokenHitAt,
    IReadOnlyList<DateTimeOffset>? AutoWakeTimes,
    Func<DateTimeOffset?> LimitInForce,
    Func<WakeFolder> Folder,
    /// <summary>
    /// The session has a record in the rail (the supervisor keeps it). The shared start refuses a session the rail holds, so the rule says so here
    /// instead of trying the start: a notice, not a wake.
    /// </summary>
    bool KeptInRail = false);

/// <summary>The result of <see cref="LimitWake.Decide"/>: the action, and for a notice the words the owner sees.</summary>
public sealed record LimitWakeDecision(LimitWakeAction Action, string? Note = null);

/// <summary>
/// The rule that decides whether a session that stopped at the usage limit is started again (limit wake, 2026-11-20). Pure: the clock, the listing,
/// the limit in force and the folder check are passed in. The rows are checked in order; the first that holds decides.
/// <para>
/// Why: a start with <c>--resume</c> on a running session makes a copy (claude --help), so a listed session is never started; a session the owner
/// stopped stays stopped; and the owner's evidence is that a wake before the reset always failed, and one after it worked. So never before
/// <see cref="LimitReset.StartFor"/>, and not while any limit is still in force.
/// </para>
/// </summary>
public static class LimitWake
{
    /// <summary>The fixed line a woken session is given. The owner's text; it is not editable.</summary>
    public const string Line = "The usage limit has reset. Please continue where you left off.";

    /// <summary>The note of a session that is still running after its reset: it is not started, and the owner is told to open it.</summary>
    public const string RunningNote = "running: open it to continue";

    /// <summary>The note of a session the rail keeps: the rail's own start is the one that resumes it, so the wake does not.</summary>
    public const string KeptInRailNote = "kept in the rail: not woken";

    /// <summary>The most wakes of one session in <see cref="Day"/>. A fourth is not started; the owner is told.</summary>
    public const int MaxWakesPerDay = 3;

    /// <summary>The span the wakes are counted in.</summary>
    public static readonly TimeSpan Day = TimeSpan.FromHours(24);

    /// <summary>
    /// The decision for one session. The rows, in order: no owner flag, no stop, or a stop too old to show (nothing); this hit was already woken
    /// (nothing); before the reset margin, or another limit still in force (wait); the daily cap (notice); listed stopped or failed (notice);
    /// listed alive (notice); not listed but kept in the rail (notice: the start would be refused); not listed with a usable folder (wake now); not
    /// listed with an unusable folder (notice with the reason).
    /// </summary>
    public static LimitWakeDecision Decide(LimitWakeInput input)
    {
        var stop = input.Stop;
        if (!input.AutoWake || stop is null || !stop.IsRecent(input.Now)) return new LimitWakeDecision(LimitWakeAction.None);

        // Woken already: a new limit hit has a different HitAt, and that one is eligible again. This comes before the wait rows, so a hit that was
        // woken is not shown as waiting for another limit.
        if (input.AutoWokenHitAt == stop.HitAt) return new LimitWakeDecision(LimitWakeAction.None);

        if (input.Now < LimitReset.StartFor(stop.ResetsAt)) return new LimitWakeDecision(LimitWakeAction.Wait);
        if (input.LimitInForce() is not null) return new LimitWakeDecision(LimitWakeAction.Wait);

        if (WakesInDay(input.AutoWakeTimes, input.Now) >= MaxWakesPerDay)
            return new LimitWakeDecision(LimitWakeAction.Notify, $"woken {MaxWakesPerDay} times in a day; not waking again");

        if (input.Listing == WakeListing.Stopped)
            return new LimitWakeDecision(LimitWakeAction.Notify, "listed as stopped or failed: not woken");
        if (input.Listing == WakeListing.Alive)
            return new LimitWakeDecision(LimitWakeAction.Notify, RunningNote);
        if (input.KeptInRail)
            return new LimitWakeDecision(LimitWakeAction.Notify, KeptInRailNote);

        var folder = input.Folder();
        return folder.Usable
            ? new LimitWakeDecision(LimitWakeAction.WakeNow)
            : new LimitWakeDecision(LimitWakeAction.Notify, "not woken: " + (folder.Note is { Length: > 0 } note ? note : "its folder cannot be used"));
    }

    /// <summary>The wake times that fall in the day before <paramref name="now"/>. A time after now (the clock was set back) counts as recent.</summary>
    public static int WakesInDay(IEnumerable<DateTimeOffset>? times, DateTimeOffset now) =>
        times?.Count(time => now - time < Day) ?? 0;

    /// <summary>The wake times still inside the day before <paramref name="now"/>, with <paramref name="now"/> added: what a wake records.</summary>
    public static IReadOnlyList<DateTimeOffset> WithWake(IEnumerable<DateTimeOffset>? times, DateTimeOffset now) =>
        (times ?? []).Where(time => now - time < Day).Append(now).ToList();
}

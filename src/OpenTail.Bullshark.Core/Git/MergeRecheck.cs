namespace OpenTail.Bullshark.Core.Git;

/// <summary>
/// What a coder's merge check last found when the result was a problem (reliability batch R8): when an attempt was last made, when the check
/// last ran, and the HEAD it ran on (null when HEAD could not be read).
/// </summary>
public sealed record ProblemMemory(DateTimeOffset TriedAt, DateTimeOffset RanAt, string? Head);

/// <summary>
/// When a coder's merge check that found a problem is made again (reliability batch R8). A problem is rechecked at most every five minutes, not
/// on every pass (<see cref="Floor"/>). While HEAD is the same as when the check last ran, the check is not run again within 30 minutes of that
/// run (<see cref="SameHeadInterval"/>). The functions are pure: the caller gives the clock and the HEAD it read, so the schedule is tested
/// without git or a timer.
/// </summary>
public static class MergeRecheck
{
    /// <summary>The shortest time between two attempts for a coder whose last result was a problem.</summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMinutes(5);

    /// <summary>While HEAD is unchanged, the time between two real runs of the check after a problem.</summary>
    public static readonly TimeSpan SameHeadInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether an attempt may start now. Nothing is run and no git is called when it is false. There is no memory, or the last attempt is at least
    /// <see cref="Floor"/> old (or the clock went back).
    /// </summary>
    public static bool MayTry(ProblemMemory? last, DateTimeOffset now) =>
        last is null || now < last.TriedAt || now - last.TriedAt >= Floor;

    /// <summary>
    /// Whether the attempt is skipped: HEAD now is the HEAD the check ran on, and that run was less than <see cref="SameHeadInterval"/> ago. A HEAD
    /// that could not be read (null) is never the same, so the check runs.
    /// </summary>
    public static bool SkipForSameHead(ProblemMemory last, string? headNow, DateTimeOffset now) =>
        headNow is not null
        && last.Head is not null
        && string.Equals(headNow, last.Head, StringComparison.Ordinal)
        && now >= last.RanAt
        && now - last.RanAt < SameHeadInterval;
}

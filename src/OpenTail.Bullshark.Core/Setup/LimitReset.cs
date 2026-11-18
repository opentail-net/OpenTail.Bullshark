namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// The reset time of the usage limit Claude Code last reported on this computer, for the "Start when the usage limit resets" choice
/// (gems item 5, D5.6). When a limit is hit, Claude Code writes an assistant entry to the session transcript: its <c>apiError</c> is
/// <c>usage_limit_reached</c>, and <c>apiErrorParams.rate_limit_info</c> holds a <c>status</c> and a <c>resetsAt</c> that is a Unix time in
/// seconds (checked on the owner's transcripts, 2026-11-18). The reset time is read from that structure, never from the text. Only a
/// <c>status</c> of "rejected" counts. The entries are read by <see cref="LimitEntries"/>, which the status bar and the limit history share.
/// </summary>
public static class LimitReset
{
    /// <summary>The margin after the reset time before a waiting run starts.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    private const string Rejected = "rejected";

    /// <summary>
    /// The reset time of the limit that resets last among the limits still in force at <paramref name="now"/> (D2), or null when none
    /// is in force (D3). It reads the transcripts under <paramref name="projectsRoot"/> that were written in the last eight days,
    /// subagent transcripts included, and only the tail of each. A folder or a file that cannot be read is skipped.
    /// </summary>
    public static DateTimeOffset? Find(string projectsRoot, DateTimeOffset now)
    {
        DateTimeOffset? latest = null;
        foreach (var file in LimitEntries.Recent(projectsRoot, now.UtcDateTime - LimitEntries.LimitWindow))
        {
            foreach (var entry in LimitEntries.Tail(file.FullName))
            {
                if (InForce(entry, now) is { } reset && (latest is null || reset > latest)) latest = reset;
            }
        }
        return latest;
    }

    /// <summary>The time a waiting run starts for a limit that resets at <paramref name="resetsAt"/>: the margin after it.</summary>
    public static DateTimeOffset StartFor(DateTimeOffset resetsAt) => resetsAt + Margin;

    /// <summary>
    /// The reset time one transcript line reports: a rejected usage limit that resets after <paramref name="now"/>. Otherwise null,
    /// including for a line that is not JSON, a value that cannot be read, and a reset that is not in the future.
    /// </summary>
    internal static DateTimeOffset? FromLine(string line, DateTimeOffset now) =>
        LimitEntries.FromLine(line) is { } entry ? InForce(entry, now) : null;

    /// <summary>The entry's reset time when the entry is a rejected limit that resets after <paramref name="now"/>, else null.</summary>
    private static DateTimeOffset? InForce(LimitEntry entry, DateTimeOffset now) =>
        entry.Status == Rejected && entry.ResetsAt > now ? entry.ResetsAt : null;
}

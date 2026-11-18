using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Reports;

/// <summary>
/// One session of the current runs that was active in the period, or started in it. Its files are those it wrote over the whole session,
/// not only in the period. A session with no transcript yet has no activity time, no files and no text.
/// </summary>
public sealed record AwayRow(
    string Name,
    string SessionId,
    SessionRole Role,
    string State,
    DateTimeOffset? LastActivity,
    DateTimeOffset StartedAt,
    int FilesWritten,
    IReadOnlyList<string> FirstFiles,
    int FailedToolCalls,
    string? LastText,
    bool NeedsYou);

/// <summary>A session of the current runs with no activity in the period. <see cref="NeedsYou"/> marks one that waits for the owner.</summary>
public sealed record QuietSession(string Name, bool NeedsYou);

/// <summary>
/// "While you were away" (backlog D1): what happened from <see cref="Since"/> to <see cref="Now"/>, session by session for the current runs,
/// what the supervisor did, and the limits hit. Read-only: nothing here is sent to a session, and no attention item is made. The rules are
/// pure, so a test reaches them without a disk or a window; the window reads the files and passes them in.
/// </summary>
public sealed record AwayDigest(
    DateTimeOffset Since,
    DateTimeOffset Now,
    IReadOnlyList<AwayRow> Rows,
    IReadOnlyList<QuietSession> QuietSessions,
    IReadOnlyList<SupervisorEvent> Events,
    IReadOnlyList<LimitHit> LimitHits)
{
    /// <summary>The newest supervisor events the window reads for the page.</summary>
    public const int EventLimit = 200;

    /// <summary>The longest last text a row shows, in characters, the ellipsis included.</summary>
    public const int TextLimit = 300;

    /// <summary>The file names a row names. The count is the whole session's.</summary>
    public const int FileNameLimit = 5;

    /// <summary>How long the window must have been away before the page opens by itself when it comes back (D2).</summary>
    public static readonly TimeSpan ReturnGap = TimeSpan.FromMinutes(30);

    /// <summary>The shortest period the page covers when the status-bar button opens it (D2).</summary>
    public static readonly TimeSpan MinimumPeriod = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True when something happened in the period: a session was active, the supervisor did something, or a limit was hit. Sessions that were
    /// only quiet do not count. With nothing in the period, the page does not open by itself.
    /// </summary>
    public bool HasNews => Rows.Count > 0 || Events.Count > 0 || LimitHits.Count > 0;

    /// <summary>
    /// The start of the period the status-bar button shows: the last time the window was away from, and at least <see cref="MinimumPeriod"/>
    /// back. A missing last-seen time, or one in the future, gives the minimum period.
    /// </summary>
    public static DateTimeOffset PeriodStart(DateTimeOffset? lastSeen, DateTimeOffset now)
    {
        var recent = now - MinimumPeriod;
        return lastSeen is { } seen && seen < recent ? seen : recent;
    }

    /// <summary>
    /// True when the window was away for at least <see cref="ReturnGap"/>, so that its return may open the page. A missing last-seen time is
    /// no return: there is nothing to say the window was away.
    /// </summary>
    public static bool IsReturn(DateTimeOffset? lastSeen, DateTimeOffset now) => lastSeen is { } seen && now - seen >= ReturnGap;

    /// <summary>
    /// The digest of the period. Only the sessions of <paramref name="records"/> are rows or quiet names; a session is a row when its main
    /// transcript had activity in the period, or when it has no transcript yet and started in it. Subagent transcripts are not sessions here.
    /// Events of kind "state" and "other" are left out (a session's state is in its row). Limit hits and events are kept when they fall in the
    /// period, oldest first. Records with no session id (a start that failed) are left out: the rail shows them under Needs you.
    /// </summary>
    public static AwayDigest Build(
        IReadOnlyList<SessionRecord> records,
        IReadOnlyList<SessionDigest> digests,
        IReadOnlyList<AgentInfo> agents,
        IReadOnlyList<SupervisorEvent> events,
        IReadOnlyList<LimitHit> limitHits,
        IReadOnlyList<AttentionItem> attention,
        DateTimeOffset since,
        DateTimeOffset now)
    {
        var latest = LatestMainDigests(digests);
        var waiting = attention.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var rows = new List<AwayRow>();
        var quiet = new List<QuietSession>();

        foreach (var record in records.Where(record => !string.IsNullOrEmpty(record.SessionId)))
        {
            var needsYou = waiting.Contains(record.Name);
            var state = StateOf(record, agents);
            latest.TryGetValue(record.SessionId, out var digest);
            if (digest is null)
            {
                if (Within(record.StartedAt, since, now))
                    rows.Add(new AwayRow(record.Name, record.SessionId, record.Role, state, null, record.StartedAt, 0, [], 0, null, needsYou));
                else
                    quiet.Add(new QuietSession(record.Name, needsYou));
            }
            else if (digest.LastActivity is { } last && Within(last, since, now))
            {
                rows.Add(RowOf(record, digest, state, needsYou));
            }
            else
            {
                quiet.Add(new QuietSession(record.Name, needsYou));
            }
        }

        return new AwayDigest(
            since,
            now,
            rows.OrderByDescending(row => row.LastActivity ?? row.StartedAt).ThenBy(row => row.Name, StringComparer.Ordinal).ToList(),
            quiet.OrderBy(session => session.Name, StringComparer.Ordinal).ToList(),
            events
                .Where(evt => evt.Event is not ("state" or "other") && Within(evt.At, since, now))
                .OrderBy(evt => evt.At)
                .ToList(),
            limitHits
                .Where(hit => Within(hit.Time, since, now))
                .OrderBy(hit => hit.Time)
                .ToList());
    }

    /// <summary>
    /// The last assistant text, as a row shows it: its white space collapsed to single spaces, and cut to <see cref="TextLimit"/> characters
    /// with an ellipsis at the end when it is longer. Null for a blank text.
    /// </summary>
    public static string? TrimmedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= TextLimit ? line : line[..(TextLimit - 1)] + "…";
    }

    /// <summary>One digest per session id: the latest of its main transcripts. Subagent transcripts are left out.</summary>
    private static Dictionary<string, SessionDigest> LatestMainDigests(IReadOnlyList<SessionDigest> digests)
    {
        var latest = new Dictionary<string, SessionDigest>(StringComparer.OrdinalIgnoreCase);
        foreach (var digest in digests.Where(digest => !digest.IsSubagent))
        {
            if (!latest.TryGetValue(digest.SessionId, out var kept) || Activity(digest) > Activity(kept))
                latest[digest.SessionId] = digest;
        }
        return latest;
    }

    private static DateTimeOffset Activity(SessionDigest digest) => digest.LastActivity ?? DateTimeOffset.MinValue;

    private static AwayRow RowOf(SessionRecord record, SessionDigest digest, string state, bool needsYou) => new(
        record.Name,
        record.SessionId,
        record.Role,
        state,
        digest.LastActivity,
        record.StartedAt,
        digest.FilesWritten.Count,
        digest.FilesWritten.Take(FileNameLimit).Select(FileName).ToList(),
        digest.FailedToolCalls,
        TrimmedText(digest.LastAssistantText),
        needsYou);

    private static string FileName(string path) => Path.GetFileName(path) is { Length: > 0 } name ? name : path;

    /// <summary>The listed state of the session, or "not listed" when the listing does not show it.</summary>
    private static string StateOf(SessionRecord record, IReadOnlyList<AgentInfo> agents) =>
        agents.FirstOrDefault(agent => string.Equals(agent.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase))?.EffectiveState
        ?? "not listed";

    private static bool Within(DateTimeOffset at, DateTimeOffset since, DateTimeOffset now) => at >= since && at <= now;
}

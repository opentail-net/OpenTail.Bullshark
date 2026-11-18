using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Git;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>Why a session is waiting for the owner.</summary>
public enum AttentionReason
{
    /// <summary>Listed as `blocked`: waiting for a permission answer, an answer to a question, or an idle session (F19, F21).</summary>
    Blocked,

    /// <summary>The session is gone and the supervisor has stopped resuming it (the restart-loop alert).</summary>
    RestartLoop,

    /// <summary>A start or a resume failed. The owner asks for a retry (or looks at the reason).</summary>
    StartFailed,

    /// <summary>The session was stopped and is not brought back (the stopped alert, or listed as `stopped`). The owner starts the run again.</summary>
    Stopped,

    /// <summary>The session failed and is not brought back (the failed alert, or listed as `failed`). The owner looks at its tab.</summary>
    Failed,

    /// <summary>
    /// Another session of the same run wrote the same file (gems item 1). One item per file for each session that wrote it, so the
    /// owner is told about each file once. It is a notice: nothing is sent to the sessions.
    /// </summary>
    Overlap,

    /// <summary>
    /// The coder's committed work conflicts with the default branch (gems item 2). Its detail lists the files. It is a notice like
    /// <see cref="Overlap"/>: the owner copies a resolve prompt, and nothing is sent to the session.
    /// </summary>
    Conflict,

    /// <summary>
    /// The run reached its limit, so nothing new starts or resumes (additions item 4, revised by round 5 item 8: the owner needs to see
    /// it). One item for each session that holds the level, normally the lead. It is shown when the session has no other reason to wait.
    /// </summary>
    LimitReached,
}

/// <summary>A coder's merge check result that found conflicts: the session's name, the conflicting files and the branch they conflict with.</summary>
public sealed record SessionConflict(string Name, IReadOnlyList<string> Files, string Target);

/// <summary>
/// One session that waits for the owner. <see cref="SessionId"/> is null for a start that never got a session.
/// <see cref="FilePath"/> is set only for <see cref="AttentionReason.Overlap"/>: the file the notice is about.
/// <see cref="Files"/> is set only for <see cref="AttentionReason.Conflict"/>: the conflicting files, which identify the notice.
/// </summary>
public sealed record AttentionItem(
    string Name,
    string? SessionId,
    AttentionReason Reason,
    string Detail,
    DateTimeOffset StartedAt,
    string? FilePath = null,
    IReadOnlyList<string>? Files = null);

/// <summary>
/// The sessions that need the owner, as a pure function of the state records and the agent listing. The window shows them
/// under "Needs you" and announces a session when it joins the list. Nothing here answers a prompt or starts anything.
/// </summary>
public static class AttentionList
{
    /// <summary>The start of <see cref="SessionRecord.LastAlert"/> for a failed resume. The supervisor writes it; this reads it.</summary>
    public const string ResumeFailedPrefix = "resume failed: ";

    /// <summary>The start of <see cref="SessionRecord.LastAlert"/> for a restart loop. Written as `{AlertKind}: {detail}`.</summary>
    public const string RestartLoopPrefix = "RestartLoop: ";

    /// <summary>The start of <see cref="SessionRecord.LastAlert"/> for a stopped session. Written as `{AlertKind}: {detail}`.</summary>
    public const string StoppedAlertPrefix = nameof(AlertKind.Stopped) + ": ";

    /// <summary>The start of <see cref="SessionRecord.LastAlert"/> for a failed session. Written as `{AlertKind}: {detail}`.</summary>
    public const string FailedAlertPrefix = nameof(AlertKind.Failed) + ": ";

    /// <summary>
    /// The detail of the alert for a session that was waiting for input when the background daemon stopped (round 8 item 1). The owner
    /// opens its tab: the session is not resumed by the supervisor, because its pending prompt is lost with the daemon.
    /// </summary>
    public const string StaleBlockedDetail = "was waiting for input when the background daemon stopped; open its tab to resume it";

    /// <summary>
    /// The start of <see cref="SessionRecord.LastAlert"/> for that alert: `{AlertKind}: ` and the detail. It is cleared when the session is listed
    /// live again, so a later stale spell alerts again.
    /// </summary>
    public const string StaleBlockedAlertPrefix = nameof(AlertKind.Blocked) + ": " + StaleBlockedDetail;

    /// <summary>True when the last alert is the one for a session that was waiting for input when the daemon stopped.</summary>
    public static bool IsStaleBlockedAlert(string? alert) => alert?.StartsWith(StaleBlockedAlertPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// The sessions that need the owner, oldest session first (by when it started, then by name). The rules:
    /// <list type="bullet">
    /// <item>A record with no session id is a start that failed: <see cref="AttentionReason.StartFailed"/>.</item>
    /// <item>A session that is listed as `blocked`: <see cref="AttentionReason.Blocked"/>. Listed as `stopped` or `failed`:
    /// <see cref="AttentionReason.Stopped"/> or <see cref="AttentionReason.Failed"/>, since such a session is not brought back.</item>
    /// <item>A session that is not listed and whose last alert is a restart loop, a failed resume, a stopped or a failed session:
    /// that reason. A listed session that is working, done or unknown is not waiting for anyone because of an old alert, so the
    /// alert is ignored while it is listed. A session that was waiting for input when the daemon stopped (round 8 item 1) is
    /// <see cref="AttentionReason.Blocked"/> while its alert stands, whether it is listed as stale or not listed at all.</item>
    /// <item>Each clash in <paramref name="clashes"/> gives <see cref="AttentionReason.Overlap"/> to every session that wrote the file.</item>
    /// <item>Each conflict in <paramref name="conflicts"/> gives <see cref="AttentionReason.Conflict"/> to its session.</item>
    /// <item>A session whose run reached its limit (its <see cref="SessionRecord.LimitLevel"/> is reached) gives
    /// <see cref="AttentionReason.LimitReached"/>, when no other reason above applies to it, and while it is listed and not `done`
    /// (round 5 item 8, round 6 item 5).</item>
    /// </list>
    /// The time a session entered its state is not recorded, so "oldest" means the session's own start time.
    /// <paramref name="clashes"/> (gems item 1) adds one <see cref="AttentionReason.Overlap"/> item per session per shared file.
    /// </summary>
    public static IReadOnlyList<AttentionItem> From(
        IEnumerable<SessionRecord> records,
        IReadOnlyList<AgentInfo> agents,
        IEnumerable<FileClash>? clashes = null,
        IEnumerable<SessionConflict>? conflicts = null)
    {
        var recordList = records.ToList();
        var items = new List<AttentionItem>();
        foreach (var record in recordList)
        {
            var item = ReasonFor(record, agents) ?? LimitItem(record, agents);
            if (item is not null) items.Add(item);
        }
        if (clashes is not null) items.AddRange(OverlapItems(recordList, clashes));
        if (conflicts is not null) items.AddRange(ConflictItems(recordList, conflicts));
        return items
            .OrderBy(item => item.StartedAt)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The items in <paramref name="current"/> that are new since <paramref name="previous"/>. A session that is waiting for any reason
    /// is not new when it changes its reason, so the rule compares names. The notices (<see cref="AttentionReason.Overlap"/> and
    /// <see cref="AttentionReason.Conflict"/>) are the exception: a notice is new unless the same session already had that notice
    /// (for an overlap, the same file; for a conflict, the same files), so a second shared file or a conflict of a session that waits is
    /// announced. A session that stopped waiting and waits again is new. Only a wait makes a session "already waiting" (round 5 item 6).
    /// </summary>
    public static IReadOnlyList<AttentionItem> Gained(IReadOnlyList<AttentionItem> previous, IReadOnlyList<AttentionItem> current) =>
        Gained(previous, current, announced: null);

    /// <summary>
    /// The same rule, and a notice is also not new when <paramref name="announced"/> has its key: an overlap or a conflict that was announced
    /// once does not sound again when its session comes back after a listing gap, or after it left `done` with the same files (round 5 item 7,
    /// round 6 item 6). The keys are those of <see cref="NoticeKeys"/>, which the caller keeps for the life of the window.
    /// </summary>
    public static IReadOnlyList<AttentionItem> Gained(
        IReadOnlyList<AttentionItem> previous,
        IReadOnlyList<AttentionItem> current,
        IReadOnlySet<string>? announced)
    {
        // Only a wait makes a session "already waiting". A notice the session had (an overlap, a conflict) does not, so the first
        // blocked, stopped, failed or start-failed reason of that session is announced (round 5 item 6).
        var waiting = previous.Where(item => !IsNotice(item)).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var notices = previous.Where(IsNotice).Select(NoticeKey).ToHashSet(StringComparer.Ordinal);
        return current.Where(item => IsNotice(item)
                ? !notices.Contains(NoticeKey(item)) && announced?.Contains(NoticeKey(item)) != true
                : !waiting.Contains(item.Name))
            .ToList();
    }

    /// <summary>
    /// The session the owner goes to next: the session after <paramref name="currentSessionId"/> in <paramref name="items"/>, wrapping at the end,
    /// or the first when the current session is null or not in the list. The items are taken in the order given (<see cref="From"/> gives oldest
    /// first), and a session with several items counts once. An item with no session id (a start that failed) is skipped. Any item counts as
    /// waiting, a notice or a limit too, so the caller passes what the Needs-you group lists. A lone waiting session that is the current one gives
    /// null: there is nothing else to go to. Nothing here sends anything to a session.
    /// </summary>
    public static string? NextWaiting(IReadOnlyList<AttentionItem> items, string? currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(items);
        var sessions = items
            .Where(item => !string.IsNullOrEmpty(item.SessionId))
            .Select(item => item.SessionId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sessions.Count == 0) return null;

        var at = currentSessionId is null
            ? -1
            : sessions.FindIndex(id => string.Equals(id, currentSessionId, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return sessions[0];
        if (sessions.Count == 1) return null;
        return sessions[(at + 1) % sessions.Count];
    }

    /// <summary>The keys of the conflicts in <paramref name="items"/>, for the caller to remember once they have been announced.</summary>
    public static IReadOnlySet<string> ConflictKeys(IEnumerable<AttentionItem> items) =>
        items.Where(item => item.Reason == AttentionReason.Conflict).Select(NoticeKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The keys of the notices (overlaps and conflicts) in <paramref name="items"/>, for the caller to remember once they have been announced.
    /// The key of a notice is its session, reason and file or files, so a notice that comes back unchanged is the same notice.
    /// </summary>
    public static IReadOnlySet<string> NoticeKeys(IEnumerable<AttentionItem> items) =>
        items.Where(IsNotice).Select(NoticeKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What makes a notice the same notice: the session, the reason, and the file (an overlap) or the set of files (a conflict). The
    /// detail is left out, since an overlap's detail shows the other sessions' last writes, which change.
    /// </summary>
    private static string NoticeKey(AttentionItem item) =>
        string.Join('\u001f', item.Name, item.Reason, item.FilePath ?? string.Empty,
            string.Join('\u001e', (item.Files ?? []).OrderBy(file => file, StringComparer.Ordinal)));

    /// <summary>
    /// How many rows the Needs-you group lists (round 8 item 5): one per waiting session that has a state record, and one for the daemon note when
    /// there is one. The group header shows this number, so it never says 0 with a row under it, and never counts a session that has no row.
    /// </summary>
    public static int NeedsYouRowCount(IEnumerable<AttentionItem> items, IReadOnlyDictionary<string, SessionRecord> records, bool daemonNote) =>
        items.DistinctBy(item => item.Name, StringComparer.Ordinal).Count(item => records.ContainsKey(item.Name)) + (daemonNote ? 1 : 0);

    /// <summary>A notice tells the owner about something that happened, and the session keeps running. It is not a wait.</summary>
    private static bool IsNotice(AttentionItem item) =>
        item.Reason is AttentionReason.Overlap or AttentionReason.Conflict;

    /// <summary>
    /// Whether the owner's <c>notify</c> setting in ui.json turns notices on. A missing value, or one that is not a boolean, means on.
    /// </summary>
    public static bool NotificationsOn(JsonNode? value) =>
        value is JsonValue json && json.TryGetValue<bool>(out var on) ? on : true;

    private static AttentionItem? ReasonFor(SessionRecord record, IReadOnlyList<AgentInfo> agents)
    {
        if (string.IsNullOrEmpty(record.SessionId))
            return Item(record, AttentionReason.StartFailed, record.LastAlert ?? "start failed");

        var listed = agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
        if (listed is not null)
        {
            var state = listed.EffectiveState;
            if (string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase))
                return Item(record, AttentionReason.Blocked, listed.Waiting is { } waiting ? $"waiting for {waiting}" : "waiting for input");
            if (string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase))
                return Item(record, AttentionReason.Stopped, record.LastAlert ?? "stopped");
            if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
                return Item(record, AttentionReason.Failed, record.LastAlert ?? "failed");
            // A stale session that was waiting for input keeps its alert until the owner acts (round 8 item 1). A stale session without it waits for nobody.
            if (string.Equals(state, DaemonStatus.StaleState, StringComparison.OrdinalIgnoreCase) && IsStaleBlockedAlert(record.LastAlert))
                return Item(record, AttentionReason.Blocked, StaleBlockedDetail);
            return null;
        }

        // Not listed: the supervisor has lost it. Only the last alert says why it was not brought back.
        var alert = record.LastAlert;
        if (alert is null) return null;
        if (IsStaleBlockedAlert(alert))
            return Item(record, AttentionReason.Blocked, StaleBlockedDetail);
        if (alert.StartsWith(RestartLoopPrefix, StringComparison.Ordinal))
            return Item(record, AttentionReason.RestartLoop, alert);
        if (alert.StartsWith(ResumeFailedPrefix, StringComparison.Ordinal))
            return Item(record, AttentionReason.StartFailed, alert);
        if (alert.StartsWith(StoppedAlertPrefix, StringComparison.Ordinal))
            return Item(record, AttentionReason.Stopped, alert);
        if (alert.StartsWith(FailedAlertPrefix, StringComparison.Ordinal))
            return Item(record, AttentionReason.Failed, alert);
        return null;
    }

    /// <summary>
    /// The limit notice of a run's session, once the run has reached its limit (round 5 item 8). It is shown only while the session is live:
    /// listed, and not `done` (a finished session has no reason to wait), and a session that is not listed (lost) is not live either. Once
    /// the run's limit is removed or raised, the level goes and so does the notice (round 6 item 5).
    /// </summary>
    private static AttentionItem? LimitItem(SessionRecord record, IReadOnlyList<AgentInfo> agents)
    {
        if (!string.Equals(record.LimitLevel, RunBudget.ReachedLevel, StringComparison.Ordinal)) return null;
        var listed = agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
        if (listed is null || string.Equals(listed.EffectiveState, "done", StringComparison.OrdinalIgnoreCase)) return null;
        return Item(record, AttentionReason.LimitReached,
            "the run reached its limit: nothing new is started or resumed. Stop a session, or raise the limit.");
    }

    private static AttentionItem Item(SessionRecord record, AttentionReason reason, string detail) =>
        new(record.Name, string.IsNullOrEmpty(record.SessionId) ? null : record.SessionId, reason, detail, record.StartedAt);

    /// <summary>One conflict item per conflicting session. A session with no record is left out, since its start time is unknown.</summary>
    private static IEnumerable<AttentionItem> ConflictItems(IReadOnlyList<SessionRecord> records, IEnumerable<SessionConflict> conflicts)
    {
        var byName = records.GroupBy(r => r.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var conflict in conflicts)
        {
            if (!byName.TryGetValue(conflict.Name, out var record) || string.IsNullOrEmpty(record.SessionId)) continue;
            yield return new AttentionItem(conflict.Name, record.SessionId, AttentionReason.Conflict,
                ConflictDetail(conflict), record.StartedAt, FilePath: null, Files: conflict.Files);
        }
    }

    /// <summary>The first five files are named; the rest are counted. The branch is shown without its refs prefix.</summary>
    private static string ConflictDetail(SessionConflict conflict)
    {
        var shown = string.Join(", ", conflict.Files.Take(5));
        var more = conflict.Files.Count > 5 ? $" and {conflict.Files.Count - 5} more" : string.Empty;
        var count = conflict.Files.Count == 1 ? "1 file" : $"{conflict.Files.Count} files";
        return $"{count} conflict with {MergeCheck.DisplayName(conflict.Target)}: {shown}{more}";
    }

    /// <summary>One overlap item per session per shared file. A session with no record is left out, since its start time is unknown.</summary>
    private static IEnumerable<AttentionItem> OverlapItems(IReadOnlyList<SessionRecord> records, IEnumerable<FileClash> clashes)
    {
        var byName = records.GroupBy(r => r.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var clash in clashes)
        {
            foreach (var session in clash.Sessions)
            {
                if (!byName.TryGetValue(session.Name, out var record)) continue;
                yield return new AttentionItem(session.Name, session.SessionId, AttentionReason.Overlap,
                    FileOverlap.Detail(clash, session.Name), record.StartedAt, clash.Path);
            }
        }
    }
}

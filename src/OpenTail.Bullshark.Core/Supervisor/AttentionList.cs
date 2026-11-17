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
}

/// <summary>A coder's merge check result that found conflicts: the session's name, the conflicting files and the branch they conflict with.</summary>
public sealed record SessionConflict(string Name, IReadOnlyList<string> Files, string Target);

/// <summary>
/// One session that waits for the owner. <see cref="SessionId"/> is null for a start that never got a session.
/// <see cref="FilePath"/> is set only for <see cref="AttentionReason.Overlap"/>: the file the notice is about.
/// </summary>
public sealed record AttentionItem(
    string Name,
    string? SessionId,
    AttentionReason Reason,
    string Detail,
    DateTimeOffset StartedAt,
    string? FilePath = null);

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
    /// The sessions that need the owner, oldest session first (by when it started, then by name). The rules:
    /// <list type="bullet">
    /// <item>A record with no session id is a start that failed: <see cref="AttentionReason.StartFailed"/>.</item>
    /// <item>A session that is listed as `blocked`: <see cref="AttentionReason.Blocked"/>. Listed as `stopped` or `failed`:
    /// <see cref="AttentionReason.Stopped"/> or <see cref="AttentionReason.Failed"/>, since such a session is not brought back.</item>
    /// <item>A session that is not listed and whose last alert is a restart loop, a failed resume, a stopped or a failed session:
    /// that reason. A listed session that is working, done or unknown is not waiting for anyone because of an old alert, so the
    /// alert is ignored while it is listed.</item>
    /// <item>Each clash in <paramref name="clashes"/> gives <see cref="AttentionReason.Overlap"/> to every session that wrote the file.</item>
    /// <item>Each conflict in <paramref name="conflicts"/> gives <see cref="AttentionReason.Conflict"/> to its session.</item>
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
            if (ReasonFor(record, agents) is { } item) items.Add(item);
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
    /// (for an overlap, the same file), so a second shared file or a conflict of a session that waits is announced.
    /// A session that stopped waiting and waits again is new.
    /// </summary>
    public static IReadOnlyList<AttentionItem> Gained(IReadOnlyList<AttentionItem> previous, IReadOnlyList<AttentionItem> current)
    {
        // Only a wait makes a session "already waiting". A notice the session had (an overlap, a conflict) does not, so the first
        // blocked, stopped, failed or start-failed reason of that session is announced (round 5 item 6).
        var waiting = previous.Where(item => !IsNotice(item)).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var notices = previous
            .Where(IsNotice)
            .Select(item => (item.Name, item.Reason, item.FilePath))
            .ToHashSet();
        return current.Where(item => IsNotice(item)
                ? !notices.Contains((item.Name, item.Reason, item.FilePath))
                : !waiting.Contains(item.Name))
            .ToList();
    }

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
                return Item(record, AttentionReason.Blocked, "waiting for input");
            if (string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase))
                return Item(record, AttentionReason.Stopped, record.LastAlert ?? "stopped");
            if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
                return Item(record, AttentionReason.Failed, record.LastAlert ?? "failed");
            return null;
        }

        // Not listed: the supervisor has lost it. Only the last alert says why it was not brought back.
        var alert = record.LastAlert;
        if (alert is null) return null;
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
                ConflictDetail(conflict), record.StartedAt);
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

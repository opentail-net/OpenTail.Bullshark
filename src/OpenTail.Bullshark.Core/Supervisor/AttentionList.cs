using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Claude;

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
}

/// <summary>One session that waits for the owner. <see cref="SessionId"/> is null for a start that never got a session.</summary>
public sealed record AttentionItem(
    string Name,
    string? SessionId,
    AttentionReason Reason,
    string Detail,
    DateTimeOffset StartedAt);

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

    /// <summary>
    /// The sessions that need the owner, oldest session first (by when it started, then by name). The rules:
    /// <list type="bullet">
    /// <item>A record with no session id is a start that failed: <see cref="AttentionReason.StartFailed"/>.</item>
    /// <item>A session that is listed as `blocked`: <see cref="AttentionReason.Blocked"/>.</item>
    /// <item>A session that is not listed and whose last alert is a restart loop or a failed resume: that reason. A listed session
    /// is not waiting for anyone because of an old alert, so the alert is ignored while it is listed.</item>
    /// </list>
    /// The time a session entered its state is not recorded, so "oldest" means the session's own start time.
    /// </summary>
    public static IReadOnlyList<AttentionItem> From(IEnumerable<SessionRecord> records, IReadOnlyList<AgentInfo> agents)
    {
        var items = new List<AttentionItem>();
        foreach (var record in records)
        {
            if (ReasonFor(record, agents) is { } item) items.Add(item);
        }
        return items
            .OrderBy(item => item.StartedAt)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The items in <paramref name="current"/> whose session was not waiting in <paramref name="previous"/>. Sessions are compared
    /// by name, so a session that changes its reason while it waits is not new. A session that stopped waiting and waits again is new.
    /// </summary>
    public static IReadOnlyList<AttentionItem> Gained(IReadOnlyList<AttentionItem> previous, IReadOnlyList<AttentionItem> current)
    {
        var waiting = previous.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        return current.Where(item => !waiting.Contains(item.Name)).ToList();
    }

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
            return string.Equals(listed.EffectiveState, "blocked", StringComparison.OrdinalIgnoreCase)
                ? Item(record, AttentionReason.Blocked, "waiting for input")
                : null;
        }

        // Not listed: the supervisor has lost it. Only the last alert says why it was not brought back.
        var alert = record.LastAlert;
        if (alert is null) return null;
        if (alert.StartsWith(RestartLoopPrefix, StringComparison.Ordinal))
            return Item(record, AttentionReason.RestartLoop, alert);
        if (alert.StartsWith(ResumeFailedPrefix, StringComparison.Ordinal))
            return Item(record, AttentionReason.StartFailed, alert);
        return null;
    }

    private static AttentionItem Item(SessionRecord record, AttentionReason reason, string detail) =>
        new(record.Name, string.IsNullOrEmpty(record.SessionId) ? null : record.SessionId, reason, detail, record.StartedAt);
}

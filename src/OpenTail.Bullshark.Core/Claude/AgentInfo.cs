using System.Text.Json;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>
/// One entry from `claude agents --json`. Background sessions carry <c>id</c> and <c>state</c>;
/// interactive sessions carry <c>pid</c> and <c>status</c>. Field names were checked on 2026-11-14 (finding F2).
/// </summary>
/// <remarks>
/// <see cref="WaitingFor"/> (round 7 item 3) is what a waiting session waits for: "permission prompt", "input needed", and so on, as the
/// docs list them. The field is optional and not on 2.1.326 yet, so a listing without it gives null. Its raw text is kept, so a newer shape
/// (an object) reads as the JSON the owner would see.
/// </remarks>
public sealed record AgentInfo(
    string? Id,
    string? SessionId,
    string Name,
    string Kind,
    string? State,
    string? Status,
    string? Cwd,
    int? Pid,
    DateTimeOffset? StartedAt,
    string? WaitingFor = null,
    /// <summary>
    /// The state the listing printed, when <see cref="DaemonStatus.IdleListing"/> has made the row stale (round 8 item 1): "working" or "blocked".
    /// Null for every other row. The policy needs it, since a stale working session is lost and a stale blocked one is held.
    /// </summary>
    string? ListedState = null)
{
    /// <summary>The state to act on: background <c>state</c>, or interactive <c>status</c>.</summary>
    public string EffectiveState => State ?? Status ?? "unknown";

    /// <summary>
    /// The state the supervisor records: the listed state of a stale row, otherwise <see cref="EffectiveState"/> (round 8 item 2). "stale" is for
    /// display and decisions only, so a session that goes stale and comes back blocked is not a new entry into blocked.
    /// </summary>
    public string ObservedState => ListedState ?? EffectiveState;

    /// <summary>
    /// Whether closing this session runs `claude stop` on it (round 9 item 6). A session the listing shows as done is idle and is not stopped
    /// (review question 6). A stale row is not alive while the daemon is not running, so there is nothing to stop, and a stop of it would fail
    /// and roll the close back. Any other listed state is stopped.
    /// </summary>
    public bool NeedsStopOnClose => EffectiveState != "done" && EffectiveState != DaemonStatus.StaleState;

    /// <summary>What the session waits for, trimmed, or null when the listing says nothing about it (or says only blanks).</summary>
    public string? Waiting => string.IsNullOrWhiteSpace(WaitingFor) ? null : WaitingFor.Trim();

    public bool IsBackground => Kind == "background";

    /// <summary>Parses the JSON array printed by `claude agents --json`.</summary>
    public static IReadOnlyList<AgentInfo> ParseList(string json)
    {
        // A daemon start can print a line before the JSON. Begin at the first array bracket.
        var start = json.IndexOf('[');
        // Nothing printed is no sessions. Something printed without a list is a failed listing, not an empty one.
        if (start < 0)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AgentInfo>();
            throw new FormatException("the agent listing has no list");
        }

        using var document = JsonDocument.Parse(json[start..]);
        var list = new List<AgentInfo>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            list.Add(new AgentInfo(
                Id: ReadString(element, "id"),
                SessionId: ReadString(element, "sessionId"),
                Name: ReadString(element, "name") ?? string.Empty,
                Kind: ReadString(element, "kind") ?? "unknown",
                State: ReadString(element, "state"),
                Status: ReadText(element, "status"),
                Cwd: ReadString(element, "cwd"),
                Pid: element.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number ? pid.GetInt32() : null,
                StartedAt: element.TryGetProperty("startedAt", out var started) && started.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeMilliseconds(started.GetInt64())
                    : null,
                WaitingFor: ReadText(element, "waitingFor")));
        }
        return list;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A field that is text, or a newer shape read as text (round 7 item 3, round 8 item 9). A string is its value. An object shows its "kind",
    /// "text" or "message" string when it has one, else its first string value; an array shows its first string. A number, a boolean, null, or a
    /// shape with no string in it gives null. The raw JSON is never the value: it would be shown as the reason or as the state.
    /// </summary>
    private static string? ReadText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? TextOf(value) : null;

    private static string? TextOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Object => ObjectText(value),
        JsonValueKind.Array => ArrayText(value),
        _ => null,
    };

    /// <summary>The text an object stands for: its "kind", "text" or "message" string, else the first string it holds.</summary>
    private static string? ObjectText(JsonElement value)
    {
        foreach (var name in new[] { "kind", "text", "message" })
            if (value.TryGetProperty(name, out var named) && named.ValueKind == JsonValueKind.String) return named.GetString();
        foreach (var property in value.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
        return null;
    }

    /// <summary>The text an array stands for: its first item that is text.</summary>
    private static string? ArrayText(JsonElement value)
    {
        foreach (var item in value.EnumerateArray())
            if (TextOf(item) is { } text) return text;
        return null;
    }
}

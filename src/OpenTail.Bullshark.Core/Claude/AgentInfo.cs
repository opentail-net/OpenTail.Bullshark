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
    /// A field that is text, or a newer shape kept as text (round 7 item 3): a string is its value, an object or an array is its raw JSON.
    /// A number, a boolean or null is not text, and gives null.
    /// </summary>
    private static string? ReadText(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) ? null : value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            _ => null,
        };
}

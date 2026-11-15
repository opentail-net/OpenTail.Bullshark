using System.Text.Json;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>
/// One entry from `claude agents --json`. Background sessions carry <c>id</c> and <c>state</c>;
/// interactive sessions carry <c>pid</c> and <c>status</c>. Field names were checked on 2026-11-14 (finding F2).
/// </summary>
public sealed record AgentInfo(
    string? Id,
    string? SessionId,
    string Name,
    string Kind,
    string? State,
    string? Status,
    string? Cwd,
    int? Pid,
    DateTimeOffset? StartedAt)
{
    /// <summary>The state to act on: background <c>state</c>, or interactive <c>status</c>.</summary>
    public string EffectiveState => State ?? Status ?? "unknown";

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
                Status: ReadString(element, "status"),
                Cwd: ReadString(element, "cwd"),
                Pid: element.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number ? pid.GetInt32() : null,
                StartedAt: element.TryGetProperty("startedAt", out var started) && started.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeMilliseconds(started.GetInt64())
                    : null));
        }
        return list;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

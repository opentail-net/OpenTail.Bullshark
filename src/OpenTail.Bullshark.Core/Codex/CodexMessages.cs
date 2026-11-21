using System.Text.Json;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The one reader of a rollout's messages (steps S1 and R1). The summary input (<see cref="CodexSummaryInput"/>) and the transcript lines
/// (<see cref="CodexTurns"/>) both read through here, so the summary and the start text see the same messages. Injected context at the start of
/// a session (decision R-D5) is left out of the owner's messages in both.
/// </summary>
internal static class CodexMessages
{
    /// <summary>
    /// The prefixes of injected context at the start of a session (R-D5). These are an assumption, checked only with synthetic text: no real rollout
    /// has been read to confirm them.
    /// </summary>
    private static readonly string[] InjectedPrefixes = ["<environment_context>", "<user_instructions>", "<INSTRUCTIONS>", "# AGENTS.md"];

    /// <summary>True when the trimmed text starts with one of the injected-context prefixes (R-D5).</summary>
    public static bool IsInjectedContext(string text)
    {
        var trimmed = text.TrimStart();
        return InjectedPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The message of a rollout line: its label (<c>you</c> for a user message, <c>codex</c> for an assistant one), its joined text, and the record's
    /// timestamp (null when it has none). Null for a line that is not JSON, a record that is not a <c>response_item</c> message of the owner or of
    /// Codex, a message with no text, or a user message that is injected context. Only user messages are checked for injected context.
    /// </summary>
    public static (string Label, string Text, DateTimeOffset? Time)? ReadMessage(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        using var document = RolloutReader.TryParseObject(line);
        if (document is null) return null;

        var record = document.RootElement;
        if (RolloutReader.StringOf(record, "type") != "response_item") return null;
        if (!record.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;

        var label = RolloutReader.StringOf(payload, "role") switch
        {
            "user" => "you",
            "assistant" => "codex",
            _ => null,
        };
        if (label is null) return null;
        if (TextOf(payload) is not { } text) return null;
        if (label == "you" && IsInjectedContext(text)) return null;
        return (label, text, RolloutReader.TimestampOf(record));
    }

    /// <summary>
    /// The text of a message: its string content, or the text parts of its content list joined with a newline. Each part is trimmed and a blank
    /// one is skipped. Null when there is no text.
    /// </summary>
    private static string? TextOf(JsonElement payload)
    {
        if (!payload.TryGetProperty("content", out var content)) return null;
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return RolloutReader.StringOf(payload, "content");
            case JsonValueKind.Array:
                var parts = new List<string>();
                foreach (var part in content.EnumerateArray())
                {
                    if (RolloutReader.StringOf(part, "text") is { } piece) parts.Add(piece);
                }
                return parts.Count == 0 ? null : string.Join("\n", parts);
            default:
                return null;
        }
    }
}

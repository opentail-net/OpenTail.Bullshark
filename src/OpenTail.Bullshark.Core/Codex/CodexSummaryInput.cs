using System.Text.Json;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The conversation a Codex summary reads (S1, decisions S-D3 and S-D7). It is built from a rollout's lines: only the owner's messages (<c>you: </c>)
/// and Codex's replies (<c>codex: </c>) are kept, one per message, and tool calls and results are left out, as Claude's
/// <c>ResumeText.SummaryInput</c> does. A line that is not JSON, or not a message, is skipped. When the text is over the cap, the oldest
/// messages go first, the newest message is always kept (cut in its middle if it alone is over the cap), and the summary is told so with
/// <see cref="CutNotice"/>. The result is never longer than the cap.
/// </summary>
public static class CodexSummaryInput
{
    /// <summary>The line put at the top when the start of the session is left out.</summary>
    public const string CutNotice = "(The start of this session is left out: it is longer than the summary reads.)";

    /// <summary>The blank line between messages, and between the notice and the messages.</summary>
    private const string Separator = "\n\n";

    /// <summary>
    /// The messages of the rollout as one text, within <paramref name="maxChars"/> characters. The lines are the rollout's lines, verbatim; an
    /// overlong line should be left out by the caller (it has no text here). Throws <see cref="ArgumentOutOfRangeException"/> when the cap is too
    /// small to hold the notice.
    /// </summary>
    public static string Build(IEnumerable<string> rolloutLines, int maxChars = CodexSummarizer.MaxInputChars)
    {
        if (maxChars <= CutNotice.Length + Separator.Length)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "The cap is too small to hold the notice.");

        var messages = new List<string>();
        foreach (var line in rolloutLines)
        {
            if (Rendered(line) is { } message) messages.Add(message);
        }

        var whole = string.Join(Separator, messages);
        if (whole.Length <= maxChars) return whole;

        // Cut: the notice and its blank line are paid for first, then the newest messages are kept while they fit.
        var budget = maxChars - CutNotice.Length - Separator.Length;
        var kept = new List<string>();
        var size = 0;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var cost = messages[i].Length + (kept.Count > 0 ? Separator.Length : 0);
            if (size + cost > budget) break;
            kept.Add(messages[i]);
            size += cost;
        }
        kept.Reverse();

        // The newest message alone is over the budget: it is kept, cut in its middle (its start and its end stay), so the latest text is never lost.
        var body = kept.Count > 0 ? string.Join(Separator, kept) : ResumeText.CutMiddle(messages[^1], budget);
        return CutNotice + Separator + body;
    }

    /// <summary>
    /// The line as <c>you: </c> or <c>codex: </c> followed by its text, or null when the line is not a JSON object, is not a message of the owner or
    /// of Codex (a <c>response_item</c> whose role is <c>user</c> or <c>assistant</c>), or has no text.
    /// </summary>
    internal static string? Rendered(string line)
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
        return TextOf(payload) is { } text ? label + ": " + text : null;
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

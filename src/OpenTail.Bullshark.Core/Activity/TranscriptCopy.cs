using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The text the transcript view copies or shows in full (copy and full-result batch, T3). A shown line keeps only a preview of a tool result, so the
/// full text is read again from the raw record. Nothing here sends anything anywhere: the owner's clipboard is the only destination.
/// </summary>
public static class TranscriptCopy
{
    /// <summary>The most characters a full result shows or copies. A longer result is cut, and the view says so.</summary>
    public const int FullResultCap = 200_000;

    /// <summary>A text to show or copy: the text kept (cut to <see cref="FullResultCap"/>), its whole length, and whether it was cut.</summary>
    public sealed record FullText(string Text, int Length, bool Truncated);

    /// <summary>The message of a shown line as written. A result's line is only its preview: its whole text is <see cref="FullResult"/>.</summary>
    public static string Text(TranscriptLine line) => line.Text;

    /// <summary>The command of the first Bash call in a raw record, or null when the record has none, or it is empty.</summary>
    public static string? Command(string raw)
    {
        if (!TranscriptJson.TryParse(raw, out var document)) return null;
        using (document)
        {
            try
            {
                if (!ContentBlocks(document.RootElement, out var content)) return null;
                foreach (var block in content.EnumerateArray())
                {
                    if (TranscriptJson.Str(block, "type") != "tool_use" || TranscriptJson.Str(block, "name") != "Bash") continue;
                    if (!block.TryGetProperty("input", out var input)) continue;
                    var command = TranscriptJson.Str(input, "command");
                    if (!string.IsNullOrEmpty(command)) return command;
                }
                return null;
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The whole text of the tool results in a raw record, capped; when the record has no tool result, or the line is not JSON, the raw line itself
    /// (so a line that does not parse shows as it is written).
    /// </summary>
    public static FullText FullResult(string raw) => Cap(ResultTextOf(raw) ?? raw);

    /// <summary>The text cut to <see cref="FullResultCap"/> characters, not splitting a surrogate pair, with the whole length kept.</summary>
    public static FullText Cap(string text)
    {
        if (text.Length <= FullResultCap) return new FullText(text, text.Length, false);
        var cut = FullResultCap;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return new FullText(text[..cut], text.Length, true);
    }

    private static string? ResultTextOf(string raw)
    {
        if (!TranscriptJson.TryParse(raw, out var document)) return null;
        using (document)
        {
            try
            {
                if (!ContentBlocks(document.RootElement, out var content)) return null;
                var parts = new List<string>();
                foreach (var block in content.EnumerateArray())
                {
                    if (TranscriptJson.Str(block, "type") == "tool_result") parts.Add(TranscriptLine.FullResultText(block));
                }
                return parts.Count == 0 ? null : string.Join('\n', parts);
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return null;
            }
        }
    }

    /// <summary>The list of content blocks of a record's message, when it has one.</summary>
    private static bool ContentBlocks(JsonElement root, out JsonElement content)
    {
        content = default;
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
               message.TryGetProperty("content", out content) && content.ValueKind == JsonValueKind.Array;
    }
}

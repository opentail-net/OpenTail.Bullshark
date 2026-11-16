using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// What a session was about, for its heading: the first message the owner typed, in one line and shortened. System notices
/// (lines that start with "&lt;") and slash commands are skipped, and so are tool results, which carry no text of the owner's.
/// </summary>
public static class SessionTitle
{
    /// <summary>The longest title, in characters. A longer one is cut and ends with an ellipsis.</summary>
    public const int MaxLength = 80;

    /// <summary>The line a resumed session's start text begins with, naming the session it continues (D-L8).</summary>
    private static readonly Regex ContinuesLine = new(@"^Continues session: ([0-9a-fA-F-]{36})\b", RegexOptions.Compiled);

    /// <summary>The text of a user record as written, not cleaned: its string content, or its first text block. Null when none.</summary>
    public static string? TextOf(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content)) return null;

        string? text = null;
        if (content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var value))
                {
                    text = value.GetString();
                    break;
                }
            }
        }
        return text;
    }

    /// <summary>The text of a user record when it is a real message; otherwise null.</summary>
    public static string? FromUserRecord(JsonElement root) => Clean(TextOf(root));

    /// <summary>The session a resumed session continues, read from the first line of a user record (D-L8); null when there is none.</summary>
    public static string? ContinuesIdOf(JsonElement root)
    {
        var text = TextOf(root);
        if (text is null) return null;
        var match = ContinuesLine.Match(text.TrimStart());
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The first line of a message, in single spaces, shortened to <see cref="MaxLength"/>. Null when it is empty or a notice.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        // The app's own briefs (a planner, a coder or a solo session) are not what the owner wrote. A brief that carries a
        // task shows the task; one without a task, and the restart prompt, are skipped.
        if (trimmed.StartsWith("You were restarted", StringComparison.Ordinal)) return null;

        // A resumed session starts from the text of the session it continues, which is not what the owner typed (D-L8).
        if (trimmed.StartsWith("Continues session:", StringComparison.Ordinal)) return null;
        if (trimmed.StartsWith("This session continues an earlier Claude Code session", StringComparison.Ordinal)) return null;
        if (trimmed.StartsWith("You are ", StringComparison.Ordinal))
        {
            var at = trimmed.IndexOf("Task: ", StringComparison.Ordinal);
            if (at < 0) return null;
            trimmed = trimmed[(at + "Task: ".Length)..];
        }

        // A message another session sent to this one arrives with a wrapper line; the title is the message itself.
        if (trimmed.StartsWith("Another Claude session sent a message:", StringComparison.Ordinal))
        {
            var body = trimmed.Split('\n').Skip(1).FirstOrDefault(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('<'));
            if (body is null) return null;
            trimmed = body;
        }

        if (trimmed.StartsWith('<') || trimmed.StartsWith('/')) return null;

        var firstLine = trimmed.Split('\n')[0];
        var words = firstLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var line = string.Join(' ', words);
        if (line.Length == 0) return null;
        return line.Length <= MaxLength ? line : line[..(MaxLength - 1)] + "…";
    }
}
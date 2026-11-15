using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// What a session was about, for its heading: the first message the owner typed, in one line and shortened. System notices
/// (lines that start with "&lt;") and slash commands are skipped, and so are tool results, which carry no text of the owner's.
/// </summary>
public static class SessionTitle
{
    /// <summary>The longest title, in characters. A longer one is cut and ends with an ellipsis.</summary>
    public const int MaxLength = 80;

    /// <summary>The text of a user record when it is a real message; otherwise null.</summary>
    public static string? FromUserRecord(JsonElement root)
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
        return Clean(text);
    }

    /// <summary>The first line of a message, in single spaces, shortened to <see cref="MaxLength"/>. Null when it is empty or a notice.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        // The app's own briefs (a planner, a coder or a solo session) are not what the owner wrote. A brief that carries a
        // task shows the task; one without a task, and the restart prompt, are skipped.
        if (trimmed.StartsWith("You were restarted", StringComparison.Ordinal)) return null;
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
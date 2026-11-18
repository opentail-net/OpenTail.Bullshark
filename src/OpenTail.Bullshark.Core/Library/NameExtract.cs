using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// The text of a past session that the magic rename gives the model (magic rename, D4): the owner's and Claude's text messages in order,
/// each one labelled "You:" or "Claude:" and capped. Tool calls, tool results and thinking are left out, and so is command and system
/// boilerplate. A session that fits the cap is sent whole; a longer one keeps its first 40% and its last 60% of the cap, with a marker
/// between. Pure: it reads the lines it is given and keeps no more than the cap in memory, plus a small rolling tail.
/// </summary>
public static class NameExtract
{
    /// <summary>The whole extract's cap, in characters (about 4 to 5 thousand tokens).</summary>
    public const int DefaultMaxChars = 16_000;

    /// <summary>The most characters one message keeps; a longer message is cut and ends with an ellipsis.</summary>
    public const int MessageCap = 1_200;

    /// <summary>What stands between the first and the last part of a session that is too long to send whole.</summary>
    public const string Marker = "\n[... middle of the session left out ...]\n";

    /// <summary>The extract of a session's transcript lines, or null when it has no text messages at all (for example only tool calls).</summary>
    /// <param name="lines">The transcript's lines (JSON Lines). A line that is not JSON, or whose values cannot be read, is skipped.</param>
    /// <param name="maxChars">The cap of the whole extract. It must be at least twice the length of <see cref="Marker"/>.</param>
    public static string? Build(IEnumerable<string> lines, int maxChars = DefaultMaxChars)
    {
        if (maxChars < 2 * Marker.Length) throw new ArgumentOutOfRangeException(nameof(maxChars));
        var accumulator = new Accumulator(maxChars);
        foreach (var line in lines)
        {
            if (!TranscriptJson.TryParse(line, out var document)) continue;
            using (document)
            {
                foreach (var piece in PiecesOf(document.RootElement)) accumulator.Add(piece);
            }
        }
        return accumulator.Result();
    }

    /// <summary>The labelled messages of one record: none for a record that is not a message the model should see.</summary>
    private static IEnumerable<string> PiecesOf(JsonElement root)
    {
        List<string> pieces = [];
        try
        {
            var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
            if (type is not ("user" or "assistant")) return pieces;
            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return pieces;
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return pieces;
            if (!message.TryGetProperty("content", out var content)) return pieces;

            var label = type == "user" ? "You" : "Claude";
            if (content.ValueKind == JsonValueKind.String)
            {
                AddText(pieces, label, content.GetString());
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    // Only text blocks: tool calls, tool results and thinking are not what the session was about.
                    if (block.ValueKind != JsonValueKind.Object) continue;
                    if (!block.TryGetProperty("type", out var blockType) || blockType.ValueKind != JsonValueKind.String || blockType.GetString() != "text") continue;
                    if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) AddText(pieces, label, text.GetString());
                }
            }
        }
        catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
        {
            // A record whose values cannot be read counts for nothing, as in the index.
            pieces.Clear();
        }
        return pieces;
    }

    private static void AddText(List<string> pieces, string label, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var trimmed = text.Trim();
        if (IsBoilerplate(trimmed)) return;
        pieces.Add($"{label}: {Cap(trimmed)}");
    }

    /// <summary>Command and system wrappers, and the briefs the app writes itself. Their text is not the owner's or Claude's talk.</summary>
    private static bool IsBoilerplate(string text) =>
        text.StartsWith('<') || text.StartsWith('/') || text.Contains("<system-reminder>", StringComparison.Ordinal)
        || text.StartsWith("You were restarted", StringComparison.Ordinal)
        || text.StartsWith("Continues session:", StringComparison.Ordinal)
        || text.StartsWith("This session continues an earlier Claude Code session", StringComparison.Ordinal);

    private static string Cap(string text)
    {
        if (text.Length <= MessageCap) return text;
        var cut = text[..(MessageCap - 1)];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut + "…";
    }

    /// <summary>
    /// Collects the labelled messages. While the whole text fits the cap it is kept whole. Once it does not, only the first part and a
    /// rolling tail are kept, so a long transcript does not stay in memory.
    /// </summary>
    private sealed class Accumulator
    {
        private readonly int _maxChars;
        private readonly int _headBudget;
        private readonly int _tailBudget;
        private readonly StringBuilder _whole = new();
        private readonly StringBuilder _head = new();
        private readonly StringBuilder _tail = new();
        private long _total;
        private bool _overflowed;

        public Accumulator(int maxChars)
        {
            _maxChars = maxChars;
            var budget = maxChars - Marker.Length;
            _headBudget = budget * 40 / 100;
            _tailBudget = budget - _headBudget;
        }

        public void Add(string piece)
        {
            var line = piece + "\n";
            _total += line.Length;
            if (!_overflowed)
            {
                if (_total <= _maxChars)
                {
                    _whole.Append(line);
                }
                else
                {
                    _overflowed = true;
                    _whole.Clear();
                }
            }
            if (_head.Length < _headBudget) _head.Append(line, 0, Math.Min(line.Length, _headBudget - _head.Length));
            _tail.Append(line);
            if (_tail.Length > 2 * _tailBudget) _tail.Remove(0, _tail.Length - _tailBudget);
        }

        public string? Result()
        {
            if (_total == 0) return null;
            if (!_overflowed) return _whole.ToString().TrimEnd('\n');

            var head = _head.ToString();
            if (head.Length > 0 && char.IsHighSurrogate(head[^1])) head = head[..^1];
            var tailText = _tail.ToString();
            var tail = tailText[Math.Max(0, tailText.Length - _tailBudget)..];
            if (tail.Length > 0 && char.IsLowSurrogate(tail[0])) tail = tail[1..];
            return (head + Marker + tail).TrimEnd('\n');
        }
    }
}

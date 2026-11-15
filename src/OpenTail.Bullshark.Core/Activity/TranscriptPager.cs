using System.Text;
using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>One record of a transcript as a person reads it. Tool results are collapsed to a short preview.</summary>
public sealed record TranscriptLine(int Number, string Kind, DateTimeOffset? Time, string Text, bool Collapsed)
{
    private const int PreviewLength = 120;

    /// <summary>
    /// Turns one transcript record into a line for display. Returns null for records that are not shown
    /// (metadata, snapshots, thinking, and anything unreadable).
    /// </summary>
    public static TranscriptLine? From(int number, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var type = Str(root, "type");
            var time = TimeOf(root);

            switch (type)
            {
                case "user":
                    return UserLine(number, root, time);

                case "assistant":
                    return AssistantLine(number, root, time);

                case "system" when Str(root, "subtype") == "turn_duration":
                    var seconds = root.TryGetProperty("durationMs", out var ms) && ms.ValueKind == JsonValueKind.Number
                        ? ms.GetDouble() / 1000.0
                        : 0;
                    return new TranscriptLine(number, "turn", time, $"turn finished ({seconds:0.#} s)", Collapsed: false);

                default:
                    return null;
            }
        }
    }

    private static TranscriptLine? UserLine(int number, JsonElement root, DateTimeOffset? time)
    {
        if (!root.TryGetProperty("message", out var message)) return null;
        if (!message.TryGetProperty("content", out var content)) return null;

        // A plain typed message.
        if (content.ValueKind == JsonValueKind.String)
            return new TranscriptLine(number, "you", time, content.GetString() ?? string.Empty, Collapsed: false);

        // A list of blocks: tool results come back as user records, and they are collapsed.
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (Str(block, "type") != "tool_result") continue;
                var preview = ResultText(block);
                var shown = preview.Length > PreviewLength ? preview[..PreviewLength] + "…" : preview;
                return new TranscriptLine(number, "result", time, $"result: {shown}", Collapsed: true);
            }
            foreach (var block in content.EnumerateArray())
            {
                if (Str(block, "type") == "text")
                    return new TranscriptLine(number, "you", time, Str(block, "text") ?? string.Empty, Collapsed: false);
            }
        }
        return null;
    }

    private static TranscriptLine? AssistantLine(int number, JsonElement root, DateTimeOffset? time)
    {
        if (!root.TryGetProperty("message", out var message)) return null;
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;

        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "text":
                    var text = Str(block, "text");
                    if (!string.IsNullOrWhiteSpace(text))
                        return new TranscriptLine(number, "claude", time, text, Collapsed: false);
                    break;

                case "tool_use":
                    var name = Str(block, "name") ?? "tool";
                    var target = block.TryGetProperty("input", out var input) ? Str(input, "file_path") ?? Str(input, "command") : null;
                    var detail = target is null ? string.Empty : " " + Short(target);
                    return new TranscriptLine(number, "tool", time, $"▸ {name}{detail}", Collapsed: true);
            }
        }
        return null;
    }

    private static string ResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content)) return string.Empty;
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (Str(part, "type") == "text") return Str(part, "text") ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static string Short(string value) => value.Length > 100 ? "…" + value[^99..] : value;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? TimeOf(JsonElement root) =>
        Str(root, "timestamp") is { } raw && DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}

/// <summary>
/// Reads a transcript page by page without loading it whole. Opening scans the file once for line starts (a few bytes
/// per line); a page is then read from its byte offset. The history view opens on the last page.
/// </summary>
public sealed class TranscriptPager
{
    private readonly string _path;
    private readonly long[] _lineStarts;

    private TranscriptPager(string path, long[] lineStarts)
    {
        _path = path;
        _lineStarts = lineStarts;
    }

    /// <summary>Number of lines (records, including ones that are not shown).</summary>
    public int LineCount => _lineStarts.Length;

    public static TranscriptPager Open(string path)
    {
        var starts = new List<long> { 0 };
        long position = 0;
        var buffer = new byte[1 << 20];
        using (var stream = File.OpenRead(path))
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == (byte)'\n') starts.Add(position + i + 1);
                }
                position += read;
            }
        }
        // A trailing newline does not start another line.
        if (starts.Count > 1 && starts[^1] == position) starts.RemoveAt(starts.Count - 1);
        return new TranscriptPager(path, starts.ToArray());
    }

    /// <summary>Reads lines [start, start + count), clipped to the file. Records that are not shown are dropped.</summary>
    public IReadOnlyList<TranscriptLine> ReadPage(int start, int count)
    {
        var result = new List<TranscriptLine>();
        if (start < 0 || start >= LineCount || count <= 0) return result;

        int end = Math.Min(LineCount, start + count);
        using var stream = File.OpenRead(_path);
        stream.Seek(_lineStarts[start], SeekOrigin.Begin);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16, leaveOpen: true);

        for (int n = start; n < end; n++)
        {
            var text = reader.ReadLine();
            if (text is null) break;
            var line = TranscriptLine.From(n + 1, text);
            if (line is not null) result.Add(line);
        }
        return result;
    }

    /// <summary>The first line to show when a transcript opens: the start of its last page.</summary>
    public int LastPageStart(int pageSize) => Math.Max(0, LineCount - pageSize);
}

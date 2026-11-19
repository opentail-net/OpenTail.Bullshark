using System.Text;
using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>One record of a transcript as a person reads it. Tool results are collapsed to a short preview.</summary>
public sealed record TranscriptLine(int Number, string Kind, DateTimeOffset? Time, string Text, bool Collapsed, bool Failed = false)
{
    private const int PreviewLength = 120;

    /// <summary>
    /// Turns one transcript record into a line for display. Returns null for records that are not shown
    /// (metadata, snapshots, thinking, and anything unreadable).
    /// </summary>
    public static TranscriptLine? From(int number, string json)
    {
        if (!TranscriptJson.TryParse(json, out var document)) return null;

        using (document)
        {
            try
            {
                return FromRoot(number, document.RootElement);
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                // A value of the record cannot be read: the record is not shown (round 12 item A1).
                return null;
            }
        }
    }

    /// <summary>
    /// The line for a parsed record, as <see cref="From(int, string)"/> gives it, but without the guard: a value that cannot be read throws
    /// <see cref="InvalidOperationException"/>. A reader that counts the record as well (the turn and effort facts) calls this, so that it
    /// skips the record whole.
    /// </summary>
    internal static TranscriptLine? FromRoot(int number, JsonElement root)
    {
        var type = TranscriptJson.Str(root, "type");
        var time = TranscriptJson.Time(root, "timestamp");

        switch (type)
        {
            case "user":
                return UserLine(number, root, time);

            case "assistant":
                return AssistantLine(number, root, time);

            case "system" when TranscriptJson.Str(root, "subtype") == "turn_duration":
                var seconds = root.TryGetProperty("durationMs", out var ms) && ms.ValueKind == JsonValueKind.Number
                    ? ms.GetDouble() / 1000.0
                    : 0;
                return new TranscriptLine(number, "turn", time, $"turn finished ({seconds:0.#} s)", Collapsed: false);

            case "system" when TranscriptJson.Str(root, "subtype") == "compact_boundary":
                return CompactLine(number, root, time);

            default:
                return null;
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
                if (TranscriptJson.Str(block, "type") != "tool_result") continue;
                var preview = ResultText(block);
                var shown = preview.Length > PreviewLength ? preview[..CutBefore(preview, PreviewLength)] + "…" : preview;
                return new TranscriptLine(number, "result", time, $"result: {shown}", Collapsed: true, Failed: IsFailedResult(block));
            }
            foreach (var block in content.EnumerateArray())
            {
                if (TranscriptJson.Str(block, "type") == "text")
                    return new TranscriptLine(number, "you", time, TranscriptJson.Str(block, "text") ?? string.Empty, Collapsed: false);
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
            switch (TranscriptJson.Str(block, "type"))
            {
                case "text":
                    var text = TranscriptJson.Str(block, "text");
                    if (!string.IsNullOrWhiteSpace(text))
                        return new TranscriptLine(number, "claude", time, text, Collapsed: false);
                    break;

                case "tool_use":
                    var name = TranscriptJson.Str(block, "name") ?? "tool";
                    var target = block.TryGetProperty("input", out var input) ? TranscriptJson.Str(input, "file_path") ?? TranscriptJson.Str(input, "command") : null;
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
                if (TranscriptJson.Str(part, "type") == "text") return TranscriptJson.Str(part, "text") ?? string.Empty;
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// A compaction: the context was cut here (D-A3). The text gives the trigger and the token counts when the record has them
    /// (observed on 2026-11-16: <c>compactMetadata</c> with <c>trigger</c>, <c>preTokens</c>, <c>postTokens</c>).
    /// </summary>
    private static TranscriptLine CompactLine(int number, JsonElement root, DateTimeOffset? time)
    {
        var meta = root.TryGetProperty("compactMetadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        var trigger = TranscriptJson.Str(meta, "trigger");
        var before = TranscriptJson.Number(meta, "preTokens");
        var after = TranscriptJson.Number(meta, "postTokens");
        var head = trigger is null ? "conversation compacted" : $"conversation compacted ({trigger})";
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var text = before is { } pre && after is { } post
            ? $"{head}: {pre.ToString("N0", culture)} → {post.ToString("N0", culture)} tokens"
            : head;
        return new TranscriptLine(number, "compact", time, text, Collapsed: false);
    }

    /// <summary>
    /// True when a tool result says it failed: <c>is_error</c> is true on the block (observed on 78 transcript files on
    /// 2026-11-16). The reader and the turn facts both use this one test.
    /// </summary>
    public static bool IsFailedResult(JsonElement block) =>
        block.ValueKind == JsonValueKind.Object &&
        block.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The whole text of a tool result block, not cut: its string content, or each text part of its content list, joined by new lines.
    /// Used by find and by the full-result view; the line's own text is the cut preview (<see cref="ResultText"/>).
    /// </summary>
    internal static string FullResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content)) return string.Empty;
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return string.Empty;
        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (TranscriptJson.Str(part, "type") == "text") parts.Add(TranscriptJson.Str(part, "text") ?? string.Empty);
        }
        return string.Join('\n', parts);
    }

    private static string Short(string value) => value.Length > 100 ? "…" + value[CutAfter(value, value.Length - 99)..] : value;

    /// <summary>A cut at <paramref name="index"/> that does not split a surrogate pair (an emoji would show as a broken glyph).</summary>
    private static int CutBefore(string value, int index) =>
        index > 0 && index < value.Length && char.IsLowSurrogate(value[index]) ? index - 1 : index;

    private static int CutAfter(string value, int index) =>
        index > 0 && index < value.Length && char.IsLowSurrogate(value[index]) ? index + 1 : index;

}

/// <summary>
/// Reads a transcript page by page without loading it whole. Opening scans the file once for line starts (a few bytes
/// per line); a page is then read from its byte offset. The history view opens on the last page.
/// </summary>
public sealed class TranscriptPager
{
    private readonly string _path;
    private readonly long[] _lineStarts;

    internal TranscriptPager(string path, long[] lineStarts)
    {
        _path = path;
        _lineStarts = lineStarts;
    }

    /// <summary>Number of lines (records, including ones that are not shown).</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>The line starts of the transcripts this process opens, kept per file (T5 batch). The views share it.</summary>
    private static readonly TranscriptPagerCache Shared = new();

    /// <summary>
    /// How many times the file was scanned for its line starts while the shared cache kept them. A pager opened on an unchanged file reuses the
    /// starts, so this does not move (see <see cref="TranscriptPagerCache.ScansOf"/>).
    /// </summary>
    public static int ScansOf(string path) => Shared.ScansOf(path);

    /// <summary>
    /// Opens a transcript for paging. The line starts are kept per file, with the file's length and write time: a file that has not changed since
    /// its last open is not scanned again, so a jump in a long transcript is quick. A changed file is scanned again (T5 batch).
    /// </summary>
    public static TranscriptPager Open(string path) => Shared.Open(path);

    /// <summary>Scans a file once for the offset of each line (a few bytes per line).</summary>
    internal static long[] Scan(string path)
    {
        var starts = new List<long> { 0 };
        long position = 0;
        var buffer = new byte[1 << 20];
        using (var stream = TranscriptFile.Open(path))
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
        return starts.ToArray();
    }

    /// <summary>
    /// The raw text of one line, as the file holds it (its JSON), or null when the line is past the end. <paramref name="number"/> is the line's
    /// number as the view shows it (one-based). Read on demand, for the full result and the copy of a command.
    /// </summary>
    public string? ReadRaw(int number)
    {
        var index = number - 1;
        if (index < 0 || index >= LineCount) return null;

        using var stream = TranscriptFile.Open(_path);
        stream.Seek(_lineStarts[index], SeekOrigin.Begin);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16, leaveOpen: true);
        return reader.ReadLine();
    }

    /// <summary>Reads lines [start, start + count), clipped to the file. Records that are not shown are dropped.</summary>
    public IReadOnlyList<TranscriptLine> ReadPage(int start, int count) => ReadPage(start, count, CancellationToken.None);

    /// <summary>
    /// The same page, read on a pool thread by the view (perf and testability batch, Q3). The token is looked at before each record, so a page
    /// that is no longer wanted stops early and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public IReadOnlyList<TranscriptLine> ReadPage(int start, int count, CancellationToken cancellationToken)
    {
        var result = new List<TranscriptLine>();
        if (start < 0 || start >= LineCount || count <= 0) return result;

        int end = Math.Min(LineCount, start + count);
        using var stream = TranscriptFile.Open(_path);
        stream.Seek(_lineStarts[start], SeekOrigin.Begin);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16, leaveOpen: true);

        for (int n = start; n < end; n++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

/// <summary>
/// The line starts of transcripts, kept per file with the file's key (path, length, write time): a file that has not changed since its last open is
/// not scanned again, and a changed file is scanned again (T5 batch). At most <see cref="MaxFiles"/> files are kept. An instance is one cache: the
/// pager's <see cref="TranscriptPager.Open"/> uses one shared instance, and a test makes its own so that no other reader can change its counts.
/// </summary>
public sealed class TranscriptPagerCache(int maxFiles = 8)
{
    private readonly Dictionary<string, (TranscriptFileKey Key, long[] Starts, int Scans)> _startsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>The most files whose line starts are kept. Past it, one other file is dropped (which one is not promised; it starts again when opened).</summary>
    public int MaxFiles { get; } = maxFiles > 0 ? maxFiles : throw new ArgumentOutOfRangeException(nameof(maxFiles));

    /// <summary>
    /// How many times the file was scanned while its starts were kept in this cache. A file dropped from the cache starts again at 0, and the count
    /// carries over when a changed file is scanned again.
    /// </summary>
    public int ScansOf(string path)
    {
        lock (_gate) return _startsByPath.TryGetValue(path, out var known) ? known.Scans : 0;
    }

    /// <summary>Opens a transcript for paging through this cache.</summary>
    public TranscriptPager Open(string path)
    {
        var key = TranscriptFileKey.Of(path);
        var scans = 0;
        lock (_gate)
        {
            if (_startsByPath.TryGetValue(path, out var known))
            {
                if (known.Key == key) return new TranscriptPager(path, known.Starts);
                scans = known.Scans;
            }
        }

        var starts = TranscriptPager.Scan(path);
        lock (_gate)
        {
            _startsByPath[path] = (key, starts, scans + 1);
            // The file just opened is kept: another one is dropped. (The dictionary may reuse a freed slot, so the new entry can come first.)
            while (_startsByPath.Count > MaxFiles)
                _startsByPath.Remove(_startsByPath.Keys.First(known => !StringComparer.OrdinalIgnoreCase.Equals(known, path)));
        }
        return new TranscriptPager(path, starts);
    }
}

using System.Globalization;
using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// One turn of a session: a typed prompt, the work after it, and the record that ends it (transcript-activity.md, TA3).
/// <see cref="StartLine"/> and <see cref="EndLine"/> are file line numbers, one-based, as <see cref="TranscriptPager"/> numbers
/// them. <see cref="EndLine"/> is the line of the turn's "turn finished" record, which is where the transcript view shows the facts.
/// </summary>
public sealed record TurnFact(
    int StartLine,
    int EndLine,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string Prompt,
    int ToolCalls,
    int FailedCalls,
    IReadOnlyDictionary<string, int> ToolNames,
    IReadOnlyList<string> FilesWritten,
    double? DurationSeconds,
    int Compactions);

/// <summary>
/// Reads the turns of a transcript, once, from its lines. A turn starts at a line the transcript view shows as "you" (the rule
/// resume text uses, D-A5). It ends at its <c>turn_duration</c> record; a turn with none ends at its last record before the next
/// prompt, or at the end of the file, and its duration is null. Tool calls and compactions before the first prompt form a turn
/// with an empty prompt. Lines that are blank or not JSON are skipped, but they still count in the line numbers.
/// </summary>
public static class TurnFacts
{
    private const int PromptLength = 120;

    public static IReadOnlyList<TurnFact> ReadFile(string path) => Read(TranscriptFile.ReadLines(path));

    public static IReadOnlyList<TurnFact> Read(IEnumerable<string> lines)
    {
        var turns = new List<TurnFact>();
        Turn? open = null;
        var number = 0;
        var lastRecord = 0;
        DateTimeOffset? lastTime = null;
        DateTimeOffset? time = null;

        foreach (var line in lines)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                var type = Str(root, "type");
                time = TimeOf(root);
                var content = ContentOf(root);

                if (type == "user" && TranscriptLine.From(number, line) is { Kind: "you" } prompt)
                {
                    // A typed prompt starts a turn. The previous turn, if it never got a turn_duration, ends at its last record.
                    if (open is not null) turns.Add(open.Close(lastRecord, lastTime, null));
                    open = new Turn(number, time, Clip(prompt.Text));
                }
                else if (type == "system" && Str(root, "subtype") == "turn_duration")
                {
                    if (open is not null)
                    {
                        var ms = Num(root, "durationMs");
                        turns.Add(open.Close(number, time ?? lastTime, ms is { } n ? n / 1000.0 : null));
                        open = null;
                    }
                }
                else if (type == "system" && Str(root, "subtype") == "compact_boundary")
                {
                    open ??= new Turn(number, time, string.Empty);
                    open.Compactions++;
                }
                else if (type == "assistant" && content is { ValueKind: JsonValueKind.Array } blocks)
                {
                    foreach (var block in blocks.EnumerateArray())
                    {
                        // A call is counted once per id: the same message can be written in more than one record.
                        if (Str(block, "type") != "tool_use" || Str(block, "id") is not { } id) continue;
                        open ??= new Turn(number, time, string.Empty);
                        if (!open.ToolIds.Add(id)) continue;
                        open.ToolCalls++;
                        var name = Str(block, "name") ?? "unknown";
                        open.CountTool(name);
                        if (TranscriptReader.FileWritingTools.Contains(name) &&
                            block.TryGetProperty("input", out var input) && Str(input, "file_path") is { } path)
                        {
                            open.AddFile(path);
                        }
                    }
                }
                else if (type == "user" && content is { ValueKind: JsonValueKind.Array } results && open is not null)
                {
                    foreach (var block in results.EnumerateArray())
                    {
                        if (Str(block, "type") != "tool_result" || !TranscriptLine.IsFailedResult(block)) continue;
                        if (Str(block, "tool_use_id") is { } failedId && open.FailedIds.Add(failedId)) open.FailedCalls++;
                    }
                }
            }

            lastRecord = number;
            if (time is { } at) lastTime = at;
        }

        if (open is not null) turns.Add(open.Close(lastRecord, lastTime, null));
        return turns;
    }

    /// <summary>A turn while it is open. Its counts are kept here until it closes.</summary>
    private sealed class Turn
    {
        private readonly Dictionary<string, int> _toolNames = new(StringComparer.Ordinal);
        private readonly List<string> _files = [];
        private readonly HashSet<string> _fileSet = new(StringComparer.Ordinal);

        public Turn(int startLine, DateTimeOffset? start, string prompt)
        {
            StartLine = startLine;
            Start = start;
            Prompt = prompt;
        }

        public int StartLine { get; }
        public DateTimeOffset? Start { get; }
        public string Prompt { get; }
        public HashSet<string> ToolIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FailedIds { get; } = new(StringComparer.Ordinal);
        public int ToolCalls { get; set; }
        public int FailedCalls { get; set; }
        public int Compactions { get; set; }

        public IReadOnlyDictionary<string, int> ToolNames => _toolNames;

        public void CountTool(string name) =>
            _toolNames[name] = _toolNames.TryGetValue(name, out var count) ? count + 1 : 1;

        public void AddFile(string path)
        {
            if (_fileSet.Add(path)) _files.Add(path);
        }

        public TurnFact Close(int endLine, DateTimeOffset? end, double? durationSeconds) =>
            new(StartLine, endLine, Start, end, Prompt, ToolCalls, FailedCalls,
                new Dictionary<string, int>(_toolNames, StringComparer.Ordinal), _files.ToList(), durationSeconds, Compactions);
    }

    /// <summary>The prompt as one line, cut to the length the facts show.</summary>
    private static string Clip(string text)
    {
        var oneLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= PromptLength ? oneLine : oneLine[..PromptLength] + "…";
    }

    private static JsonElement? ContentOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var message) &&
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var content)
            ? content
            : null;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : null;

    private static DateTimeOffset? TimeOf(JsonElement root) =>
        Str(root, "timestamp") is { } raw &&
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}

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
/// with an empty prompt. Lines that are blank or not JSON are skipped, but they still count in the line numbers. So is a line one of
/// whose values cannot be read: it is neither a record nor a last record (round 12 item A1).
/// </summary>
public static class TurnFacts
{
    private const int PromptLength = 120;

    public static IReadOnlyList<TurnFact> ReadFile(string path) => Read(TranscriptFile.ReadLines(path));

    public static IReadOnlyList<TurnFact> Read(IEnumerable<string> lines) => ReadWithMarks(lines).Turns;

    /// <summary>The turns of a file and its jump marks, read in one pass (T5 batch).</summary>
    public static (IReadOnlyList<TurnFact> Turns, TranscriptMarks Marks) ReadFileWithMarks(string path) => ReadWithMarks(TranscriptFile.ReadLines(path));

    /// <summary>
    /// The turns and the jump marks of the lines, in one pass. The marks are the lines a jump key goes to: the typed prompts, the failed tool results
    /// (the line the view shows in red), the first call of each file-writing tool, and the compactions. A line that cannot be read is in neither.
    /// </summary>
    public static (IReadOnlyList<TurnFact> Turns, TranscriptMarks Marks) ReadWithMarks(IEnumerable<string> lines)
    {
        var turns = new List<TurnFact>();
        var prompts = new List<int>();
        var failures = new List<int>();
        var writes = new List<int>();
        var compactions = new List<int>();
        var writeIds = new HashSet<string>(StringComparer.Ordinal);
        Turn? open = null;
        var number = 0;
        var lastRecord = 0;
        DateTimeOffset? lastTime = null;
        var facts = new LineFacts();

        foreach (var line in lines)
        {
            number++;
            if (!TranscriptJson.TryParse(line, out var document)) continue;

            // Every value the line is counted from is read first, so a line that cannot be read changes nothing.
            using (document)
            {
                try
                {
                    facts.Read(document.RootElement, number);
                }
                catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
                {
                    continue;
                }
            }

            var time = facts.Time;
            if (facts.Type == "user" && facts.PromptText is { } promptText)
            {
                // A typed prompt starts a turn. The previous turn, if it never got a turn_duration, ends at its last record.
                if (open is not null) turns.Add(open.Close(lastRecord, lastTime, null));
                open = new Turn(number, time, Clip(promptText));
                prompts.Add(number);
            }
            else if (facts.Type == "system" && facts.Subtype == "turn_duration")
            {
                if (open is not null)
                {
                    turns.Add(open.Close(number, time ?? lastTime, facts.DurationMs is { } ms ? ms / 1000.0 : null));
                    open = null;
                }
            }
            else if (facts.Type == "system" && facts.Subtype == "compact_boundary")
            {
                open ??= new Turn(number, time, string.Empty);
                open.Compactions++;
                compactions.Add(number);
            }
            else if (facts.Type == "assistant" && facts.ContentIsArray)
            {
                foreach (var block in facts.Blocks)
                {
                    // A call is counted once per id: the same message can be written in more than one record.
                    if (block.Id is not { } id) continue;
                    // The first record of each file write is a mark, whether or not a turn is open (T5 batch).
                    if (block.Name is { } written && TranscriptReader.FileWritingTools.Contains(written) && writeIds.Add(id)) writes.Add(number);
                    open ??= new Turn(number, time, string.Empty);
                    if (!open.ToolIds.Add(id)) continue;
                    open.ToolCalls++;
                    var name = block.Name ?? "unknown";
                    open.CountTool(name);
                    if (block.FilePath is { } path) open.AddFile(path);
                }
            }
            else if (facts.Type == "user" && facts.ContentIsArray && open is not null)
            {
                foreach (var failedId in facts.FailedResultIds)
                {
                    if (failedId is not null && open.FailedIds.Add(failedId)) open.FailedCalls++;
                }
            }

            // The failed result the view shows in red: the first tool result of the record is the one it shows (see TranscriptLine).
            if (facts.Type == "user" && facts.FirstResultFailed) failures.Add(number);

            lastRecord = number;
            if (time is { } at) lastTime = at;
        }

        if (open is not null) turns.Add(open.Close(lastRecord, lastTime, null));
        return (turns, new TranscriptMarks(prompts, failures, writes, compactions));
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

    /// <summary>
    /// The values of one transcript line that the turns count from, read before anything is counted (round 12 item A1). A value that cannot be
    /// read throws, and the line is skipped. The counting itself is in <see cref="Read"/>, in the order of the records. One instance serves a file.
    /// </summary>
    private sealed class LineFacts
    {
        private readonly List<ToolBlock> _blocks = [];
        private readonly List<string?> _failedResultIds = [];

        public DateTimeOffset? Time { get; private set; }
        public string? Type { get; private set; }
        public string? Subtype { get; private set; }
        public long? DurationMs { get; private set; }
        public string? PromptText { get; private set; }
        public bool ContentIsArray { get; private set; }

        /// <summary>Whether the record's first tool result failed: the one the view shows for the record (T5 batch).</summary>
        public bool FirstResultFailed { get; private set; }
        public IReadOnlyList<ToolBlock> Blocks => _blocks;
        public IReadOnlyList<string?> FailedResultIds => _failedResultIds;

        public void Read(JsonElement root, int number)
        {
            Time = TranscriptJson.Time(root, "timestamp");
            Type = TranscriptJson.Str(root, "type");
            Subtype = Type == "system" ? TranscriptJson.Str(root, "subtype") : null;
            DurationMs = Subtype == "turn_duration" ? TranscriptJson.Number(root, "durationMs") : null;
            PromptText = Type == "user" && TranscriptLine.FromRoot(number, root) is { Kind: "you" } prompt ? prompt.Text : null;

            _blocks.Clear();
            _failedResultIds.Clear();
            ContentIsArray = false;
            FirstResultFailed = false;
            if (ContentOf(root) is not { ValueKind: JsonValueKind.Array } content) return;
            ContentIsArray = true;

            var sawResult = false;
            foreach (var block in content.EnumerateArray())
            {
                var blockType = TranscriptJson.Str(block, "type");
                if (blockType == "tool_use")
                {
                    var name = TranscriptJson.Str(block, "name");
                    string? filePath = null;
                    if (TranscriptReader.FileWritingTools.Contains(name ?? "unknown") && block.TryGetProperty("input", out var input))
                        filePath = TranscriptJson.Str(input, "file_path");
                    _blocks.Add(new ToolBlock(TranscriptJson.Str(block, "id"), name, filePath));
                }
                else if (blockType == "tool_result")
                {
                    var failed = TranscriptLine.IsFailedResult(block);
                    if (!sawResult)
                    {
                        sawResult = true;
                        FirstResultFailed = failed;
                    }
                    if (failed) _failedResultIds.Add(TranscriptJson.Str(block, "tool_use_id"));
                }
            }
        }
    }

    /// <summary>A tool call of an assistant message as the turns count it: its id, its name, and the file it names when it writes one.</summary>
    private readonly record struct ToolBlock(string? Id, string? Name, string? FilePath);
}

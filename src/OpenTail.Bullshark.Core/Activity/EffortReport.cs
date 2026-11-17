using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// One line of the effort panel: a label (a tool, a file or a command), how many times it happened, and the first line of the
/// transcript where it happened, so the view can jump there. Line numbers are one-based, as <see cref="TurnFact"/> numbers them.
/// </summary>
public sealed record EffortItem(string Label, int Count, int Line);

/// <summary>A turn that wrote no file and gave no text answer, with the tokens its messages used (cache reads are in <see cref="TokenUsage.CacheRead"/>).</summary>
public sealed record EffortTurn(int StartLine, string Prompt, TokenUsage Tokens);

/// <summary>
/// Where a session's effort went (gems item 4, from dashboard-for-claude-code and token-dashboard). Computed from the transcript
/// lines, with no model involved. The rules:
/// <list type="bullet">
/// <item>Failed tool calls, counted per tool (a call that fails is counted once, however often its result is written).</item>
/// <item>Files read more than <see cref="RereadThreshold"/> times.</item>
/// <item>Shell commands run more than <see cref="RepeatThreshold"/> times, by their exact text.</item>
/// <item>Turns that wrote no file and gave no text answer, with the tokens their messages used. A message counts once, however many
/// records carry it. A turn is the same span <see cref="TurnFacts"/> shows (a typed prompt starts it, its turn_duration ends it).</item>
/// </list>
/// </summary>
public sealed record EffortReport(
    int Turns,
    IReadOnlyList<EffortItem> FailedByTool,
    IReadOnlyList<EffortItem> RereadFiles,
    IReadOnlyList<EffortItem> RepeatedCommands,
    IReadOnlyList<EffortTurn> TurnsWithoutOutput)
{
    /// <summary>A file is listed when it was read more than this many times.</summary>
    public const int RereadThreshold = 3;

    /// <summary>A command is listed when it was run more than this many times.</summary>
    public const int RepeatThreshold = 2;

    /// <summary>All failed tool calls.</summary>
    public int FailedCalls => FailedByTool.Sum(item => item.Count);

    /// <summary>The tokens of the turns without output, summed.</summary>
    public TokenUsage TokensWithoutOutput => TurnsWithoutOutput.Aggregate(TokenUsage.Zero, (sum, turn) => sum.Add(turn.Tokens));

    /// <summary>Reads a transcript file. The file is read once, as a stream.</summary>
    public static EffortReport ReadFile(string path) => From(TranscriptFile.ReadLines(path));

    /// <summary>Builds the report from the lines of a transcript. Lines that are blank or not JSON are skipped, but they count in the line numbers.</summary>
    public static EffortReport From(IEnumerable<string> lines)
    {
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var countedCalls = new HashSet<string>(StringComparer.Ordinal);
        var failedIds = new HashSet<string>(StringComparer.Ordinal);
        var messageKeys = new HashSet<string>(StringComparer.Ordinal);
        var failed = new Tally();
        var rereads = new Tally();
        var commands = new Tally();
        var withoutOutput = new List<EffortTurn>();
        var turns = 0;
        OpenTurn? open = null;

        void CloseTurn()
        {
            if (open is null) return;
            turns++;
            if (!open.Wrote && !open.Texted) withoutOutput.Add(new EffortTurn(open.StartLine, open.Prompt, open.Tokens));
            open = null;
        }

        var number = 0;
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
                var subtype = type == "system" ? Str(root, "subtype") : null;

                if (type == "user" && TranscriptLine.From(number, line) is { Kind: "you" } prompt)
                {
                    CloseTurn();
                    open = new OpenTurn(number, Shorten(prompt.Text));
                }
                else if (subtype == "turn_duration")
                {
                    CloseTurn();
                }
                else if (subtype == "compact_boundary")
                {
                    open ??= new OpenTurn(number, string.Empty);
                }
                else if (type == "assistant" && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    CountUsage(root, message, ref open, messageKeys);
                    if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            var blockType = Str(block, "type");
                            if (blockType == "text")
                            {
                                if (open is not null && !string.IsNullOrWhiteSpace(Str(block, "text"))) open.Texted = true;
                                continue;
                            }
                            if (blockType != "tool_use" || Str(block, "id") is not { } id) continue;

                            var name = Str(block, "name") ?? "unknown";
                            toolNames[id] = name;
                            // A call is counted once per id: the same message can be written in more than one record.
                            open ??= new OpenTurn(number, string.Empty);
                            if (!countedCalls.Add(id)) continue;

                            var input = block.TryGetProperty("input", out var inputElement) ? inputElement : default;
                            if (TranscriptReader.FileWritingTools.Contains(name) && Str(input, "file_path") is not null) open.Wrote = true;
                            if (name == "Read" && Str(input, "file_path") is { } readPath) rereads.Add(readPath, number);
                            if (name == "Bash" && Str(input, "command") is { } command) commands.Add(command, number);
                        }
                    }
                }
                else if (type == "user" && root.TryGetProperty("message", out var userMessage) &&
                         userMessage.ValueKind == JsonValueKind.Object && userMessage.TryGetProperty("content", out var results) &&
                         results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in results.EnumerateArray())
                    {
                        if (Str(block, "type") != "tool_result" || !TranscriptLine.IsFailedResult(block)) continue;
                        if (Str(block, "tool_use_id") is { } failedId && failedIds.Add(failedId))
                            failed.Add(toolNames.TryGetValue(failedId, out var tool) ? tool : "unknown", number);
                    }
                }
            }
        }
        CloseTurn();

        return new EffortReport(
            turns,
            failed.Items(minimumExclusive: 0),
            rereads.Items(minimumExclusive: RereadThreshold, keepWhole: true),
            commands.Items(minimumExclusive: RepeatThreshold),
            withoutOutput);
    }

    /// <summary>
    /// Adds a message's usage to the open turn, once per message. A message is identified by its id and its request id, as the
    /// usage ledger does. A record with no identity is counted where it stands. Usage before the first prompt has no turn to join,
    /// so it is not in any turn.
    /// </summary>
    private static void CountUsage(JsonElement root, JsonElement message, ref OpenTurn? open, HashSet<string> messageKeys)
    {
        if (open is null) return;
        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;

        var key = Str(message, "id") is { } messageId && Str(root, "requestId") is { } requestId ? messageId + ":" + requestId : null;
        if (key is not null && !messageKeys.Add(key)) return;

        open.Tokens = open.Tokens.Add(new TokenUsage(
            Num(usage, "input_tokens"), Num(usage, "output_tokens"),
            Num(usage, "cache_creation_input_tokens"), Num(usage, "cache_read_input_tokens")));
    }

    /// <summary>A turn while it is open. Its flags and counts are kept here until it closes.</summary>
    private sealed class OpenTurn(int startLine, string prompt)
    {
        public int StartLine { get; } = startLine;
        public string Prompt { get; } = prompt;
        public bool Wrote { get; set; }
        public bool Texted { get; set; }
        public TokenUsage Tokens { get; set; } = TokenUsage.Zero;
    }

    /// <summary>Counts per label, with the first line each label appears on.</summary>
    private sealed class Tally
    {
        private readonly Dictionary<string, (int Count, int First)> _counts = new(StringComparer.Ordinal);

        public void Add(string label, int line) =>
            _counts[label] = _counts.TryGetValue(label, out var known) ? (known.Count + 1, known.First) : (1, line);

        /// <summary>
        /// The labels seen more than <paramref name="minimumExclusive"/> times, most first. A label is shortened for the panel unless
        /// <paramref name="keepWhole"/> is set: a file path is kept whole, so the panel can trim it to its width and show its file name (round 6 item 10).
        /// </summary>
        public IReadOnlyList<EffortItem> Items(int minimumExclusive, bool keepWhole = false) =>
            _counts
                .Where(pair => pair.Value.Count > minimumExclusive)
                .Select(pair => new EffortItem(keepWhole ? pair.Key : Shorten(pair.Key), pair.Value.Count, pair.Value.First))
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ToList();
    }

    private const int LabelLength = 120;

    /// <summary>A label or prompt as one line, cut to the length the panel shows.</summary>
    private static string Shorten(string text)
    {
        var oneLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= LabelLength ? oneLine : oneLine[..LabelLength] + "…";
    }

    /// <summary>
    /// A file path as the panel shows it in the width it has (round 6 item 10): the whole path when <paramref name="fits"/> says it fits, else an
    /// ellipsis and as many of its last folders as fit, trimmed at a folder boundary, with the file name always kept. Given a path with no folder,
    /// or one whose file name does not fit even alone, the result is the path, or the ellipsis and the file name, and the view wraps it.
    /// </summary>
    public static string FitPath(string path, Func<string, bool> fits)
    {
        if (fits(path)) return path;
        var separators = new List<int>();
        for (var i = 0; i < path.Length; i++)
            if (path[i] is '\\' or '/') separators.Add(i);
        if (separators.Count == 0) return path;

        // The longest suffix that fits comes first: the folder boundaries are tried from the start of the path to its file name.
        foreach (var at in separators)
        {
            var candidate = "…" + path[at..];
            if (fits(candidate)) return candidate;
        }
        return "…" + path[separators[^1]..];
    }

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : 0;
}

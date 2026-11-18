using System.Security.Cryptography;
using System.Text;
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
/// A call that failed again and again: the same tool with the same input, failing <see cref="Count"/> times in a row with no success of that
/// call in between. <see cref="FirstLine"/> and <see cref="LastLine"/> are the lines of its first and last failed result, so the view can jump
/// to the first one.
/// </summary>
public sealed record EffortLoop(string Tool, int Count, int FirstLine, int LastLine);

/// <summary>
/// Where a session's effort went (gems item 4, from dashboard-for-claude-code and token-dashboard). Computed from the transcript
/// lines, with no model involved. The rules:
/// <list type="bullet">
/// <item>Failed tool calls, counted per tool (a call that fails is counted once, however often its result is written).</item>
/// <item>Files read more than <see cref="RereadThreshold"/> times.</item>
/// <item>Shell commands run more than <see cref="RepeatThreshold"/> times, by their exact text.</item>
/// <item>Turns that wrote no file and gave no text answer, with the tokens their messages used. A message counts once, however many
/// records carry it. A turn is the same span <see cref="TurnFacts"/> shows (a typed prompt starts it, its turn_duration ends it).</item>
/// <item>Failing loops (features batch G3): the same tool with the same input failing <see cref="LoopThreshold"/> times or more in a row for
/// that call, with no success of that call in between. Other calls in between do not break the run; a fail, edit, pass sequence is not
/// a loop. The input is compared by a hash and is never kept.</item>
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

    /// <summary>A call is listed as a failing loop from this many failures in a row.</summary>
    public const int LoopThreshold = 3;

    /// <summary>
    /// The failing loops (G3), in the order they start. Not part of the positional list, so the callers that build a report from the other five
    /// values keep working; it is empty for those.
    /// </summary>
    public IReadOnlyList<EffortLoop> FailingLoops { get; init; } = [];

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
        var facts = new LineFacts();
        // The failing loops (G3): each call's tool and signature, the calls whose result was read, and the run of failures per signature.
        var callKeys = new Dictionary<string, CallKey>(StringComparer.Ordinal);
        var resolvedCalls = new HashSet<string>(StringComparer.Ordinal);
        var streaks = new Dictionary<string, Streak>(StringComparer.Ordinal);
        var loops = new List<EffortLoop>();

        void CloseTurn()
        {
            if (open is null) return;
            turns++;
            if (!open.Wrote && !open.Texted) withoutOutput.Add(new EffortTurn(open.StartLine, open.Prompt, open.Tokens));
            open = null;
        }

        // A run that ends (a success of its call, or the end of the transcript) is a loop when it reached the threshold.
        void EndRun(string signature)
        {
            if (!streaks.Remove(signature, out var run) || run.Count < LoopThreshold) return;
            loops.Add(new EffortLoop(run.Tool, run.Count, run.FirstLine, run.LastLine));
        }

        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (!TranscriptJson.TryParse(line, out var document)) continue;

            // Every value the line is counted from is read first, so a line that cannot be read changes nothing (round 12 item A1).
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

            if (facts.Type == "user" && facts.PromptText is { } promptText)
            {
                CloseTurn();
                open = new OpenTurn(number, Shorten(promptText));
            }
            else if (facts.Subtype == "turn_duration")
            {
                CloseTurn();
            }
            else if (facts.Subtype == "compact_boundary")
            {
                open ??= new OpenTurn(number, string.Empty);
            }
            else if (facts.Type == "assistant" && facts.AssistantMessage)
            {
                CountUsage(facts, ref open, messageKeys);
                if (facts.ContentIsArray)
                {
                    foreach (var block in facts.Blocks)
                    {
                        if (block.Type == "text")
                        {
                            if (open is not null && block.HasText) open.Texted = true;
                            continue;
                        }
                        if (block.Type != "tool_use" || block.Id is not { } callId) continue;

                        var name = block.Name ?? "unknown";
                        toolNames[callId] = name;
                        callKeys[callId] = new CallKey(name, name + "\u001f" + (block.InputHash ?? string.Empty));
                        // A call is counted once per id: the same message can be written in more than one record.
                        open ??= new OpenTurn(number, string.Empty);
                        if (!countedCalls.Add(callId)) continue;

                        if (TranscriptReader.FileWritingTools.Contains(name) && block.FilePath is not null) open.Wrote = true;
                        if (name == "Read" && block.FilePath is { } readPath) rereads.Add(readPath, number);
                        if (name == "Bash" && block.Command is { } command) commands.Add(command, number);
                    }
                }
            }
            else if (facts.Type == "user" && facts.UserResultsArray)
            {
                foreach (var result in facts.Results)
                {
                    if (result.Failed && result.Id is { } failedCallId && failedIds.Add(failedCallId))
                        failed.Add(toolNames.TryGetValue(failedCallId, out var tool) ? tool : "unknown", number);

                    // Only the first result of a call decides its run: a repeated record of the same result changes nothing (G3).
                    if (result.Id is { } resultId && callKeys.TryGetValue(resultId, out var call) && resolvedCalls.Add(resultId))
                    {
                        if (!result.Failed) EndRun(call.Signature);
                        else if (streaks.TryGetValue(call.Signature, out var run)) { run.Count++; run.LastLine = number; }
                        else streaks[call.Signature] = new Streak(call.Tool, 1, number, number);
                    }
                }
            }
        }
        CloseTurn();
        foreach (var signature in streaks.Keys.ToList()) EndRun(signature);

        return new EffortReport(
            turns,
            failed.Items(minimumExclusive: 0),
            rereads.Items(minimumExclusive: RereadThreshold, keepWhole: true),
            commands.Items(minimumExclusive: RepeatThreshold),
            withoutOutput)
        {
            FailingLoops = loops.OrderBy(loop => loop.FirstLine).ToList(),
        };
    }

    /// <summary>
    /// Adds a message's usage to the open turn, once per message. A message is identified by its id and its request id, as the
    /// usage ledger does. A record with no identity is counted where it stands. Usage before the first prompt has no turn to join,
    /// so it is not in any turn.
    /// </summary>
    private static void CountUsage(LineFacts facts, ref OpenTurn? open, HashSet<string> messageKeys)
    {
        if (open is null) return;
        if (facts.Usage is not { } usage) return;
        if (facts.MessageKey is { } key && !messageKeys.Add(key)) return;

        open.Tokens = open.Tokens.Add(usage);
    }

    /// <summary>
    /// The values of one transcript line that the effort report counts from, read before anything is counted (round 12 item A1). A value
    /// that cannot be read throws, and the line is skipped. The counting is in <see cref="From"/>, in the order of the records. A value is read
    /// when the record's shape can carry it, and some are read though the count may not reach them; a read that throws skips the line.
    /// </summary>
    private sealed class LineFacts
    {
        private readonly List<Block> _blocks = [];
        private readonly List<(string? Id, bool Failed)> _results = [];

        public string? Type { get; private set; }
        public string? Subtype { get; private set; }
        public string? PromptText { get; private set; }
        public bool AssistantMessage { get; private set; }
        public bool ContentIsArray { get; private set; }
        public bool UserResultsArray { get; private set; }
        public string? MessageKey { get; private set; }
        public TokenUsage? Usage { get; private set; }
        public IReadOnlyList<Block> Blocks => _blocks;

        /// <summary>The tool results of a user record, in order: the call's id and whether the result is an error.</summary>
        public IReadOnlyList<(string? Id, bool Failed)> Results => _results;

        public void Read(JsonElement root, int number)
        {
            Type = TranscriptJson.Str(root, "type");
            Subtype = Type == "system" ? TranscriptJson.Str(root, "subtype") : null;
            PromptText = Type == "user" && TranscriptLine.FromRoot(number, root) is { Kind: "you" } prompt ? prompt.Text : null;

            AssistantMessage = false;
            ContentIsArray = false;
            UserResultsArray = false;
            MessageKey = null;
            Usage = null;
            _blocks.Clear();
            _results.Clear();

            if (Type == "assistant" && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                AssistantMessage = true;
                MessageKey = TranscriptJson.Str(message, "id") is { } messageId && TranscriptJson.Str(root, "requestId") is { } requestId
                    ? messageId + ":" + requestId
                    : null;
                if (message.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
                {
                    Usage = new TokenUsage(
                        TranscriptJson.Long(usageElement, "input_tokens"), TranscriptJson.Long(usageElement, "output_tokens"),
                        TranscriptJson.Long(usageElement, "cache_creation_input_tokens"), TranscriptJson.Long(usageElement, "cache_read_input_tokens"));
                }
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    ContentIsArray = true;
                    foreach (var block in content.EnumerateArray())
                    {
                        var blockType = TranscriptJson.Str(block, "type");
                        if (blockType == "text")
                        {
                            _blocks.Add(new Block("text", null, null, null, null,
                                !string.IsNullOrWhiteSpace(TranscriptJson.Str(block, "text"))));
                        }
                        else if (blockType == "tool_use")
                        {
                            var input = block.TryGetProperty("input", out var inputElement) ? inputElement : default;
                            _blocks.Add(new Block("tool_use", TranscriptJson.Str(block, "id"), TranscriptJson.Str(block, "name"),
                                TranscriptJson.Str(input, "file_path"), TranscriptJson.Str(input, "command"), false, InputHash(input)));
                        }
                    }
                }
            }
            else if (Type == "user" && root.TryGetProperty("message", out var userMessage) &&
                     userMessage.ValueKind == JsonValueKind.Object && userMessage.TryGetProperty("content", out var results) &&
                     results.ValueKind == JsonValueKind.Array)
            {
                UserResultsArray = true;
                foreach (var block in results.EnumerateArray())
                {
                    if (TranscriptJson.Str(block, "type") == "tool_result")
                        _results.Add((TranscriptJson.Str(block, "tool_use_id"), TranscriptLine.IsFailedResult(block)));
                }
            }
        }

        /// <summary>
        /// A stable hash of a call's input, so two calls with the same input are the same call (G3). Only the hash is kept: the input text is
        /// hashed where it stands and dropped. An input that is not there hashes as the empty text.
        /// </summary>
        private static string InputHash(JsonElement input)
        {
            var text = input.ValueKind == JsonValueKind.Undefined ? string.Empty : input.GetRawText();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
        }
    }

    /// <summary>One block of an assistant message as the report counts it: its type, a call's id, name, file and command, whether a text block has text, and the hash of a call's input.</summary>
    private readonly record struct Block(string? Type, string? Id, string? Name, string? FilePath, string? Command, bool HasText, string? InputHash = null);

    /// <summary>A call as the loop rule keys it: its tool, and the tool with the hash of its input.</summary>
    private sealed record CallKey(string Tool, string Signature);

    /// <summary>The failures of one call signature in a row: the tool, how many, and the lines of the first and the last.</summary>
    private sealed class Streak(string tool, int count, int firstLine, int lastLine)
    {
        public string Tool { get; } = tool;
        public int Count { get; set; } = count;
        public int FirstLine { get; } = firstLine;
        public int LastLine { get; set; } = lastLine;
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

    /// <summary>
    /// The most file paths the panel fits to its width (round 8 item 10). A fit measures every path's text on the UI thread at each resize, so a
    /// long transcript with many file paths made a resize slow. Past this many, the rest are shown whole, and their full path is in the tooltip.
    /// </summary>
    public const int MaxFittedPaths = 200;

    /// <summary>
    /// The text of each file in the panel, in the panel's order, for the width it has. The first <see cref="MaxFittedPaths"/> are trimmed by
    /// <see cref="FitPath"/>, where <paramref name="fits"/> says whether a candidate (the path and its suffix) fits and <c>index</c> is the item's
    /// place in the list; the rest are shown whole. Each item is its path and the text after it, which is never trimmed.
    /// </summary>
    public static IReadOnlyList<string> FitPaths(IReadOnlyList<(string Path, string Suffix)> items, Func<int, string, bool> fits)
    {
        var texts = new List<string>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var (path, suffix) = items[i];
            texts.Add(i < MaxFittedPaths
                ? FitPath(path, candidate => fits(i, candidate + suffix)) + suffix
                : path + suffix);
        }
        return texts;
    }
}

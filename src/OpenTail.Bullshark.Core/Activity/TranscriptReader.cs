using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>Token totals from one or more assistant messages (the `usage` field in each record).</summary>
public sealed record TokenUsage(long Input, long Output, long CacheCreation, long CacheRead)
{
    public static TokenUsage Zero { get; } = new(0, 0, 0, 0);

    public TokenUsage Add(TokenUsage other) => new(
        Input + other.Input, Output + other.Output, CacheCreation + other.CacheCreation, CacheRead + other.CacheRead);
}

/// <summary>What a session's transcript says about its activity. Built from the JSON Lines file on disk.</summary>
public sealed record TranscriptSummary(
    int Records,
    int UserMessages,
    int AssistantMessages,
    int ToolCalls,
    int CompletedTurns,
    string? LastAssistantText,
    DateTimeOffset? LastTimestamp,
    string? LastModel,
    IReadOnlyDictionary<string, TokenUsage> UsageByModel,
    DateTimeOffset? FirstTimestamp = null,
    IReadOnlyDictionary<string, int>? ToolNames = null,
    IReadOnlyList<string>? FilesWritten = null,
    long ContextTokensAtLastTurn = 0,
    string? FirstUserText = null,
    string? AiTitle = null,
    string? CustomTitle = null,
    string? LastRecap = null,
    DateTimeOffset? LastRecapAt = null,
    int Compactions = 0,
    string? ContinuesSessionId = null,
    int FailedToolCalls = 0,
    IReadOnlyDictionary<string, DateTimeOffset>? FileLastWrites = null,
    string? LatestCwd = null,
    /// <summary>
    /// The folder each relative file path was written from, by the cwd of the record that carries the write (round 8 item 8). A path
    /// written before any cwd is named is not in it, and the caller takes the session's folder for it.
    /// </summary>
    IReadOnlyDictionary<string, string>? FileFolders = null)
{
    public TokenUsage TotalUsage => UsageByModel.Values.Aggregate(TokenUsage.Zero, (sum, next) => sum.Add(next));
}

/// <summary>
/// Reads a session transcript. Transcripts live under <c>~\.claude\projects\&lt;encoded cwd&gt;\&lt;sessionId&gt;.jsonl</c>.
/// Format checked on one file on 2026-11-14 (finding F10). The reader does not assume the directory encoding:
/// it searches for the session file by its id.
/// </summary>
public static class TranscriptReader
{
    /// <summary>Tools whose input names a file they change.</summary>
    internal static readonly HashSet<string> FileWritingTools = new(StringComparer.Ordinal)
    {
        "Write", "Edit", "MultiEdit", "NotebookEdit",
    };

    public static string DefaultProjectsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// <summary>Finds the transcript for a session id, or null if it does not exist yet.</summary>
    public static string? FindTranscript(string projectsRoot, string sessionId)
    {
        if (!Directory.Exists(projectsRoot)) return null;
        return Directory.EnumerateFiles(projectsRoot, sessionId + ".jsonl", SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>Summarises one file. Pass a shared <paramref name="ledger"/> to count each message once across files.</summary>
    public static TranscriptSummary ReadFile(string path, UsageLedger? ledger = null) =>
        Summarize(TranscriptFile.ReadLines(path), ledger, path);

    /// <summary>
    /// Summarises transcript lines. Lines that are not valid JSON are skipped, not fatal, and so is a line one of whose values cannot be read
    /// (round 12 item A1): it counts nothing. Usage goes into a <see cref="UsageLedger"/>, once per message (F34, F35, F36). Records and tool calls
    /// are counted for this file. Tool calls are counted once per tool-use id, and assistant messages once per message key.
    /// <paramref name="firstLine"/> is the number of lines before <paramref name="lines"/> in the file, for a summary of the part of a
    /// file that was appended (round 5 item 9): the lines that have no message id are keyed by their line number, so it must go on.
    /// </summary>
    public static TranscriptSummary Summarize(IEnumerable<string> lines, UsageLedger? ledger = null, string? fileName = null, int firstLine = 0)
    {
        var usageLedger = ledger ?? new UsageLedger();
        var file = fileName ?? string.Empty;
        int records = 0, users = 0, assistants = 0, tools = 0, turns = 0, lineNumber = firstLine;
        string? lastText = null, lastModel = null;
        DateTimeOffset? lastTimestamp = null, firstTimestamp = null;
        string? firstUserText = null;
        string? aiTitle = null, customTitle = null, lastRecap = null;
        DateTimeOffset? lastRecapAt = null;
        int compactions = 0;
        string? continuesSessionId = null;
        // Failed tool calls (TA1): a tool_result that says is_error, counted once per call id.
        int failedCalls = 0;
        var failedIds = new HashSet<string>(StringComparer.Ordinal);
        long contextAtLastTurn = 0;
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        var toolNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var filesWritten = new List<string>();
        var fileSet = new HashSet<string>(StringComparer.Ordinal);
        // The time of the last write to each file, so an overlap between sessions can say when each one wrote it (FileOverlap).
        var fileLastWrites = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        // The working folder that the last record naming one gives: the folder the session edits now, which can be a worktree (round 7 item 4).
        string? latestCwd = null;
        // The folder each relative file path was written from: the cwd of the record that carries the write, not the latest one (round 8 item 8).
        var fileFolders = new Dictionary<string, string>(StringComparer.Ordinal);
        var facts = new LineFacts();

        foreach (var line in lines)
        {
            lineNumber++;
            if (!TranscriptJson.TryParse(line, out var document)) continue;

            // Every value the line is counted from is read first. A line that cannot be read is skipped whole, so nothing of it is counted.
            using (document)
            {
                try
                {
                    facts.Read(document.RootElement, wantFirstUserText: firstUserText is null, wantContinuesId: users == 0);
                }
                catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
                {
                    continue;
                }
            }

            records++;

            // Claude Code writes the session's working folder on its records (observed 2026-11-17 on 2.1.326: `cwd` on most lines). A blank value is ignored.
            if (facts.Cwd is not null) latestCwd = facts.Cwd;

            var recordTime = facts.Time;
            if (recordTime is { } parsed)
            {
                lastTimestamp = parsed;
                firstTimestamp ??= parsed;
            }

            if (facts.TurnDuration) turns++;

            // Facts Claude Code writes itself (observed 2026-11-16; the shape is not documented). A blank value is ignored.
            if (facts.AiTitle is not null) aiTitle = facts.AiTitle;
            if (facts.CustomTitle is not null) customTitle = facts.CustomTitle;
            if (facts.LibraryKind == "away_summary" && facts.Recap is not null)
            {
                lastRecap = facts.Recap;
                lastRecapAt = recordTime;
            }
            else if (facts.LibraryKind == "compact_boundary")
            {
                compactions++;
            }

            if (facts.IsUser)
            {
                users++;
                firstUserText ??= facts.FirstUserText;
                // Only the first user message can name the session this one continues (D-L8).
                if (users == 1) continuesSessionId = facts.ContinuesId;
                foreach (var failedId in facts.FailedToolIds)
                {
                    if (failedIds.Add(failedId)) failedCalls++;
                }
            }

            if (!facts.IsAssistant) continue;

            // The model of this record, not the last model seen: usage is attributed to the record's own model.
            lastModel = facts.Model;

            // Count this message once per file: the first record that carries its key.
            bool firstInFile = facts.MessageKey is null || seenInFile.Add(facts.MessageKey);
            if (firstInFile) assistants++;

            foreach (var block in facts.Blocks)
            {
                if (block.Type == "tool_use")
                {
                    // A tool call has its own id, and it is written in just one of the message's records.
                    bool countThisCall = block.Id is not null ? seenInFile.Add("tool:" + block.Id) : firstInFile;
                    if (countThisCall)
                    {
                        tools++;
                        var name = block.Name ?? "unknown";
                        toolNames[name] = toolNames.TryGetValue(name, out var n) ? n + 1 : 1;
                        if (block.FilePath is { } filePath)
                        {
                            if (recordTime is { } writeTime) fileLastWrites[filePath] = writeTime;
                            // latestCwd is this record's own folder when it names one (it is read before the content above), else the last one named.
                            if (latestCwd is not null && !Path.IsPathRooted(filePath)) fileFolders[filePath] = latestCwd;
                            if (fileSet.Add(filePath)) filesWritten.Add(filePath);
                        }
                    }
                }
                if (block.Type == "text" && block.Text is { } text) lastText = text;
            }

            if (facts.Usage is { } usage)
            {
                // A record with no identity cannot be matched to another, so it is counted where it stands.
                var usageKey = facts.MessageKey ?? $"unkeyed:{file}:{lineNumber}";
                usageLedger.Record(usageKey, facts.Model, file, usage);
                // The context a next turn would load is this message's input plus everything read from and written to the cache.
                contextAtLastTurn = usage.Input + usage.CacheRead + usage.CacheCreation;
            }
        }

        return new TranscriptSummary(records, users, assistants, tools, turns, lastText, lastTimestamp, lastModel,
            usageLedger.ByFile(file), firstTimestamp, toolNames, filesWritten, contextAtLastTurn, firstUserText,
            aiTitle, customTitle, lastRecap, lastRecapAt, compactions, continuesSessionId, failedCalls, fileLastWrites, latestCwd, fileFolders);
    }

    /// <summary>
    /// The values of one transcript line that <see cref="Summarize"/> counts from, read before anything is counted (round 12 item A1). It is
    /// filled by <see cref="Read"/>, which reads every value the line can count from; a value read here that the count would not reach is
    /// read but not used, and a value that cannot be read throws and skips the line. The counting is done in <see cref="Summarize"/>, in the
    /// order of the records. One instance serves a whole file.
    /// </summary>
    private sealed class LineFacts
    {
        private readonly List<ToolBlock> _blocks = [];
        private readonly List<string> _failedToolIds = [];

        public string? Cwd { get; private set; }
        public DateTimeOffset? Time { get; private set; }
        public bool TurnDuration { get; private set; }
        public string? LibraryKind { get; private set; }
        public string? AiTitle { get; private set; }
        public string? CustomTitle { get; private set; }
        public string? Recap { get; private set; }
        public bool IsUser { get; private set; }
        public string? FirstUserText { get; private set; }
        public string? ContinuesId { get; private set; }
        public IReadOnlyList<string> FailedToolIds => _failedToolIds;
        public bool IsAssistant { get; private set; }
        public string Model { get; private set; } = "unknown";
        public string? MessageKey { get; private set; }
        public IReadOnlyList<ToolBlock> Blocks => _blocks;
        public TokenUsage? Usage { get; private set; }

        /// <summary>
        /// Reads the line's values. <paramref name="wantFirstUserText"/> and <paramref name="wantContinuesId"/> say whether the count would use the
        /// two user-message texts (the first user message, and the session a resumed one continues); the others are read as the lines are.
        /// </summary>
        public void Read(JsonElement root, bool wantFirstUserText, bool wantContinuesId)
        {
            Cwd = null;
            Time = null;
            TurnDuration = false;
            LibraryKind = null;
            AiTitle = null;
            CustomTitle = null;
            Recap = null;
            IsUser = false;
            FirstUserText = null;
            ContinuesId = null;
            _failedToolIds.Clear();
            IsAssistant = false;
            Model = "unknown";
            MessageKey = null;
            _blocks.Clear();
            Usage = null;

            // Claude Code writes the session's working folder on its records (observed 2026-11-17 on 2.1.326: `cwd` on most lines).
            if (TranscriptJson.TryStr(root, "cwd", out var cwdText) && !string.IsNullOrWhiteSpace(cwdText)) Cwd = cwdText;
            Time = TranscriptJson.Time(root, "timestamp");

            var type = TranscriptJson.Str(root, "type");
            if (type == "system" && TranscriptJson.TryStr(root, "subtype", out var subtype))
            {
                TurnDuration = subtype == "turn_duration";
                LibraryKind = subtype;
                if (subtype == "away_summary" && TranscriptJson.TryStr(root, "content", out var recapText) && !string.IsNullOrWhiteSpace(recapText))
                    Recap = recapText.Trim();
            }
            if (type == "ai-title" && TranscriptJson.TryStr(root, "aiTitle", out var aiTitleText) && !string.IsNullOrWhiteSpace(aiTitleText))
                AiTitle = aiTitleText.Trim();
            if (type == "custom-title" && TranscriptJson.TryStr(root, "customTitle", out var customTitleText) && !string.IsNullOrWhiteSpace(customTitleText))
                CustomTitle = customTitleText.Trim();

            if (type == "user")
            {
                IsUser = true;
                if (wantFirstUserText) FirstUserText = SessionTitle.FromUserRecord(root);
                if (wantContinuesId) ContinuesId = SessionTitle.ContinuesIdOf(root);
                if (root.TryGetProperty("message", out var userMessage) &&
                    userMessage.TryGetProperty("content", out var userContent) && userContent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var resultBlock in userContent.EnumerateArray())
                    {
                        if (TranscriptJson.TryStr(resultBlock, "type", out var resultType) && resultType == "tool_result" &&
                            TranscriptLine.IsFailedResult(resultBlock) &&
                            TranscriptJson.TryStr(resultBlock, "tool_use_id", out var failedId))
                        {
                            _failedToolIds.Add(failedId!);
                        }
                    }
                }
            }

            if (type != "assistant" || !root.TryGetProperty("message", out var message)) return;

            IsAssistant = true;
            Model = TranscriptJson.TryStr(message, "model", out var model) ? model! : "unknown";
            MessageKey = TranscriptJson.TryStr(message, "id", out var messageId) && TranscriptJson.TryStr(root, "requestId", out var requestId)
                ? $"{messageId}:{requestId}"
                : null;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (!TranscriptJson.TryStr(block, "type", out var blockType)) continue;
                    string? id = null, name = null, filePath = null, text = null;
                    if (blockType == "tool_use")
                    {
                        id = TranscriptJson.Str(block, "id");
                        name = TranscriptJson.Str(block, "name");
                        if (TranscriptReader.FileWritingTools.Contains(name ?? "unknown") && block.TryGetProperty("input", out var input))
                            filePath = TranscriptJson.Str(input, "file_path");
                    }
                    else if (blockType == "text")
                    {
                        text = TranscriptJson.Str(block, "text");
                    }
                    _blocks.Add(new ToolBlock(blockType, id, name, filePath, text));
                }
            }

            if (message.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
            {
                Usage = new TokenUsage(
                    TranscriptJson.Long(usageElement, "input_tokens"),
                    TranscriptJson.Long(usageElement, "output_tokens"),
                    TranscriptJson.Long(usageElement, "cache_creation_input_tokens"),
                    TranscriptJson.Long(usageElement, "cache_read_input_tokens"));
            }
        }
    }

    /// <summary>One block of an assistant message as the count uses it: its type, and the id, name, file or text it can carry.</summary>
    private readonly record struct ToolBlock(string? Type, string? Id, string? Name, string? FilePath, string? Text);
}

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
    IReadOnlyDictionary<string, DateTimeOffset>? FileLastWrites = null)
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
    /// Summarises transcript lines. Lines that are not valid JSON are skipped, not fatal.
    /// Usage goes into a <see cref="UsageLedger"/>, once per message (F34, F35, F36). Records and tool calls are counted
    /// for this file. Tool calls are counted once per tool-use id, and assistant messages once per message key.
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

        foreach (var line in lines)
        {
            lineNumber++;
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
                records++;

                DateTimeOffset? recordTime = null;
                if (TryGetString(root, "timestamp", out var timestamp) &&
                    DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    recordTime = parsed;
                    lastTimestamp = parsed;
                    firstTimestamp ??= parsed;
                }

                var type = TryGetString(root, "type", out var t) ? t : null;
                if (type == "system" && TryGetString(root, "subtype", out var subtype) && subtype == "turn_duration")
                    turns++;

                // Facts Claude Code writes itself (observed 2026-11-16; the shape is not documented). A blank value is ignored.
                if (type == "ai-title" && TryGetString(root, "aiTitle", out var aiTitleText) && !string.IsNullOrWhiteSpace(aiTitleText))
                    aiTitle = aiTitleText.Trim();
                if (type == "custom-title" && TryGetString(root, "customTitle", out var customTitleText) && !string.IsNullOrWhiteSpace(customTitleText))
                    customTitle = customTitleText.Trim();
                if (type == "system" && TryGetString(root, "subtype", out var libraryKind))
                {
                    if (libraryKind == "away_summary" && TryGetString(root, "content", out var recapText) && !string.IsNullOrWhiteSpace(recapText))
                    {
                        lastRecap = recapText.Trim();
                        lastRecapAt = recordTime;
                    }
                    else if (libraryKind == "compact_boundary")
                    {
                        compactions++;
                    }
                }
                if (type == "user")
                {
                    users++;
                    firstUserText ??= SessionTitle.FromUserRecord(root);
                    // Only the first user message can name the session this one continues (D-L8).
                    if (users == 1) continuesSessionId = SessionTitle.ContinuesIdOf(root);
                    if (root.TryGetProperty("message", out var userMessage) &&
                        userMessage.TryGetProperty("content", out var userContent) && userContent.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var resultBlock in userContent.EnumerateArray())
                        {
                            if (TryGetString(resultBlock, "type", out var resultType) && resultType == "tool_result" &&
                                TranscriptLine.IsFailedResult(resultBlock) &&
                                TryGetString(resultBlock, "tool_use_id", out var failedId) && failedIds.Add(failedId!))
                            {
                                failedCalls++;
                            }
                        }
                    }
                }
                if (type != "assistant" || !root.TryGetProperty("message", out var message)) continue;

                // The model of this record, not the last model seen: usage is attributed to the record's own model.
                var recordModel = TryGetString(message, "model", out var model) ? model! : "unknown";
                lastModel = recordModel;

                var messageKey = TryGetString(message, "id", out var messageId) && TryGetString(root, "requestId", out var requestId)
                    ? $"{messageId}:{requestId}"
                    : null;
                // Count this message once per file: the first record that carries its key.
                bool firstInFile = messageKey is null || seenInFile.Add(messageKey);
                if (firstInFile) assistants++;

                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (!TryGetString(block, "type", out var blockType)) continue;
                        if (blockType == "tool_use")
                        {
                            // A tool call has its own id, and it is written in just one of the message's records.
                            bool countThisCall = TryGetString(block, "id", out var toolId)
                                ? seenInFile.Add("tool:" + toolId)
                                : firstInFile;
                            if (countThisCall)
                            {
                                tools++;
                                var name = TryGetString(block, "name", out var toolName) ? toolName! : "unknown";
                                toolNames[name] = toolNames.TryGetValue(name, out var n) ? n + 1 : 1;
                                if (FileWritingTools.Contains(name) && block.TryGetProperty("input", out var input) &&
                                    TryGetString(input, "file_path", out var filePath))
                                {
                                    if (recordTime is { } writeTime) fileLastWrites[filePath!] = writeTime;
                                    if (fileSet.Add(filePath!)) filesWritten.Add(filePath!);
                                }
                            }
                        }
                        if (blockType == "text" && TryGetString(block, "text", out var text)) lastText = text;
                    }
                }

                if (message.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
                {
                    var input = ReadLong(usageElement, "input_tokens");
                    var output = ReadLong(usageElement, "output_tokens");
                    var cacheWrite = ReadLong(usageElement, "cache_creation_input_tokens");
                    var cacheRead = ReadLong(usageElement, "cache_read_input_tokens");
                    // A record with no identity cannot be matched to another, so it is counted where it stands.
                    var usageKey = messageKey ?? $"unkeyed:{file}:{lineNumber}";
                    usageLedger.Record(usageKey, recordModel, file, new TokenUsage(input, output, cacheWrite, cacheRead));
                    // The context a next turn would load is this message's input plus everything read from and written to the cache.
                    contextAtLastTurn = input + cacheRead + cacheWrite;
                }
            }
        }

        return new TranscriptSummary(records, users, assistants, tools, turns, lastText, lastTimestamp, lastModel,
            usageLedger.ByFile(file), firstTimestamp, toolNames, filesWritten, contextAtLastTurn, firstUserText,
            aiTitle, customTitle, lastRecap, lastRecapAt, compactions, continuesSessionId, failedCalls, fileLastWrites);
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        value = null;
        return false;
    }

    private static long ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : 0;
}

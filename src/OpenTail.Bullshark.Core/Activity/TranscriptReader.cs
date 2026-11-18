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
        var counter = new SummaryCounter(ledger, fileName, firstLine);
        foreach (var line in lines)
        {
            counter.NextLine();
            if (TranscriptJson.TryParse(line, out var document))
            {
                using (document) counter.Add(document.RootElement);
            }
        }
        return counter.Summary();
    }

    /// <summary>
    /// The same summary as <see cref="Summarize"/>, from the UTF-8 bytes of each line (round 12 item A6). The index reads with this; the bench's
    /// verify step and the tests compare it with the text reader.
    /// </summary>
    internal static TranscriptSummary SummarizeUtf8(IEnumerable<ReadOnlyMemory<byte>> lines, UsageLedger? ledger = null, string? fileName = null)
    {
        var counter = new SummaryCounter(ledger, fileName, 0);
        foreach (var line in lines)
        {
            counter.NextLine();
            if (TranscriptJson.TryParseBytes(line, out var document))
            {
                bool counted;
                using (document) counted = counter.Add(document.RootElement);
                if (counted || !TranscriptJson.HasNonAscii(line.Span)) continue;
            }
            else if (!TranscriptJson.HasNonAscii(line.Span))
            {
                continue;
            }

            // The bytes did not parse, or did not read, and the line has bytes above 0x7F: it is read as the text reader reads it, decoded (an
            // invalid sequence becomes U+FFFD). A line that reads as neither is skipped, as it is there (round 12 item A6).
            if (TranscriptJson.TryParse(TranscriptJson.Decode(line.Span), out var decoded))
            {
                using (decoded) counter.Add(decoded.RootElement);
            }
        }
        return counter.Summary();
    }

    /// <summary>Summarises one file from its UTF-8 bytes: the same result as <see cref="ReadFile"/> (round 12 item A6).</summary>
    internal static TranscriptSummary ReadFileUtf8(string path, UsageLedger? ledger = null) =>
        SummarizeUtf8(TranscriptFile.ReadLineBytes(path), ledger, path);

    /// <summary>
    /// The usage of each assistant record in the lines, with the record's time, read by the same reader as <see cref="Summarize"/> (so the message
    /// key and the usage mean what they mean there). A record whose time or values cannot be read is skipped, as a line is in <see cref="Summarize"/>.
    /// The key of a record with no message id is keyed by its file and line, as the count keys it. Used by the limit history (A4), which counts
    /// the usage of a time window through a <see cref="UsageLedger"/>.
    /// </summary>
    internal static IEnumerable<(DateTimeOffset? Time, string Key, string Model, TokenUsage Usage)> UsageRecords(IEnumerable<string> lines, string fileName)
    {
        var facts = new LineFacts();
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            // A record without the word "usage" cannot carry a usage object, so it is not parsed.
            if (!line.Contains("usage", StringComparison.Ordinal)) continue;
            if (!TranscriptJson.TryParse(line, out var document)) continue;
            using (document)
            {
                try
                {
                    facts.Read(document.RootElement, wantFirstUserText: false, wantContinuesId: false);
                }
                catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
                {
                    continue;
                }
            }
            if (!facts.IsAssistant || facts.Usage is not { } usage) continue;
            yield return (facts.Time, facts.MessageKey ?? $"unkeyed:{fileName}:{lineNumber}", facts.Model, usage);
        }
    }

    /// <summary>
    /// The count of one transcript, record by record. The state that <see cref="Summarize"/> keeps across the lines is kept here, so the text and
    /// the UTF-8 readers count alike.
    /// </summary>
    private sealed class SummaryCounter
    {
        private readonly UsageLedger _ledger;
        private readonly string _file;
        private readonly LineFacts _facts = new();
        private readonly HashSet<string> _failedIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenInFile = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _toolNames = new(StringComparer.Ordinal);
        private readonly List<string> _filesWritten = [];
        private readonly HashSet<string> _fileSet = new(StringComparer.Ordinal);
        // The time of the last write to each file, so an overlap between sessions can say when each one wrote it (FileOverlap).
        private readonly Dictionary<string, DateTimeOffset> _fileLastWrites = new(StringComparer.Ordinal);
        // The folder each relative file path was written from: the cwd of the record that carries the write, not the latest one (round 8 item 8).
        private readonly Dictionary<string, string> _fileFolders = new(StringComparer.Ordinal);
        private int _records, _users, _assistants, _tools, _turns, _compactions, _failedCalls, _lineNumber;
        private string? _lastText, _lastModel, _firstUserText, _aiTitle, _customTitle, _lastRecap, _continuesSessionId;
        // The working folder that the last record naming one gives: the folder the session edits now, which can be a worktree (round 7 item 4).
        private string? _latestCwd;
        private DateTimeOffset? _lastTimestamp, _firstTimestamp, _lastRecapAt;
        private long _contextAtLastTurn;

        public SummaryCounter(UsageLedger? ledger, string? fileName, int firstLine)
        {
            _ledger = ledger ?? new UsageLedger();
            _file = fileName ?? string.Empty;
            _lineNumber = firstLine;
        }

        /// <summary>Moves on to the next line of the file. Every line counts in the line numbers, blank and not-JSON ones too.</summary>
        public void NextLine() => _lineNumber++;

        /// <summary>
        /// Counts one record. A line whose values cannot be read counts nothing (round 12 item A1): the result is false, and nothing has changed.
        /// </summary>
        public bool Add(JsonElement root)
        {
            try
            {
                _facts.Read(root, wantFirstUserText: _firstUserText is null, wantContinuesId: _users == 0);
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return false;
            }

            var facts = _facts;
            _records++;

            // Claude Code writes the session's working folder on its records (observed 2026-11-17 on 2.1.326: `cwd` on most lines). A blank value is ignored.
            if (facts.Cwd is not null) _latestCwd = facts.Cwd;

            var recordTime = facts.Time;
            if (recordTime is { } parsed)
            {
                _lastTimestamp = parsed;
                _firstTimestamp ??= parsed;
            }

            if (facts.TurnDuration) _turns++;

            // Facts Claude Code writes itself (observed 2026-11-16; the shape is not documented). A blank value is ignored.
            if (facts.AiTitle is not null) _aiTitle = facts.AiTitle;
            if (facts.CustomTitle is not null) _customTitle = facts.CustomTitle;
            if (facts.LibraryKind == "away_summary" && facts.Recap is not null)
            {
                _lastRecap = facts.Recap;
                _lastRecapAt = recordTime;
            }
            else if (facts.LibraryKind == "compact_boundary")
            {
                _compactions++;
            }

            if (facts.IsUser)
            {
                _users++;
                _firstUserText ??= facts.FirstUserText;
                // Only the first user message can name the session this one continues (D-L8).
                if (_users == 1) _continuesSessionId = facts.ContinuesId;
                foreach (var failedId in facts.FailedToolIds)
                {
                    if (_failedIds.Add(failedId)) _failedCalls++;
                }
            }

            if (!facts.IsAssistant) return true;

            // The model of this record, not the last model seen: usage is attributed to the record's own model.
            _lastModel = facts.Model;

            // Count this message once per file: the first record that carries its key.
            bool firstInFile = facts.MessageKey is null || _seenInFile.Add(facts.MessageKey);
            if (firstInFile) _assistants++;

            foreach (var block in facts.Blocks)
            {
                if (block.Type == "tool_use")
                {
                    // A tool call has its own id, and it is written in just one of the message's records.
                    bool countThisCall = block.Id is not null ? _seenInFile.Add("tool:" + block.Id) : firstInFile;
                    if (countThisCall)
                    {
                        _tools++;
                        var name = block.Name ?? "unknown";
                        _toolNames[name] = _toolNames.TryGetValue(name, out var n) ? n + 1 : 1;
                        if (block.FilePath is { } filePath)
                        {
                            if (recordTime is { } writeTime) _fileLastWrites[filePath] = writeTime;
                            // latestCwd is this record's own folder when it names one (it is read before the content above), else the last one named.
                            if (_latestCwd is not null && !Path.IsPathRooted(filePath)) _fileFolders[filePath] = _latestCwd;
                            if (_fileSet.Add(filePath)) _filesWritten.Add(filePath);
                        }
                    }
                }
                if (block.Type == "text" && block.Text is { } text) _lastText = text;
            }

            if (facts.Usage is { } usage)
            {
                // A record with no identity cannot be matched to another, so it is counted where it stands.
                var usageKey = facts.MessageKey ?? $"unkeyed:{_file}:{_lineNumber}";
                _ledger.Record(usageKey, facts.Model, _file, usage);
                // The context a next turn would load is this message's input plus everything read from and written to the cache.
                _contextAtLastTurn = usage.Input + usage.CacheRead + usage.CacheCreation;
            }
            return true;
        }

        /// <summary>What has been counted so far.</summary>
        public TranscriptSummary Summary() => new(_records, _users, _assistants, _tools, _turns, _lastText, _lastTimestamp, _lastModel,
            _ledger.ByFile(_file), _firstTimestamp, _toolNames, _filesWritten, _contextAtLastTurn, _firstUserText,
            _aiTitle, _customTitle, _lastRecap, _lastRecapAt, _compactions, _continuesSessionId, _failedCalls, _fileLastWrites, _latestCwd, _fileFolders);
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

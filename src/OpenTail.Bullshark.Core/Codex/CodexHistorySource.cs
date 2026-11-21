using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Codex's history as the neutral summaries (stage C1, C-D1). It reads <c>session_index.jsonl</c> (names) from the home folder, then every
/// rollout under the history root, one line at a time. Rules: a file that cannot be opened or decoded, or holds no JSON object line, is
/// skipped and counted (<see cref="SkippedFiles"/>); a line that is not JSON is skipped and counted (<see cref="SkippedLines"/>); an unknown
/// record type is ignored; a field not found is null, never zero. The list is empty on net10.0 (C-D2): no compressed rollout can be read there.
/// </summary>
public sealed class CodexHistorySource(ProviderPaths paths) : IHistorySource
{
    /// <summary>The longest title taken from a first user message (C-D1).</summary>
    public const int MaxTitleLength = 80;

    /// <summary>The names file, in the home folder beside the sessions.</summary>
    public const string IndexFileName = "session_index.jsonl";

    private static readonly Regex WhiteSpace = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>Files skipped by the last <see cref="ListAsync"/>: one that could not be opened or decoded, or held no JSON object line.</summary>
    public int SkippedFiles { get; private set; }

    /// <summary>Lines skipped by the last <see cref="ListAsync"/>: a line that is not JSON, in a readable rollout or in the index.</summary>
    public int SkippedLines { get; private set; }

    /// <summary>The Codex sessions, one per rollout. Runs on a background thread; honours the token between files.</summary>
    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) => Task.Run(() => List(ct), ct);

    private IReadOnlyList<SessionSummary> List(CancellationToken ct)
    {
        SkippedFiles = 0;
        SkippedLines = 0;
        // C-D2: no rollout is compressed-readable on net10.0, so the history is empty there and nothing is walked.
        if (!RolloutReader.CanDecodeCompressed || !Directory.Exists(paths.HistoryRoot)) return [];

        var names = ReadIndex(ct);
        var sessions = new List<SessionSummary>();
        foreach (var path in RolloutReader.Files(paths.HistoryRoot))
        {
            ct.ThrowIfCancellationRequested();
            var summary = Summarise(path, names, ct);
            if (summary is not null) sessions.Add(summary);
        }
        return sessions;
    }

    /// <summary>
    /// The names by session id (the last non-blank name for an id wins). A missing index is no names. An index that cannot be read is
    /// counted as a skipped file, and the rollouts are still listed without names.
    /// </summary>
    private Dictionary<string, string> ReadIndex(CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var file = Path.Combine(paths.HomeRoot, IndexFileName);
        if (!File.Exists(file)) return names;
        try
        {
            using var reader = new StreamReader(
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Trim().Length == 0) continue;
                using var document = RolloutReader.TryParseObject(line);
                if (document is null)
                {
                    SkippedLines++;
                    continue;
                }
                var id = RolloutReader.StringOf(document.RootElement, "id");
                var name = RolloutReader.StringOf(document.RootElement, "thread_name");
                if (id is not null && name is not null) names[id] = name;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SkippedFiles++;
        }
        return names;
    }

    /// <summary>
    /// One rollout as a summary, or null when the file is skipped. The id is the session's own id when a session record gives one, else the
    /// UUID in the file name. A file with neither has no key, so it is skipped.
    /// </summary>
    private SessionSummary? Summarise(string path, IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        var facts = new RolloutFacts();
        var badLines = 0;
        try
        {
            using var reader = RolloutReader.Open(path);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Trim().Length == 0) continue;
                using var document = RolloutReader.TryParseObject(line);
                if (document is null)
                {
                    badLines++;
                    continue;
                }
                facts.Add(document.RootElement);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SkippedFiles++;
            return null;
        }

        var id = facts.SessionId ?? RolloutReader.UuidFromFileName(path);
        if (facts.Records == 0 || id is null)
        {
            SkippedFiles++;
            return null;
        }
        SkippedLines += badLines;

        // The index name wins; then the first user message, cut to 80 characters; else no title.
        var title = names.TryGetValue(id, out var indexName) ? indexName : Cut(facts.FirstUserText);
        return new SessionSummary(
            new SessionKey(ProviderId.Codex, id),
            title,
            facts.Folder,
            facts.First,
            facts.Last,
            facts.IsSubagent,
            facts.SawResponse ? facts.UserMessages : null,
            facts.SawResponse ? facts.AssistantMessages : null,
            facts.Compactions,
            facts.Model);
    }

    private static string? Cut(string? text) =>
        text is null || text.Length <= MaxTitleLength ? text : text[..MaxTitleLength];

    /// <summary>The text of a user message: its string content, or the text parts of its content list, with the white space made single.</summary>
    private static string? MessageText(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("content", out var content)) return null;
        string text;
        if (content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString() ?? string.Empty;
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (RolloutReader.StringOf(part, "text") is not { } piece) continue;
                if (parts.Length > 0) parts.Append(' ');
                parts.Append(piece);
            }
            text = parts.ToString();
        }
        else
        {
            return null;
        }
        var collapsed = WhiteSpace.Replace(text, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>
    /// What one rollout says, collected record by record. A record type not listed in <see cref="Add"/> is ignored (format drift, R2).
    /// The counts are read from the records the file has; the rules for null are on <see cref="SawResponse"/>.
    /// </summary>
    private sealed class RolloutFacts
    {
        /// <summary>Records with a JSON object line, of any type.</summary>
        public int Records { get; private set; }

        /// <summary>The session id from the first session record that has one, or null.</summary>
        public string? SessionId { get; private set; }

        /// <summary>The working folder: the session record's, else the last turn's.</summary>
        public string? Folder => _sessionFolder ?? _lastTurnFolder;

        public DateTimeOffset? First { get; private set; }

        public DateTimeOffset? Last { get; private set; }

        /// <summary>True when a session record marks a sub-agent; null otherwise (the rollout does not say).</summary>
        public bool? IsSubagent { get; private set; }

        /// <summary>True once a response record has been seen. Until then the message counts are unknown (null), not zero.</summary>
        public bool SawResponse { get; private set; }

        public int UserMessages { get; private set; }

        public int AssistantMessages { get; private set; }

        /// <summary>The number of compaction records. The whole file is read, so zero is a real zero.</summary>
        public int Compactions { get; private set; }

        /// <summary>The model of the last turn that names one.</summary>
        public string? Model { get; private set; }

        /// <summary>The text of the first user message that has text, collapsed (not yet cut).</summary>
        public string? FirstUserText { get; private set; }

        private string? _sessionFolder;
        private string? _lastTurnFolder;

        public void Add(JsonElement record)
        {
            Records++;
            if (RolloutReader.TimestampOf(record) is { } at)
            {
                First ??= at;
                Last = at;
            }

            var payload = record.ValueKind == JsonValueKind.Object && record.TryGetProperty("payload", out var value) ? value : default;
            switch (RolloutReader.StringOf(record, "type"))
            {
                case "session_meta":
                {
                    SessionId ??= RolloutReader.StringOf(payload, "id");
                    _sessionFolder ??= RolloutReader.StringOf(payload, "cwd");
                    if (IsSubagentMarked(payload)) IsSubagent = true;
                    break;
                }
                case "turn_context":
                {
                    _lastTurnFolder = RolloutReader.StringOf(payload, "cwd") ?? _lastTurnFolder;
                    Model = RolloutReader.StringOf(payload, "model") ?? Model;
                    break;
                }
                case "response_item":
                {
                    SawResponse = true;
                    var role = RolloutReader.StringOf(payload, "role");
                    if (role == "user")
                    {
                        UserMessages++;
                        FirstUserText ??= MessageText(payload);
                    }
                    else if (role == "assistant")
                    {
                        AssistantMessages++;
                    }
                    break;
                }
                case "compacted":
                {
                    Compactions++;
                    break;
                }
            }
        }

        /// <summary>A session that names itself a sub-agent: a source or thread source with that value, or a source object with a subagent part.</summary>
        private static bool IsSubagentMarked(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object) return false;
            if (string.Equals(RolloutReader.StringOf(payload, "thread_source"), "subagent", StringComparison.OrdinalIgnoreCase)) return true;
            if (!payload.TryGetProperty("source", out var source)) return false;
            if (source.ValueKind == JsonValueKind.String) return string.Equals(source.GetString(), "subagent", StringComparison.OrdinalIgnoreCase);
            return source.ValueKind == JsonValueKind.Object && source.TryGetProperty("subagent", out _);
        }
    }
}

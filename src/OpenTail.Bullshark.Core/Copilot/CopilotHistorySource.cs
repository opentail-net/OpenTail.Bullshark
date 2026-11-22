using System.Text.Json;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// What Copilot's own files say about one session, beyond the shared <see cref="SessionSummary"/> (G-D19): the turn count, the git fields and the
/// workspace's <c>user_named</c> and <c>created_at</c>. Shown nowhere in v1; kept so the shared record stays unchanged.
/// </summary>
public sealed record CopilotSessionInfo(
    string SessionId,
    int Turns,
    string? GitRoot,
    string? Branch,
    string? Repository,
    bool? UserNamed,
    DateTimeOffset? CreatedAt);

/// <summary>One listed Copilot session: the shared summary and the Copilot-only facts.</summary>
public sealed record CopilotListing(SessionSummary Summary, CopilotSessionInfo Info);

/// <summary>
/// Copilot's history as neutral summaries (G1.2, G-D3, G-D16, G-D19). For each folder under <c>session-state</c> it reads
/// <c>workspace.yaml</c> (name, cwd, updated_at, created_at, user_named, git fields) and <c>events.jsonl</c> (the session id, start time, cwd, the
/// message and turn counts, the model). Rules: a folder with no <c>events.jsonl</c> is an empty session: counted in <see cref="EmptySessions"/>
/// and not listed; a folder whose <c>session.start</c> cannot be read, or whose id is not its folder name, is damaged: counted in
/// <see cref="SkippedFiles"/> and not listed; a line that is not a JSON object, or is over 16 MB, is skipped and counted in
/// <see cref="SkippedLines"/>; an unreadable <c>workspace.yaml</c> is counted in <see cref="SkippedFiles"/> and the session is listed without its
/// values. Nothing is written, and no transcript text is kept beyond the title.
/// </summary>
public sealed class CopilotHistorySource(ProviderPaths paths) : IHistorySource
{
    /// <summary>Folders with an <c>events.jsonl</c> file that could not be read, or whose session record is missing or does not match the folder.</summary>
    public int SkippedFiles { get; private set; }

    /// <summary>Lines of an <c>events.jsonl</c> that were skipped by the last listing: not JSON, or over 16 MB.</summary>
    public int SkippedLines { get; private set; }

    /// <summary>Folders with no <c>events.jsonl</c> in the last listing: they are counted here and not listed.</summary>
    public int EmptySessions { get; private set; }

    /// <summary>The Copilot sessions as neutral summaries (C1). Runs on a background thread; honours the token between folders and lines.</summary>
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) =>
        (await ListDetailedAsync(ct)).Select(listing => listing.Summary).ToList();

    /// <summary>The sessions with their Copilot-only facts (G-D19). The same listing and counts as <see cref="ListAsync"/>.</summary>
    public Task<IReadOnlyList<CopilotListing>> ListDetailedAsync(CancellationToken ct) => Task.Run(() => List(ct), ct);

    private IReadOnlyList<CopilotListing> List(CancellationToken ct)
    {
        SkippedFiles = 0;
        SkippedLines = 0;
        EmptySessions = 0;
        if (!Directory.Exists(paths.HistoryRoot)) return [];

        var listings = new List<CopilotListing>();
        var folders = new DirectoryInfo(paths.HistoryRoot)
            .EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true })
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (Summarise(folder.FullName, ct) is { } listing) listings.Add(listing);
        }
        return listings;
    }

    /// <summary>
    /// One session folder as a listing, or null when the folder is not listed (an empty session, or a damaged one). The folder name is the id; the
    /// <c>session.start</c> id must equal it.
    /// </summary>
    private CopilotListing? Summarise(string folder, CancellationToken ct)
    {
        var name = Path.GetFileName(folder);
        var workspace = CopilotWorkspaceFile.Read(Path.Combine(folder, CopilotWorkspaceFile.FileName));
        if (workspace.State == CopilotWorkspaceState.Unreadable) SkippedFiles++;
        var values = workspace.Values;

        var events = Path.Combine(folder, CopilotFiles.EventsFileName);
        if (!File.Exists(events))
        {
            EmptySessions++;
            return null;
        }

        var facts = new CopilotSessionFacts();
        var badLines = 0;
        try
        {
            using var reader = CopilotFiles.OpenText(events);
            foreach (var line in RolloutReader.Lines(reader, ct))
            {
                // An overlong line is counted as a bad line, and its text was never stored.
                if (line.Overlong)
                {
                    badLines++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line.Text)) continue;
                using var document = RolloutReader.TryParseObject(line.Text);
                if (document is null)
                {
                    badLines++;
                    continue;
                }
                facts.Add(document.RootElement);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SkippedFiles++;
            return null;
        }

        // A folder whose session record is missing, or names another id, cannot be opened by its id: it is damaged.
        if (facts.SessionId is null || !string.Equals(facts.SessionId, name, StringComparison.Ordinal))
        {
            SkippedFiles++;
            return null;
        }
        SkippedLines += badLines;

        // G-D16: the workspace name when it is not blank, else the first user message, collapsed and cut.
        var title = CopilotFiles.Value(values, "name") ?? CopilotFiles.Cut(facts.FirstUserText);
        var summary = new SessionSummary(
            new SessionKey(ProviderId.Copilot, name),
            title,
            CopilotFiles.Value(values, "cwd") ?? facts.Cwd,
            facts.StartTime,
            CopilotFiles.ParseTime(CopilotFiles.Value(values, "updated_at")),
            IsSubagent: null,
            facts.UserMessages,
            facts.AssistantMessages,
            Compactions: null,
            facts.Model);
        var info = new CopilotSessionInfo(
            name,
            facts.Turns,
            CopilotFiles.Value(values, "git_root") ?? facts.GitRoot,
            CopilotFiles.Value(values, "branch") ?? facts.Branch,
            CopilotFiles.Value(values, "repository") ?? facts.Repository,
            UserNamed: ReadFlag(CopilotFiles.Value(values, "user_named")),
            CreatedAt: CopilotFiles.ParseTime(CopilotFiles.Value(values, "created_at")));
        return new CopilotListing(summary, info);
    }

    /// <summary>A workspace flag: true or false in any letter case, else null.</summary>
    private static bool? ReadFlag(string? text) => text is null
        ? null
        : string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? true
        : string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? false
        : null;

    /// <summary>
    /// What one <c>events.jsonl</c> says, collected line by line. Counts are numbers, not null: the whole file is read, so an event that never
    /// occurs is a known zero (G-D16). An event type not listed in <see cref="Add"/> is ignored (format drift, R-C1).
    /// </summary>
    private sealed class CopilotSessionFacts
    {
        /// <summary>The session id from the first <c>session.start</c> that has one, or null.</summary>
        public string? SessionId { get; private set; }

        public DateTimeOffset? StartTime { get; private set; }

        /// <summary>The working folder from the <c>session.start</c> context (the fallback when <c>workspace.yaml</c> has none).</summary>
        public string? Cwd { get; private set; }

        public string? GitRoot { get; private set; }

        public string? Branch { get; private set; }

        public string? Repository { get; private set; }

        public int UserMessages { get; private set; }

        public int AssistantMessages { get; private set; }

        /// <summary>The number of finished turns (<c>assistant.turn_end</c>).</summary>
        public int Turns { get; private set; }

        /// <summary>The first user message that has text, collapsed (not yet cut).</summary>
        public string? FirstUserText { get; private set; }

        private string? _selectedModel;
        private string? _changedModel;

        /// <summary>The model of the last <c>session.model_change</c>, else the model the session started with.</summary>
        public string? Model => _changedModel ?? _selectedModel;

        public void Add(JsonElement record)
        {
            var data = record.ValueKind == JsonValueKind.Object && record.TryGetProperty("data", out var value) ? value : default;
            switch (RolloutReader.StringOf(record, "type"))
            {
                case "session.start":
                {
                    SessionId ??= RolloutReader.StringOf(data, "sessionId");
                    StartTime ??= CopilotFiles.ParseTime(RolloutReader.StringOf(data, "startTime"));
                    var context = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("context", out var found) ? found : default;
                    Cwd ??= RolloutReader.StringOf(context, "cwd");
                    GitRoot ??= RolloutReader.StringOf(context, "gitRoot");
                    Branch ??= RolloutReader.StringOf(context, "branch");
                    Repository ??= RolloutReader.StringOf(context, "repository");
                    _selectedModel ??= RolloutReader.StringOf(data, "selectedModel");
                    break;
                }
                case "session.model_change":
                {
                    _changedModel = RolloutReader.StringOf(data, "newModel") ?? _changedModel;
                    break;
                }
                case "user.message":
                {
                    UserMessages++;
                    FirstUserText ??= CopilotFiles.Collapse(RolloutReader.StringOf(data, "content"));
                    break;
                }
                case "assistant.message":
                {
                    AssistantMessages++;
                    break;
                }
                case "assistant.turn_end":
                {
                    Turns++;
                    break;
                }
            }
        }
    }
}

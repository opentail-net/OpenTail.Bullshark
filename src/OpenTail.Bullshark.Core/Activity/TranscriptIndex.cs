namespace OpenTail.Bullshark.Core.Activity;

/// <summary>One session's digest, built locally from its transcript. No model is involved (F17, the free features).</summary>
public sealed record SessionDigest(
    string SessionId,
    string FilePath,
    string ProjectFolder,
    bool IsSubagent,
    DateTimeOffset? FirstActivity,
    DateTimeOffset? LastActivity,
    int Turns,
    int UserMessages,
    int AssistantMessages,
    int ToolCalls,
    IReadOnlyDictionary<string, int> ToolNames,
    IReadOnlyList<string> FilesWritten,
    string? LastAssistantText,
    string? LastModel,
    long ContextTokensAtLastTurn,
    TokenUsage Usage,
    string? FirstUserText = null,
    string? AiTitle = null,
    string? CustomTitle = null,
    string? LastRecap = null,
    DateTimeOffset? LastRecapAt = null,
    int Compactions = 0,
    string? ContinuesSessionId = null,
    int FailedToolCalls = 0)
{
    /// <summary>
    /// The title (D-L1; the owner's own name comes in L3). Claude Code's title when the transcript has one, else the first
    /// message, else "Session" and the short id. The custom title is a role name, so it is not used here.
    /// </summary>
    public string Title =>
        AiTitle is { Length: > 0 } aiTitle ? aiTitle
        : FirstUserText is { Length: > 0 } text ? text
        : $"Session {SessionId[..Math.Min(8, SessionId.Length)]}";

    /// <summary>Time between the first and the last record. Not the time spent working: the gaps are included.</summary>
    public TimeSpan? Span => FirstActivity is { } first && LastActivity is { } last ? last - first : null;
}

/// <summary>All counted usage across every transcript file, once per message (the ccusage rule, F35).</summary>
public sealed record IndexTotals(TokenUsage Total, IReadOnlyDictionary<string, TokenUsage> ByModel, int CountedMessages);

/// <summary>A transcript the index could not read, so it is left out. <see cref="Reason"/> is the error's message.</summary>
public sealed record SkippedTranscript(string FilePath, string Reason);

/// <summary>
/// The index over every transcript under the projects folder. Files are read as streams, one at a time, and only the
/// digests are kept. Files are read oldest first, so a message copied into a later (for example resumed) file is
/// counted against the file it first appeared in.
/// </summary>
public sealed class TranscriptIndex
{
    private TranscriptIndex(IReadOnlyList<SessionDigest> sessions, IndexTotals totals, IReadOnlyList<SkippedTranscript> skipped)
    {
        Sessions = sessions;
        Totals = totals;
        Skipped = skipped;
    }

    public IReadOnlyList<SessionDigest> Sessions { get; }

    public IndexTotals Totals { get; }

    /// <summary>
    /// The transcripts that could not be read, and why. Claude can delete or lock a transcript after it was listed; such a file
    /// is left out and the rest are still read. Empty when every file was read.
    /// </summary>
    public IReadOnlyList<SkippedTranscript> Skipped { get; }

    /// <summary>Builds the index. A missing folder gives an empty index.</summary>
    public static TranscriptIndex Build(string projectsRoot) => Build(projectsRoot, TranscriptReader.ReadFile);

    /// <summary>
    /// Builds the index, reading each transcript with <paramref name="readFile"/> (the default is <see cref="TranscriptReader.ReadFile"/>).
    /// A file that throws an <see cref="IOException"/> or an <see cref="UnauthorizedAccessException"/> is skipped and listed in
    /// <see cref="Skipped"/>; the other files are still read. A missing folder gives an empty index.
    /// </summary>
    public static TranscriptIndex Build(string projectsRoot, Func<string, UsageLedger, TranscriptSummary> readFile)
    {
        var ledger = new UsageLedger();
        var digests = new List<SessionDigest>();
        var skipped = new List<SkippedTranscript>();
        if (!Directory.Exists(projectsRoot))
            return new TranscriptIndex(digests, new IndexTotals(TokenUsage.Zero, new Dictionary<string, TokenUsage>(), 0), skipped);

        var files = new DirectoryInfo(projectsRoot)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            TranscriptSummary summary;
            try
            {
                summary = readFile(file.FullName, ledger);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Includes FileNotFoundException: a transcript removed after it was listed. Messages this file had already
                // counted stay in the ledger (a known limit, see the plan's decisions).
                skipped.Add(new SkippedTranscript(file.FullName, ex.Message));
                continue;
            }

            var directory = file.DirectoryName ?? string.Empty;
            digests.Add(new SessionDigest(
                SessionId: Path.GetFileNameWithoutExtension(file.Name),
                FilePath: file.FullName,
                ProjectFolder: Path.GetFileName(directory),
                IsSubagent: directory.Split(Path.DirectorySeparatorChar).Contains("subagents", StringComparer.OrdinalIgnoreCase),
                FirstActivity: summary.FirstTimestamp,
                LastActivity: summary.LastTimestamp,
                Turns: summary.CompletedTurns,
                UserMessages: summary.UserMessages,
                AssistantMessages: summary.AssistantMessages,
                ToolCalls: summary.ToolCalls,
                ToolNames: summary.ToolNames ?? new Dictionary<string, int>(),
                FilesWritten: summary.FilesWritten ?? [],
                LastAssistantText: summary.LastAssistantText,
                FirstUserText: summary.FirstUserText,
                AiTitle: summary.AiTitle,
                CustomTitle: summary.CustomTitle,
                LastRecap: summary.LastRecap,
                LastRecapAt: summary.LastRecapAt,
                Compactions: summary.Compactions,
                ContinuesSessionId: summary.ContinuesSessionId,
                LastModel: summary.LastModel,
                ContextTokensAtLastTurn: summary.ContextTokensAtLastTurn,
                Usage: summary.TotalUsage,
                FailedToolCalls: summary.FailedToolCalls));
        }

        return new TranscriptIndex(digests, new IndexTotals(ledger.Total, ledger.ByModel(), ledger.Count), skipped);
    }
}

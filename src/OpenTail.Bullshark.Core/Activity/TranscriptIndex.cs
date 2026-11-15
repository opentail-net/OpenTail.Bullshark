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
    TokenUsage Usage)
{
    /// <summary>Time between the first and the last record. Not the time spent working: the gaps are included.</summary>
    public TimeSpan? Span => FirstActivity is { } first && LastActivity is { } last ? last - first : null;
}

/// <summary>All counted usage across every transcript file, once per message (the ccusage rule, F35).</summary>
public sealed record IndexTotals(TokenUsage Total, IReadOnlyDictionary<string, TokenUsage> ByModel, int CountedMessages);

/// <summary>
/// The index over every transcript under the projects folder. Files are read as streams, one at a time, and only the
/// digests are kept. Files are read oldest first, so a message copied into a later (for example resumed) file is
/// counted against the file it first appeared in.
/// </summary>
public sealed class TranscriptIndex
{
    private TranscriptIndex(IReadOnlyList<SessionDigest> sessions, IndexTotals totals)
    {
        Sessions = sessions;
        Totals = totals;
    }

    public IReadOnlyList<SessionDigest> Sessions { get; }

    public IndexTotals Totals { get; }

    /// <summary>Builds the index. A missing folder gives an empty index.</summary>
    public static TranscriptIndex Build(string projectsRoot)
    {
        var ledger = new UsageLedger();
        var digests = new List<SessionDigest>();
        if (!Directory.Exists(projectsRoot))
            return new TranscriptIndex(digests, new IndexTotals(TokenUsage.Zero, new Dictionary<string, TokenUsage>(), 0));

        var files = new DirectoryInfo(projectsRoot)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            var summary = TranscriptReader.ReadFile(file.FullName, ledger);
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
                LastModel: summary.LastModel,
                ContextTokensAtLastTurn: summary.ContextTokensAtLastTurn,
                Usage: summary.TotalUsage));
        }

        return new TranscriptIndex(digests, new IndexTotals(ledger.Total, ledger.ByModel(), ledger.Count));
    }
}

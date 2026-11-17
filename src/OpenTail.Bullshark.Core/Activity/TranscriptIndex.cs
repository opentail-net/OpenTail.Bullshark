using System.Runtime.ExceptionServices;

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
/// The index over every transcript under the projects folder. Only the digests are kept. Files are read oldest first, so a message
/// copied into a later (for example resumed) file is counted against the file it first appeared in. The default build reads the files
/// on several threads, each into a ledger of its own, and combines them in that same order (round 10 item 2).
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

    /// <summary>
    /// Builds the index. A missing folder gives an empty index. The result is the same as
    /// <c>Build(projectsRoot, TranscriptReader.ReadFile)</c>, which reads the files one after another; this one reads them on several
    /// threads, each with a ledger of its own, and combines the ledgers in the order of that read (round 10 item 2).
    /// </summary>
    public static TranscriptIndex Build(string projectsRoot)
    {
        if (!Directory.Exists(projectsRoot)) return Empty();

        var files = ListFiles(projectsRoot);
        var reads = new FileRead[files.Count];
        Parallel.For(0, files.Count, i => reads[i] = ReadOne(files[i]));

        var ledger = new UsageLedger();
        var digests = new List<SessionDigest>();
        var skipped = new List<SkippedTranscript>();
        foreach (var read in reads)
        {
            // The messages this file is the first to count: its usage is theirs, as it is when the files are read one after another.
            var first = read.Ledger.Pairs.Where(pair => !ledger.Contains(pair.Key)).Select(pair => pair.Value).ToList();
            foreach (var (key, entry) in read.Ledger.Pairs) ledger.Record(key, entry.Model, entry.FirstFile, entry.Usage);

            if (read.Error is { } error)
            {
                // The same rule as the one-after-another read: a file that cannot be read is left out, the messages it had counted stay
                // counted, and any other failure stops the build, as it does there.
                if (error is not (IOException or UnauthorizedAccessException)) ExceptionDispatchInfo.Capture(error).Throw();
                skipped.Add(new SkippedTranscript(read.Info.FullName, error.Message));
                continue;
            }

            digests.Add(DigestOf(read.Info, read.Summary! with { UsageByModel = UsageLedger.ByModelOf(first) }));
        }

        return new TranscriptIndex(digests, new IndexTotals(ledger.Total, ledger.ByModel(), ledger.Count), skipped);
    }

    /// <summary>
    /// Builds the index, reading each transcript with <paramref name="readFile"/> (the default is <see cref="TranscriptReader.ReadFile"/>).
    /// The files are read one after another, with one ledger shared by all of them. A file that throws an <see cref="IOException"/> or an
    /// <see cref="UnauthorizedAccessException"/> is skipped and listed in <see cref="Skipped"/>; the other files are still read. A missing
    /// folder gives an empty index.
    /// </summary>
    public static TranscriptIndex Build(string projectsRoot, Func<string, UsageLedger, TranscriptSummary> readFile)
    {
        var ledger = new UsageLedger();
        var digests = new List<SessionDigest>();
        var skipped = new List<SkippedTranscript>();
        if (!Directory.Exists(projectsRoot)) return Empty();

        foreach (var file in ListFiles(projectsRoot))
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

            digests.Add(DigestOf(file, summary));
        }

        return new TranscriptIndex(digests, new IndexTotals(ledger.Total, ledger.ByModel(), ledger.Count), skipped);
    }

    /// <summary>One transcript as it was read on its own: its file, its ledger, and either its summary or the error it failed with.</summary>
    private sealed record FileRead(FileInfo Info, UsageLedger Ledger, TranscriptSummary? Summary, Exception? Error);

    private static FileRead ReadOne(FileInfo file)
    {
        var ledger = new UsageLedger();
        try
        {
            return new FileRead(file, ledger, TranscriptReader.ReadFile(file.FullName, ledger), null);
        }
        catch (Exception ex)
        {
            return new FileRead(file, ledger, null, ex);
        }
    }

    /// <summary>The transcripts under the folder, oldest first (by creation time, then by name).</summary>
    private static List<FileInfo> ListFiles(string projectsRoot) =>
        new DirectoryInfo(projectsRoot)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TranscriptIndex Empty() =>
        new(new List<SessionDigest>(), new IndexTotals(TokenUsage.Zero, new Dictionary<string, TokenUsage>(), 0), new List<SkippedTranscript>());

    private static SessionDigest DigestOf(FileInfo file, TranscriptSummary summary)
    {
        var directory = file.DirectoryName ?? string.Empty;
        return new SessionDigest(
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
            FailedToolCalls: summary.FailedToolCalls);
    }
}

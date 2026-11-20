using System.Runtime.ExceptionServices;
using OpenTail.Bullshark.Core.Setup;

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
    int FailedToolCalls = 0,
    /// <summary>
    /// The working folder the transcript names last (its records' cwd), or null when none is named. Claude's folder name is lossy, so the rail
    /// and the Storage page name a folder by this (FolderDisplay). The folder's key is still the group's identity.
    /// </summary>
    string? Cwd = null,
    /// <summary>
    /// The usage-limit stop at the end of the transcript, as of the read that made the digest (limit wake), or null. A digest is as fresh as its
    /// read: the history is read on open and on refresh, so a live session's stop is read again by the supervisor pass, not from here.
    /// </summary>
    LimitStop? LimitStop = null)
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
        Parallel.For(0, files.Count, ReadOptions(), i => reads[i] = ReadOne(files[i]));
        return Combine(reads);
    }

    /// <summary>
    /// The options of the parallel reads, shared by the index and the search: one thread fewer than there are processors, so the window keeps one
    /// (round 11 item 4), and never fewer than one. The token is passed on, so a cancelled read stops taking new work.
    /// </summary>
    internal static ParallelOptions ReadOptions(CancellationToken cancellationToken = default) => new()
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
        CancellationToken = cancellationToken,
    };

    /// <summary>
    /// The default build, with what each transcript was read as kept in a cache file between builds (round 10 item 2). A transcript with the
    /// same length and write time as its entry in the cache is not read: its entry is taken as it was read. The others are read, and the cache
    /// is written again when any was read or one has gone. The result is the same as <see cref="Build(string)"/>. A cache that cannot be read
    /// or written is left out, and every transcript is read; <paramref name="log"/> is told each such failure in one line (null says nothing).
    /// </summary>
    public static TranscriptIndex Build(string projectsRoot, string cachePath, Action<string>? log = null)
    {
        if (!Directory.Exists(projectsRoot)) return Empty();

        var files = ListFiles(projectsRoot);
        var cached = IndexCache.Load(cachePath, log);
        var now = DateTime.UtcNow;
        var reads = new FileRead[files.Count];
        var readNow = new bool[files.Count];
        Parallel.For(0, files.Count, ReadOptions(), i =>
        {
            var file = files[i];
            var read = cached is not null && cached.TryGetValue(file.FullName, out var entry) ? FromCache(file, entry, now) : null;
            if (read is null)
            {
                reads[i] = ReadOne(file);
                readNow[i] = true;
            }
            else
            {
                reads[i] = read;
            }
        });

        var index = Combine(reads);
        var entries = new List<IndexCache.Entry>();
        foreach (var read in reads)
        {
            if (read.Error is null && read.Summary is { } summary)
                entries.Add(IndexCache.EntryOf(read.Info.FullName, read.Length, read.Written, summary, read.Ledger.Pairs));
        }
        if (cached is null || cached.Count != entries.Count || readNow.Any(read => read)) IndexCache.Save(cachePath, entries, log);
        return index;
    }

    /// <summary>
    /// The read of a transcript from its cache entry, or null when the entry is not for the file as it is now, or the file was written within
    /// <see cref="LiveWindow"/> of <paramref name="utcNow"/>. A live session writes its transcript often, and the metadata the listing gave can
    /// lag the file, so such a file is read again (round 11 item 1).
    /// </summary>
    private static FileRead? FromCache(FileInfo file, IndexCache.Entry entry, DateTime utcNow)
    {
        (long Length, DateTime Written) fresh;
        try
        {
            fresh = StatNow(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not readable now: the read reports it, as it does for any file that cannot be read.
            return null;
        }
        if (entry.Length != fresh.Length || entry.Written != fresh.Written.Ticks) return null;
        if (fresh.Written > utcNow - LiveWindow) return null;
        try
        {
            var (summary, ledger) = IndexCache.Restore(entry);
            return new FileRead(file, ledger, summary, null, fresh.Length, fresh.Written);
        }
        catch (Exception)
        {
            // A damaged entry is no entry: the file is read instead.
            return null;
        }
    }

    /// <summary>A transcript written this recently may still be a live session, so the cache is not used for it.</summary>
    private static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(2);

    /// <summary>The length and write time the disk gives for the file now. A fresh stat, not the copy the listing took (round 11 item 1).</summary>
    private static (long Length, DateTime Written) StatNow(FileInfo file)
    {
        file.Refresh();
        return (file.Length, file.LastWriteTimeUtc);
    }

    /// <summary>
    /// Combines the reads, in the order of the listing, into the index. Each read's messages are counted only where no earlier file counted
    /// them, as the one-after-another build does.
    /// </summary>
    private static TranscriptIndex Combine(FileRead[] reads)
    {
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
                // The same rule as the one-after-another read: a file that fails is left out and listed, the messages it had counted stay
                // counted, and only a cancellation stops the build (round 12 item A2).
                if (error is OperationCanceledException) ExceptionDispatchInfo.Capture(error).Throw();
                skipped.Add(new SkippedTranscript(read.Info.FullName, error.Message));
                continue;
            }

            digests.Add(DigestOf(read.Info, read.Summary! with { UsageByModel = UsageLedger.ByModelOf(first) }));
        }

        return new TranscriptIndex(digests, new IndexTotals(ledger.Total, ledger.ByModel(), ledger.Count), skipped);
    }

    /// <summary>
    /// Builds the index, reading each transcript with <paramref name="readFile"/> (the default is <see cref="TranscriptReader.ReadFile"/>).
    /// The files are read one after another, with one ledger shared by all of them. A file whose read throws is skipped and listed in
    /// <see cref="Skipped"/>, and the other files are still read: any exception except a cancellation (round 12 item A2). That includes
    /// <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/>, and a transcript the reader cannot decode. A missing folder
    /// gives an empty index.
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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

    /// <summary>
    /// One transcript as it was read on its own: its file, its ledger, and either its summary or the error it failed with. Length and Written
    /// are the fresh stat taken before the read (the cache key), so a file that grows during the read is read again next time.
    /// </summary>
    private sealed record FileRead(FileInfo Info, UsageLedger Ledger, TranscriptSummary? Summary, Exception? Error, long Length, DateTime Written);

    private static FileRead ReadOne(FileInfo file)
    {
        var ledger = new UsageLedger();
        try
        {
            var (length, written) = StatNow(file);
            // The index reads the bytes of each line: the same summary as the text reader, without decoding every line (round 12 item A6).
            return new FileRead(file, ledger, TranscriptReader.ReadFileUtf8(file.FullName, ledger), null, length, written);
        }
        catch (Exception ex)
        {
            return new FileRead(file, ledger, null, ex, 0, DateTime.MinValue);
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
            FailedToolCalls: summary.FailedToolCalls,
            Cwd: summary.LatestCwd,
            LimitStop: summary.LimitStop);
    }
}

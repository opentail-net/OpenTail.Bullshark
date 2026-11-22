using System.IO;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Files that two sessions of one run both wrote (gems item 1). The clashes are read on a pool thread during each pass, from the
/// transcripts of the run's sessions (only the ones that changed are read again). The rail marks the sessions, and the Changes
/// tab lists the files; the "Needs you" group gets the notices through <see cref="AttentionList"/>.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The clashes found by the last pass. Written on the UI thread only.</summary>
    private IReadOnlyList<FileClash> _clashes = [];

    /// <summary>
    /// The writes of each transcript, kept between passes. It is not thread-safe, and since round 12 three callers read it on pool threads
    /// (the pass, a merge check and the Changes tab), so every read goes through <see cref="ReadWrites"/>, under <see cref="_writesLock"/>.
    /// </summary>
    private readonly SessionWritesCache _writesCache = new();

    /// <summary>Guards <see cref="_writesCache"/>. Taken on pool threads only; the UI thread never reads the cache.</summary>
    private readonly object _writesLock = new();

    /// <summary>The writes of one transcript through the cache, under <see cref="_writesLock"/> (round 12 B3). Throws what the read throws.</summary>
    private SessionWrites ReadWrites(string sessionId, string path)
    {
        lock (_writesLock) return _writesCache.Read(sessionId, path);
    }

    /// <summary>
    /// The files and folder a session's transcript says it wrote, for the Changes tab and the merge check (round 12 B3). The path is the one
    /// the pass found, else the transcript is looked for. The notes are <see cref="TranscriptWrites.Facts"/>'s: a missing transcript and one
    /// that cannot be read. The search goes through the miss memory for <paramref name="missMemory"/> (R10), which is thread-safe. Runs on a pool thread.
    /// </summary>
    private TranscriptWrites.TranscriptFacts TranscriptFactsOf(string sessionId, string? knownPath, string projectsRoot, TimeSpan missMemory)
    {
        try
        {
            var path = knownPath ?? FindTranscriptRemembered(_misses, projectsRoot, sessionId, missMemory, DateTimeOffset.UtcNow);
            if (path is null) return new TranscriptWrites.TranscriptFacts(null, "No transcript for this session yet.", null);
            var writes = ReadWrites(sessionId, path);
            return new TranscriptWrites.TranscriptFacts(writes.Files, null, writes.Folder);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            return new TranscriptWrites.TranscriptFacts(null, $"The transcript cannot be read: {ex.Message}", null);
        }
    }

    /// <summary>
    /// The writes of each run's sessions, for the runs that read their transcripts. A run that can have neither a clash nor a limit does
    /// not read them (round 5 item 9). Runs on a pool thread, through the cache, and touches no window object. A transcript that cannot be
    /// read is skipped, and the rest are read.
    /// </summary>
    private List<(RunRecord Run, List<SessionWrites> Writes)> ReadRunWrites(
        IEnumerable<RunRecord> runs,
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, string> paths)
    {
        var result = new List<(RunRecord Run, List<SessionWrites> Writes)>();
        foreach (var run in runs)
        {
            if (!FileOverlap.NeedsTranscripts(run)) continue;
            var writes = new List<SessionWrites>();
            foreach (var spec in run.Specs)
            {
                if (!records.TryGetValue(spec.Name, out var record) || record.SessionId.Length == 0) continue;
                if (!paths.TryGetValue(record.SessionId, out var path)) continue;
                try
                {
                    writes.Add(ReadWrites(record.SessionId, path));
                }
                // Any failure is this session's alone (round 12 B1): the other sessions of the run are still read.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Write($"overlap: transcript not read: {ex.GetType().Name}");
                }
            }
            result.Add((run, writes));
        }
        return result;
    }

    /// <summary>
    /// The budgets of the runs' sessions, from the records and transcripts a pass has read (round 6 item 1). The pass reads them before it
    /// runs, so a run at its limit gets no start or resume on the pass that starts after the window opens. Runs on a pool thread.
    /// </summary>
    private IReadOnlyDictionary<string, RunBudget> BudgetsOf(
        IReadOnlyList<RunRecord> runs,
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, string> paths,
        bool clampBackwards) =>
        RunBudget.ForRuns(runs, records, ReadRunWrites(runs, records, paths).SelectMany(read => read.Writes), DateTimeOffset.UtcNow, clampBackwards);

    /// <summary>
    /// The clashes and the budgets of every run, after a pass, for the sessions in <paramref name="records"/>. The budgets are kept for the
    /// next pass and the clashes are drawn. The runs it read, and the problem reading them, go back too: the rail and the merge checks take
    /// the runs from this read (round 12 B4). Runs on a pool thread: it reads runs.json and the transcripts through the cache, and touches no
    /// window object.
    /// </summary>
    private (IReadOnlyList<FileClash> Clashes, IReadOnlyDictionary<string, RunBudget> Budgets, IReadOnlyList<RunRecord> Runs, string? RunsProblem)
        ReadClashes(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyList<AgentInfo> agents,
        bool clampBackwards)
    {
        var (runs, runsProblem) = LoadRuns(_runStore);
        // Only the live sessions can clash: a finished session's shared file no longer needs the owner (round 5 item 7).
        var live = FileOverlap.LiveRecords(records, agents);
        var read = ReadRunWrites(runs, records, paths);
        var clashes = read.SelectMany(item => FileOverlap.Find(item.Run, live, item.Writes)).ToList();
        var budgets = RunBudget.ForRuns(runs, records, read.SelectMany(item => item.Writes), DateTimeOffset.UtcNow, clampBackwards);
        return (clashes, budgets, runs, runsProblem);
    }

    /// <summary>The supervisor runner, which the limits are given to after each pass (additions item 4).</summary>
    private SupervisorRunner _runner = null!;

    /// <summary>The session ids that are in a clash, for the rail mark.</summary>
    private bool IsShared(string sessionId) =>
        _clashes.Any(clash => clash.Sessions.Any(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The lines for the Changes tab: one per clash the session is in, naming the file and the other sessions.</summary>
    private IReadOnlyList<string> SharedLinesFor(SessionRecord? record)
    {
        if (record is null) return [];
        return _clashes
            .Where(clash => clash.Sessions.Any(s => string.Equals(s.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase)))
            .Select(clash => FileOverlap.Detail(clash, record.Name))
            .ToList();
    }
}

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
    /// The writes of each transcript, kept between passes. Used only by the pass, which runs one at a time (the _passRunning guard),
    /// so it needs no lock.
    /// </summary>
    private readonly SessionWritesCache _writesCache = new();

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
                    writes.Add(_writesCache.Read(record.SessionId, path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write($"overlap: transcript of {spec.Name} not read: {ex.Message}");
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
        IReadOnlyDictionary<string, string> paths) =>
        RunBudget.ForRuns(runs, records, ReadRunWrites(runs, records, paths).SelectMany(read => read.Writes), DateTimeOffset.UtcNow);

    /// <summary>
    /// The clashes and the budgets of every run, after a pass, for the sessions in <paramref name="records"/>. The budgets are kept for the
    /// next pass and the clashes are drawn. Runs on a pool thread: it reads runs.json and the transcripts through the cache, and touches no
    /// window object.
    /// </summary>
    private (IReadOnlyList<FileClash> Clashes, IReadOnlyDictionary<string, RunBudget> Budgets) ReadClashes(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyList<AgentInfo> agents)
    {
        var (runs, _) = LoadRuns(_runStore);
        // Only the live sessions can clash: a finished session's shared file no longer needs the owner (round 5 item 7).
        var live = FileOverlap.LiveRecords(records, agents);
        var read = ReadRunWrites(runs, records, paths);
        var clashes = read.SelectMany(item => FileOverlap.Find(item.Run, live, item.Writes)).ToList();
        var budgets = RunBudget.ForRuns(runs, records, read.SelectMany(item => item.Writes), DateTimeOffset.UtcNow);
        return (clashes, budgets);
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

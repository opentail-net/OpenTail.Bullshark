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
    /// The clashes of every run, for the sessions in <paramref name="records"/>. Runs on a pool thread: it reads runs.json and the
    /// transcripts through the cache, and touches no window object. A transcript that cannot be read is skipped, and the rest are read.
    /// </summary>
    private (IReadOnlyList<FileClash> Clashes, IReadOnlyDictionary<string, RunBudget> Budgets) ReadClashes(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyList<AgentInfo> agents)
    {
        var (runs, _) = LoadRuns(_runStore);
        var clashes = new List<FileClash>();
        var budgets = new Dictionary<string, RunBudget>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        // Only the live sessions can clash: a finished session's shared file no longer needs the owner (round 5 item 7).
        var live = FileOverlap.LiveRecords(records, agents);
        foreach (var run in runs)
        {
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
            clashes.AddRange(FileOverlap.Find(run, live, writes));

            // The run's limits, from the same transcripts (additions item 4). The runner applies a budget from the next pass.
            if (RunBudget.For(run.Limits, RunSpend.Of(run, records, writes, now)) is { } budget)
                foreach (var spec in run.Specs) budgets[spec.Name] = budget;
        }
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

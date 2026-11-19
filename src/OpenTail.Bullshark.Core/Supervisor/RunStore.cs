using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One run started from setup (D-S12): the pattern, the folder, the permission mode, and the full spec list as it was
/// launched. Patterns are edited for the next launch only (D-S6), so the run keeps the specs it was created with.
/// <see cref="StartAt"/> (gems item 5) holds a run back until that time: it has no sessions in a pass until then. Null, the
/// default, means the run starts at once. Files from before the field have no such value, so they load as "at once".
/// <see cref="FirstStartedAt"/> and <see cref="ClosedOutputTokens"/> (round 9 item 1) keep what a closed session leaves behind, so the
/// run's clock and spend do not drop when its earliest session is closed. Files from before the fields load as null and 0.
/// </summary>
public sealed record RunRecord(
    string RunId,
    string PatternKey,
    string WorkingDirectory,
    string PermissionMode,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SessionSpec> Specs,
    DateTimeOffset? StartAt = null,
    RunLimits? Limits = null,
    /// <summary>The earliest start of the run's sessions that were closed, or null when none was closed (round 9 item 1).</summary>
    DateTimeOffset? FirstStartedAt = null,
    /// <summary>The output tokens the run's closed sessions wrote, read from their transcripts when they were closed (round 9 item 1).</summary>
    long ClosedOutputTokens = 0);

/// <summary>Reads and writes runs.json. Writes go to a temporary file first, like the state file.</summary>
public sealed class RunStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public RunStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    /// <summary>
    /// All runs in the file. A missing file gives an empty list. An unreadable file throws <see cref="StoreUnreadableException"/>.
    /// </summary>
    public IReadOnlyList<RunRecord> Load() => Load(FileLock.DefaultWait);

    /// <summary>The same read, waiting at most <paramref name="wait"/> for the lock (reliability batch R7). A lock not free in time throws <see cref="TimeoutException"/>.</summary>
    public IReadOnlyList<RunRecord> Load(TimeSpan wait)
    {
        using var _ = FileLock.Hold(_path, wait);
        return Read();
    }

    /// <summary>Applies a change under the file lock. The file is read again inside the lock first.</summary>
    public void Update(Func<IReadOnlyList<RunRecord>, IReadOnlyList<RunRecord>> change)
    {
        using var _ = FileLock.Hold(_path);
        Write(change(Read()));
    }

    /// <summary>Replaces every run. Callers that add or remove one run use <see cref="Update"/>.</summary>
    public void Save(IReadOnlyList<RunRecord> runs) => Update(_ => runs);

    /// <summary>
    /// The file's runs. The file is opened directly, not checked with File.Exists first: that check is false for a file in a folder the
    /// user cannot open, which would read as "no runs" and let the next save replace the file.
    /// </summary>
    private List<RunRecord> Read()
    {
        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        try
        {
            return JsonSerializer.Deserialize<List<RunRecord>>(text, Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }
    }

    private void Write(IReadOnlyList<RunRecord> runs) =>
        FileLock.WriteAtomic(_path, JsonSerializer.Serialize(runs.OrderBy(r => r.CreatedAt).ToList(), Options));

    /// <summary>The newest run, which the window opens on (D-S8), or null when there is none.</summary>
    public static RunRecord? Latest(IEnumerable<RunRecord> runs) =>
        runs.OrderByDescending(r => r.CreatedAt).FirstOrDefault();

    /// <summary>
    /// The runs with one session taken out: its spec leaves the run it belongs to, and a run left with no specs is dropped.
    /// Closing a session for good needs this, or the supervisor would start the session again on its next pass.
    /// </summary>
    /// <remarks>
    /// The session's own start and output tokens are kept on the run it leaves (round 9 item 1): <see cref="RunRecord.FirstStartedAt"/> takes
    /// the earlier of its value and <paramref name="startedAt"/>, and <see cref="RunRecord.ClosedOutputTokens"/> grows by
    /// <paramref name="outputTokens"/>. The caller reads both before the close. A run with no session left is dropped, and its budget with it.
    /// </remarks>
    public static IReadOnlyList<RunRecord> WithoutSession(
        IReadOnlyList<RunRecord> runs,
        string sessionName,
        DateTimeOffset? startedAt = null,
        long outputTokens = 0)
    {
        var kept = new List<RunRecord>();
        foreach (var run in runs)
        {
            var specs = run.Specs.Where(spec => spec.Name != sessionName).ToList();
            if (specs.Count == run.Specs.Count)
            {
                kept.Add(run);
                continue;
            }
            if (specs.Count == 0) continue;
            kept.Add(run with
            {
                Specs = specs,
                FirstStartedAt = Earlier(run.FirstStartedAt, startedAt),
                ClosedOutputTokens = run.ClosedOutputTokens + Math.Max(0, outputTokens),
            });
        }
        return kept;
    }

    /// <summary>
    /// Puts one session back after its close failed (reliability batch R6). Only that session changes: the run it was in is read as it is now, and
    /// its spec is inserted again at the place it had. Another session of the run that was closed meanwhile stays closed; a run that was dropped
    /// because its last session went comes back with this session alone. The closed start and output tokens that the close added to the run are
    /// taken back, so the run's clock and spend are as they were.
    /// </summary>
    /// <param name="current">The runs as they are now.</param>
    /// <param name="before">The runs as they were before the close (the ones that held the session).</param>
    /// <param name="sessionName">The session that failed to close.</param>
    /// <param name="closedStart">The start the close gave the run (null when the close gave none).</param>
    /// <param name="closedOutput">The output tokens the close added to the run.</param>
    public static IReadOnlyList<RunRecord> WithSessionRestored(
        IReadOnlyList<RunRecord> current,
        IReadOnlyList<RunRecord> before,
        string sessionName,
        DateTimeOffset? closedStart,
        long closedOutput)
    {
        var runs = current.ToList();
        foreach (var original in before)
        {
            var index = original.Specs.ToList().FindIndex(spec => spec.Name == sessionName);
            if (index < 0) continue;
            var spec = original.Specs[index];

            var at = runs.FindIndex(run => string.Equals(run.RunId, original.RunId, StringComparison.Ordinal));
            if (at < 0)
            {
                // The run was dropped: its other sessions had gone too. It comes back with this session only.
                runs.Add(original with { Specs = [spec] });
                continue;
            }

            var run = runs[at];
            if (run.Specs.Any(s => s.Name == sessionName)) continue;
            var specs = run.Specs.ToList();
            specs.Insert(Math.Min(index, specs.Count), spec);
            runs[at] = run with
            {
                Specs = specs,
                ClosedOutputTokens = Math.Max(0, run.ClosedOutputTokens - Math.Max(0, closedOutput)),
                FirstStartedAt = closedStart is { } start && run.FirstStartedAt == start ? original.FirstStartedAt : run.FirstStartedAt,
            };
        }
        return runs;
    }

    /// <summary>The earlier of two optional times. Null is no time, so a missing one gives the other.</summary>
    private static DateTimeOffset? Earlier(DateTimeOffset? kept, DateTimeOffset? closed)
    {
        if (closed is not { } at) return kept;
        if (kept is not { } first) return at;
        return at < first ? at : first;
    }

    /// <summary>Every session of every run, in run order. The supervisor keeps all of them alive.</summary>
    public static IReadOnlyList<SessionSpec> SpecsOf(IEnumerable<RunRecord> runs) =>
        runs.SelectMany(r => r.Specs).ToList();

    /// <summary>
    /// The sessions a pass may start, resume or adopt at <paramref name="now"/>: the specs of every run that is not waiting. A run that
    /// waits has no specs here, so the supervisor neither starts nor adopts its sessions (gems item 5). The rail still shows all runs.
    /// </summary>
    public static IReadOnlyList<SessionSpec> DueSpecsOf(IEnumerable<RunRecord> runs, DateTimeOffset now) =>
        runs.Where(run => !IsWaiting(run, now)).SelectMany(run => run.Specs).ToList();

    /// <summary>Whether a run is held back: it has a start time, and that time is still ahead. The time itself starts the run.</summary>
    public static bool IsWaiting(RunRecord run, DateTimeOffset now) => run.StartAt is { } at && at > now;

    /// <summary>
    /// The runs with the start time of one run moved to now (see <see cref="StartingNow(IEnumerable{RunRecord}, string, DateTimeOffset)"/>).
    /// The other runs are kept as they are.
    /// </summary>
    public static IReadOnlyList<RunRecord> StartingNow(IEnumerable<RunRecord> runs, string runId) =>
        StartingNow(runs, runId, DateTimeOffset.UtcNow);

    /// <summary>
    /// The runs with one run's start time moved to <paramref name="now"/>, so that it starts at once. Only a run that is still waiting
    /// changes. Its start time is moved rather than cleared: a cleared time would count the run's duration limit from its creation, and
    /// the time it waited would then use up the limit (round 5 item 3). The other runs are kept as they are.
    /// </summary>
    public static IReadOnlyList<RunRecord> StartingNow(IEnumerable<RunRecord> runs, string runId, DateTimeOffset now) =>
        runs.Select(run => string.Equals(run.RunId, runId, StringComparison.Ordinal) && IsWaiting(run, now)
            ? run with { StartAt = now }
            : run).ToList();
}

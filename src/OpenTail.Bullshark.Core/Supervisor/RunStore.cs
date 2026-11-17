using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One run started from setup (D-S12): the pattern, the folder, the permission mode, and the full spec list as it was
/// launched. Patterns are edited for the next launch only (D-S6), so the run keeps the specs it was created with.
/// <see cref="StartAt"/> (gems item 5) holds a run back until that time: it has no sessions in a pass until then. Null, the
/// default, means the run starts at once. Files from before the field have no such value, so they load as "at once".
/// </summary>
public sealed record RunRecord(
    string RunId,
    string PatternKey,
    string WorkingDirectory,
    string PermissionMode,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SessionSpec> Specs,
    DateTimeOffset? StartAt = null,
    RunLimits? Limits = null);

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
    public IReadOnlyList<RunRecord> Load()
    {
        using var _ = FileLock.Hold(_path);
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
    public static IReadOnlyList<RunRecord> WithoutSession(IReadOnlyList<RunRecord> runs, string sessionName)
    {
        var kept = new List<RunRecord>();
        foreach (var run in runs)
        {
            var specs = run.Specs.Where(spec => spec.Name != sessionName).ToList();
            if (specs.Count == run.Specs.Count) kept.Add(run);
            else if (specs.Count > 0) kept.Add(run with { Specs = specs });
        }
        return kept;
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

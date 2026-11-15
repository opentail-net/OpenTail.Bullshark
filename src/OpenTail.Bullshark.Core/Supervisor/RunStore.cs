using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One run started from setup (D-S12): the pattern, the folder, the permission mode, and the full spec list as it was
/// launched. Patterns are edited for the next launch only (D-S6), so the run keeps the specs it was created with.
/// </summary>
public sealed record RunRecord(
    string RunId,
    string PatternKey,
    string WorkingDirectory,
    string PermissionMode,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SessionSpec> Specs);

/// <summary>Reads and writes runs.json. Writes go to a temporary file first, like the state file.</summary>
public sealed class RunStore
{
    public const string LegacyRunId = "legacy";

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

    private List<RunRecord> Read()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(_path), Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }
    }

    private void Write(IReadOnlyList<RunRecord> runs) =>
        FileLock.WriteAtomic(_path, JsonSerializer.Serialize(runs.OrderBy(r => r.CreatedAt).ToList(), Options));

    /// <summary>
    /// Creates the legacy run (D-S13), once. It is created only when runs.json does not exist and the state file holds the
    /// default pair. Its folder is the `cwd` the agent listing gives the planner (or the coder); when neither is listed,
    /// the fallback folder is used (D-S17). Returns the new run, or null when nothing was migrated.
    /// </summary>
    public RunRecord? MigrateLegacyIfNeeded(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyList<AgentInfo> agents,
        string fallbackFolder,
        string task,
        DateTimeOffset now)
    {
        if (File.Exists(_path)) return null;

        records.TryGetValue(SessionSpec.PlannerName, out var planner);
        records.TryGetValue(SessionSpec.CoderName, out var coder);
        if (planner is null && coder is null) return null;

        string? CwdOf(SessionRecord? record) =>
            record is null ? null
            : agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase))?.Cwd;

        var folder = CwdOf(planner) ?? CwdOf(coder) ?? fallbackFolder;
        var legacy = new RunRecord(LegacyRunId, "plan-code", folder, "auto", now, RunPlan.Legacy(folder, task));
        Save([legacy]);
        return legacy;
    }

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
}

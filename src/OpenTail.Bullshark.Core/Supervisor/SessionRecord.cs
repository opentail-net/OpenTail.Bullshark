using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// What OpenTail.Bullshark remembers about a running session between checks. Persisted to the state file.
/// Records are keyed by the session's name (D-S13). Role stays in the record for the policy and for display.
/// </summary>
public sealed record SessionRecord(
    SessionRole Role,
    string SessionId,
    string Name,
    DateTimeOffset StartedAt,
    int Restarts,
    IReadOnlyList<DateTimeOffset> RestartTimes,
    string? LastState,
    string? LastAlert,
    /// <summary>
    /// The highest limit level the owner has been told about for this session's run: <see cref="RunBudget.NearLevel"/> or
    /// <see cref="RunBudget.ReachedLevel"/>, or null. It is kept apart from <see cref="LastAlert"/>, which another alert replaces, so one
    /// notice is given per level (round 5 item 8). Files from before the field load as null.
    /// </summary>
    string? LimitLevel = null,
    /// <summary>
    /// When the stale-blocked alert was raised (round 9 item 4). A lost session is held from a resume for an hour from this time, and no longer.
    /// It is read only while <see cref="LastAlert"/> is the stale-blocked alert. Files from before the field load as null.
    /// </summary>
    DateTimeOffset? StaleBlockedAt = null);

/// <summary>
/// Reads and writes the state file. Every change goes through <see cref="Update"/>: the file is read again under the lock,
/// changed, and written. So a change made by the other process (the window or the supervisor) since the change was prepared
/// is kept, and a file that cannot be read is never overwritten.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Action<string> _log;

    public StateStore(string path, Action<string>? log = null)
    {
        _path = path;
        _log = log ?? (_ => { });
    }

    public string Path => _path;

    /// <summary>
    /// Returns the records keyed by session name. If two records share a name, the one with the later StartedAt is
    /// kept, and the other is logged and left out of the result. Ties keep the first record in the file.
    /// An unreadable file throws <see cref="StoreUnreadableException"/>.
    /// </summary>
    public IReadOnlyDictionary<string, SessionRecord> Load() => Load(FileLock.DefaultWait);

    /// <summary>
    /// The same read, waiting at most <paramref name="wait"/> for the lock (reliability batch R7: the window's UI thread waits briefly).
    /// A lock that is not free in time throws <see cref="TimeoutException"/>.
    /// </summary>
    public IReadOnlyDictionary<string, SessionRecord> Load(TimeSpan wait)
    {
        using var _ = FileLock.Hold(_path, wait);
        return Read();
    }

    /// <summary>
    /// Applies a change under the file lock. The file is read again inside the lock first. If it cannot be read, the change
    /// is not applied and <see cref="StoreUnreadableException"/> is thrown.
    /// </summary>
    public void Update(Action<Dictionary<string, SessionRecord>> change)
    {
        using var _ = FileLock.Hold(_path);
        var records = Read();
        change(records);
        Write(records);
    }

    /// <summary>Removes one session's record, so the rail and the supervisor stop tracking it. Other records are kept.</summary>
    public void Forget(string name) => Update(records => records.Remove(name));

    /// <summary>Replaces every record. Callers that change one or two records use <see cref="Update"/>.</summary>
    public void Save(IReadOnlyDictionary<string, SessionRecord> records) => Update(current =>
    {
        current.Clear();
        foreach (var pair in records) current[pair.Key] = pair.Value;
    });

    /// <summary>
    /// The file's records. The file is opened directly, not checked with File.Exists first: a file the user cannot open must throw
    /// <see cref="StoreUnreadableException"/>, because the supervisor pass then stops before it starts or writes anything.
    /// </summary>
    private Dictionary<string, SessionRecord> Read()
    {
        var byName = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return byName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        List<SessionRecord>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<SessionRecord>>(text, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        foreach (var record in records ?? [])
        {
            if (!byName.TryGetValue(record.Name, out var kept))
            {
                byName[record.Name] = record;
                continue;
            }

            var winner = record.StartedAt > kept.StartedAt ? record : kept;
            var loser = ReferenceEquals(winner, record) ? kept : record;
            _log($"state: two records named '{record.Name}' (sessions {kept.SessionId} and {record.SessionId}); " +
                 $"keeping {winner.SessionId}, dropping {loser.SessionId}");
            byName[record.Name] = winner;
        }
        return byName;
    }

    private void Write(IReadOnlyDictionary<string, SessionRecord> records)
    {
        var json = JsonSerializer.Serialize(
            records.Values.OrderBy(r => r.Role).ThenBy(r => r.Name, StringComparer.Ordinal).ToList(), Options);
        FileLock.WriteAtomic(_path, json);
    }
}

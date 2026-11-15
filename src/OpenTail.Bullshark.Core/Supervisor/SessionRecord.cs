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
    string? LastAlert);

/// <summary>Reads and writes the state file. Writes go to a temporary file first, so a crash never leaves half a file.</summary>
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
    /// </summary>
    public IReadOnlyDictionary<string, SessionRecord> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        var json = File.ReadAllText(_path);
        var records = JsonSerializer.Deserialize<List<SessionRecord>>(json, Options) ?? [];

        var byName = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        foreach (var record in records)
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

    /// <summary>Removes one session's record, so the rail and the supervisor stop tracking it. Other records are kept.</summary>
    public void Forget(string name)
    {
        var records = Load().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (records.Remove(name)) Save(records);
    }

    public void Save(IReadOnlyDictionary<string, SessionRecord> records)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            records.Values.OrderBy(r => r.Role).ThenBy(r => r.Name, StringComparer.Ordinal).ToList(), Options);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }
}

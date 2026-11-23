using Microsoft.Data.Sqlite;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>What the last read of opencode's database found (OC-D7). Each value has one fixed phrase in <see cref="OpenCodeHistorySource.TextOf"/>.</summary>
public enum OpenCodeHistoryStatus
{
    /// <summary>The database was read, and its tables and columns are the opencode 1.4 ones.</summary>
    Ok,

    /// <summary>The location is refused (a relative or <c>:memory:</c> <c>OPENCODE_DB</c>, or a relative data folder). Nothing was opened.</summary>
    Refused,

    /// <summary>There is no file at the location. None was created.</summary>
    NoDatabase,

    /// <summary>The file is there, but a table or a column the reader needs is not.</summary>
    NotOpenCodeDatabase,

    /// <summary>A SQLite or file error: not a database, locked past the timeout, or unreadable.</summary>
    Failed,
}

/// <summary>
/// opencode's sessions as neutral summaries (OC-D3 to OC-D6), read from its database, read-only (OC-D2). Only root sessions are listed (<c>parent_id</c>
/// is null), newest first by <c>time_updated</c>. An archived session (<c>time_archived</c> not null) is listed, and its key is in
/// <see cref="ArchivedKeys"/>: <see cref="SessionSummary"/> has no archived field, so the window marks it (OC2). A row whose id is not in the
/// opencode shape, whose folder is empty, or whose time cannot be read is not listed and is counted in <see cref="SkippedRows"/>. A message row whose data
/// is not valid JSON is left out of the counts and counted there too. The user and assistant
/// counts come from one grouped query over <c>message</c>. Nothing is logged and no title or path is kept beyond the summary.
/// </summary>
public sealed class OpenCodeHistorySource(DatabasePath location) : IHistorySource
{
    private const string SessionsSql =
        "SELECT id, title, directory, time_created, time_updated, time_archived FROM session WHERE parent_id IS NULL ORDER BY time_updated DESC";

    private const string CountsSql =
        "SELECT session_id, SUM(CASE WHEN json_extract(data, '$.role') = 'user' THEN 1 ELSE 0 END), "
        + "SUM(CASE WHEN json_extract(data, '$.role') = 'assistant' THEN 1 ELSE 0 END) FROM message WHERE json_valid(data) = 1 GROUP BY session_id";

    /// <summary>The message rows whose data is not valid JSON. They are left out of the counts and counted in <see cref="SkippedRows"/>.</summary>
    private const string BadMessageRowsSql = "SELECT COUNT(*) FROM message WHERE json_valid(data) = 0";

    /// <summary>Rows of the last listing that were not listed: an invalid id, an empty folder, or a time that cannot be read; and message rows whose data is not valid JSON.</summary>
    public int SkippedRows { get; private set; }

    /// <summary>The status of the last listing. Before the first listing it holds the default value (Ok), so read it after <see cref="ListAsync"/>.</summary>
    public OpenCodeHistoryStatus Status { get; private set; }

    /// <summary>The fixed phrase for <see cref="Status"/>.</summary>
    public string StatusText => TextOf(Status);

    /// <summary>The keys of the archived sessions in the last listing (OC-D4). They are listed too.</summary>
    public IReadOnlySet<SessionKey> ArchivedKeys { get; private set; } = new HashSet<SessionKey>();

    /// <summary>The fixed phrase for a status. None holds a path, an id, a title or a message.</summary>
    public static string TextOf(OpenCodeHistoryStatus status) => status switch
    {
        OpenCodeHistoryStatus.Ok => "opencode 1.4 tables",
        OpenCodeHistoryStatus.Refused => "history not available: the database location is not an absolute path; not supported",
        OpenCodeHistoryStatus.NoDatabase => "history not available: no opencode database was found",
        OpenCodeHistoryStatus.NotOpenCodeDatabase => "history not available: the database is not an opencode 1.4 database",
        OpenCodeHistoryStatus.Failed => "history not available: the database could not be read",
        _ => "history not available",
    };

    /// <summary>
    /// The sessions (root sessions, newest first). A refused, missing, or wrong database gives an empty list and a status; a SQLite or file error during
    /// the read gives <see cref="OpenCodeHistoryStatus.Failed"/> and an empty list. Runs on a background thread and honours the token between rows.
    /// </summary>
    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) => Task.Run<IReadOnlyList<SessionSummary>>(() => List(ct), ct);

    private IReadOnlyList<SessionSummary> List(CancellationToken ct)
    {
        SkippedRows = 0;
        ArchivedKeys = new HashSet<SessionKey>();
        var (status, connection) = OpenCodeDatabase.Open(location);
        Status = status;
        if (connection is null) return [];

        try
        {
            using (connection)
            {
                var counts = ReadCounts(connection, ct);
                return ReadSessions(connection, counts, ct);
            }
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            Status = OpenCodeHistoryStatus.Failed;
            ArchivedKeys = new HashSet<SessionKey>();
            return [];
        }
    }

    /// <summary>
    /// The user and assistant message counts of every session that has messages, from one grouped query. Message rows with data that is not valid JSON
    /// are left out, and the first query adds their number to <see cref="SkippedRows"/>.
    /// </summary>
    private Dictionary<string, (int Users, int Assistants)> ReadCounts(SqliteConnection connection, CancellationToken ct)
    {
        var counts = new Dictionary<string, (int Users, int Assistants)>(StringComparer.Ordinal);
        using (var badRows = connection.CreateCommand())
        {
            badRows.CommandText = BadMessageRowsSql;
            SkippedRows += Convert.ToInt32(badRows.ExecuteScalar());
        }
        using var command = connection.CreateCommand();
        command.CommandText = CountsSql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.GetValue(0) is string sessionId)
            {
                counts[sessionId] = ((int)reader.GetInt64(1), (int)reader.GetInt64(2));
            }
        }
        return counts;
    }

    /// <summary>The listed sessions, in the query's order. Sets <see cref="ArchivedKeys"/> only when the whole read succeeds.</summary>
    private IReadOnlyList<SessionSummary> ReadSessions(SqliteConnection connection, Dictionary<string, (int Users, int Assistants)> counts, CancellationToken ct)
    {
        var listed = new List<SessionSummary>();
        var archived = new HashSet<SessionKey>();
        using var command = connection.CreateCommand();
        command.CommandText = SessionsSql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.GetValue(0) is not string id || !OpenCodeSessionId.IsValid(id))
            {
                SkippedRows++;
                continue;
            }

            var folder = reader.GetValue(2) as string;
            var firstActivity = MillisecondsOf(reader.GetValue(3));
            var lastActivity = MillisecondsOf(reader.GetValue(4));
            if (string.IsNullOrWhiteSpace(folder) || firstActivity is null || lastActivity is null)
            {
                SkippedRows++;
                continue;
            }

            var key = new SessionKey(ProviderId.OpenCode, id);
            var title = reader.GetValue(1) as string;
            var (users, assistants) = counts.TryGetValue(id, out var found) ? found : (0, 0);
            listed.Add(new SessionSummary(
                key,
                string.IsNullOrWhiteSpace(title) ? null : title,
                folder,
                firstActivity,
                lastActivity,
                IsSubagent: null,
                users,
                assistants,
                Compactions: null,
                Model: null));
            if (!reader.IsDBNull(5)) archived.Add(key);
        }
        ArchivedKeys = archived;
        return listed;
    }

    /// <summary>A time stored as integer milliseconds since the epoch (OC-D6); null when it is not an integer or is out of range.</summary>
    private static DateTimeOffset? MillisecondsOf(object value)
    {
        if (value is not long milliseconds) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

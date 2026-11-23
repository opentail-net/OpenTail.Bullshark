using Microsoft.Data.Sqlite;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// Where opencode's database is, or why it is not read (OC-D2). Exactly one of the two is set: <see cref="FilePath"/> is the absolute path of the
/// database file, or <see cref="Refusal"/> is the fixed reason it is not read.
/// </summary>
public sealed record DatabasePath(string? FilePath, string? Refusal);

/// <summary>
/// The opencode database (OC-D2, OC-D7, OC-D15): where it is, how it is opened, and the schema guard. Every connection is opened with
/// <c>Mode=ReadOnly</c> and <c>PRAGMA query_only = ON</c>, and the statements this code sends are SELECT and PRAGMA only. The file is never created,
/// and <c>immutable=1</c> is never used, because it would ignore a live write.
/// </summary>
public static class OpenCodeDatabase
{
    /// <summary>The variable that names the database file. An absolute value is used as it is.</summary>
    public const string DatabaseVariable = "OPENCODE_DB";

    /// <summary>The database file's name in the data folder.</summary>
    public const string FileName = "opencode.db";

    /// <summary>
    /// How long a read waits on a lock, in seconds (5000 ms). It is the connection's Default Timeout (see <see cref="ConnectionString"/>).
    /// </summary>
    public const int BusyTimeoutSeconds = 5;

    /// <summary>The fixed reason for a relative or <c>:memory:</c> <c>OPENCODE_DB</c> (OC-D2).</summary>
    public const string RelativeDatabaseRefusal = "OPENCODE_DB is not an absolute path; not supported";

    /// <summary>The fixed reason for a data folder that is derived from a relative <c>XDG_DATA_HOME</c>.</summary>
    public const string RelativeFolderRefusal = "the opencode data folder is not an absolute path; not supported";

    /// <summary>
    /// The database path for an environment (OC-D2). An unset or blank <c>OPENCODE_DB</c> means the data folder: <c>XDG_DATA_HOME\opencode</c> when
    /// <c>XDG_DATA_HOME</c> is set and not blank, else <c>userProfile\.local\share\opencode</c> (the rule of <see cref="ProviderPaths.ForOpenCode"/>,
    /// which supplies the folder here). A file that is not there is not checked: the caller does that.
    /// </summary>
    public static DatabasePath Resolve(IReadOnlyDictionary<string, string?> env, string userProfile)
    {
        string? set = env.TryGetValue(DatabaseVariable, out var value) ? value : null;
        if (!string.IsNullOrWhiteSpace(set))
        {
            return Path.IsPathFullyQualified(set)
                ? new DatabasePath(set, null)
                : new DatabasePath(null, RelativeDatabaseRefusal);
        }

        string folder = ProviderPaths.ForOpenCode(name => env.TryGetValue(name, out var found) ? found : null, userProfile).HomeRoot;
        return Path.IsPathFullyQualified(folder)
            ? new DatabasePath(Path.Combine(folder, FileName), null)
            : new DatabasePath(null, RelativeFolderRefusal);
    }

    /// <summary>
    /// The connection string for a file: <c>Data Source=&lt;path&gt;;Mode=ReadOnly;Cache=Default;Default Timeout=5;Pooling=False</c>. The lock wait is the
    /// Default Timeout (5 seconds). Microsoft.Data.Sqlite documents that setting as the command timeout, also used for its internal commands; its
    /// assembly has a busy-retry routine (<c>RetryWhileBusy</c>), and no test waits on a locked file. Pooling is off: a pooled handle would stay open
    /// on the owner's data after the read, and could keep the database and its -wal and -shm files in use while opencode runs. Each read opens and
    /// closes its own handle.
    /// </summary>
    public static string ConnectionString(string filePath) => new SqliteConnectionStringBuilder
    {
        DataSource = filePath,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Default,
        DefaultTimeout = BusyTimeoutSeconds,
        Pooling = false,
    }.ConnectionString;

    /// <summary>
    /// Opens the database read-only and checks its schema (OC-D7). Returns <see cref="OpenCodeHistoryStatus.Ok"/> with an open connection, which the
    /// caller disposes; any other status with a null connection. Refused: the location is refused, nothing is opened. NoDatabase: there is no file,
    /// and none is created. NotOpenCodeDatabase: a table or a column the reader needs is missing. Failed: a SQLite or file error. Exceptions are
    /// caught by type, never by their message.
    /// </summary>
    public static (OpenCodeHistoryStatus Status, SqliteConnection? Connection) Open(DatabasePath location)
    {
        if (location.FilePath is not { } filePath) return (OpenCodeHistoryStatus.Refused, null);
        if (!File.Exists(filePath)) return (OpenCodeHistoryStatus.NoDatabase, null);

        SqliteConnection? connection = null;
        try
        {
            connection = new SqliteConnection(ConnectionString(filePath));
            connection.Open();
            using (var queryOnly = connection.CreateCommand())
            {
                queryOnly.CommandText = QueryOnlySql;
                queryOnly.ExecuteNonQuery();
            }

            if (!HasColumns(connection, SessionTableInfoSql, SessionColumns)
                || !HasColumns(connection, MessageTableInfoSql, MessageColumns)
                || !HasColumns(connection, PartTableInfoSql, PartColumns))
            {
                connection.Dispose();
                return (OpenCodeHistoryStatus.NotOpenCodeDatabase, null);
            }
            return (OpenCodeHistoryStatus.Ok, connection);
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            connection?.Dispose();
            return (OpenCodeHistoryStatus.Failed, null);
        }
    }

    /// <summary>The first statement on every connection: the connection refuses every write (OC-D2).</summary>
    internal const string QueryOnlySql = "PRAGMA query_only = ON";

    /// <summary>The schema statements. Table names cannot be parameters, so each is a fixed text.</summary>
    internal const string SessionTableInfoSql = "PRAGMA table_info(session)";

    internal const string MessageTableInfoSql = "PRAGMA table_info(message)";

    internal const string PartTableInfoSql = "PRAGMA table_info(part)";

    private static readonly string[] SessionColumns = ["id", "parent_id", "directory", "title", "time_created", "time_updated", "time_archived"];

    private static readonly string[] MessageColumns = ["id", "session_id", "time_created", "time_updated", "data"];

    private static readonly string[] PartColumns = ["id", "message_id", "session_id", "time_created", "time_updated", "data"];

    /// <summary>True when the table's column names (PRAGMA table_info, column 1) include every required name. A missing table has no columns.</summary>
    private static bool HasColumns(SqliteConnection connection, string tableInfoSql, string[] required)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = tableInfoSql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetValue(1) is string name) present.Add(name);
        }
        return required.All(present.Contains);
    }
}

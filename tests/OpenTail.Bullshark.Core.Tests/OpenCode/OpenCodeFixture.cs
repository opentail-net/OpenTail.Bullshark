using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Synthetic opencode databases for the tests (OC1b). Every id, title, folder and text here is made up: the database is built in a temp folder with
/// the v1 tables (no owner data), and the code under test reads that copy only. The ids have the opencode shape (<see cref="OpenCodeSessionId"/>).
/// A test owns one fixture; <see cref="Dispose"/> closes the pooled connections and deletes the folder.
/// </summary>
internal sealed class OpenCodeFixture : IDisposable
{
    public const string RootOne = "ses_0a1b2c3d4e5fAbCdEfGhIjKlMn";
    public const string RootTwo = "ses_1f2e3d4c5b6aZyXwVuTsRqPoNm";
    public const string ArchivedRoot = "ses_3c4d5e6f7a8bLmNoPqRsTuVwXy";
    public const string ChildOfRootOne = "ses_2a3b4c5d6e7fQwErTyUiOpAsDf";
    public const string EmptyFolderRoot = "ses_4a5b6c7d8e9fAbCdEfGhIjKlMn";
    public const string BadTimeRoot = "ses_5b6c7d8e9f0aGhIjKlMnOpQrSt";
    public const string CapRoot = "ses_6c7d8e9f0a1bUvWxYzAbCdEfGh";
    public const string BadIdRow = "ses_not-a-valid-id";
    public const string UnknownId = "ses_9e8d7c6b5a4fAbCdEfGhIjKlMn";

    /// <summary>A fixed time in milliseconds since the epoch (November 2023).</summary>
    public const long BaseTime = 1_700_000_000_000;

    /// <summary>The v1 table definitions (research section 6 and the OC1 brief).</summary>
    public const string SessionDdl =
        "CREATE TABLE session (id TEXT PRIMARY KEY, project_id TEXT, parent_id TEXT, slug TEXT, directory TEXT NOT NULL, title TEXT NOT NULL, "
        + "version TEXT, time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL, time_archived INTEGER)";

    public const string MessageDdl =
        "CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL, data TEXT NOT NULL)";

    public const string PartDdl =
        "CREATE TABLE part (id TEXT PRIMARY KEY, message_id TEXT NOT NULL, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, "
        + "time_updated INTEGER NOT NULL, data TEXT NOT NULL)";

    public const string ProjectDdl =
        "CREATE TABLE project (id TEXT PRIMARY KEY, worktree TEXT NOT NULL, time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL)";

    /// <summary>A session table with no <c>time_archived</c> column: the schema guard must refuse it.</summary>
    public const string SessionDdlWithoutArchive =
        "CREATE TABLE session (id TEXT PRIMARY KEY, project_id TEXT, parent_id TEXT, slug TEXT, directory TEXT NOT NULL, title TEXT NOT NULL, "
        + "version TEXT, time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL)";

    /// <summary>The folder of the fake data home. Created on demand; deleted by <see cref="Dispose"/>.</summary>
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "bullshark-opencode-" + Guid.NewGuid().ToString("N"));

    /// <summary>The database file (<c>opencode.db</c>) in the fake data folder.</summary>
    public string Database => Path.Combine(Folder, OpenCodeDatabase.FileName);

    /// <summary>The synthetic project folders: they need not exist, the reader does not check them.</summary>
    public string ProjectOne => Path.Combine(Folder, "project-one");

    public string ProjectTwo => Path.Combine(Folder, "project-two");

    /// <summary>The database location as <see cref="OpenCodeDatabase.Resolve"/> would give it: this file, with no refusal.</summary>
    public DatabasePath Location => new(Database, null);

    /// <summary>A read-write connection to the fixture (test code only). The caller disposes it.</summary>
    public SqliteConnection OpenWriter()
    {
        Directory.CreateDirectory(Folder);
        // Not pooled: Dispose closes the handle, so a writer is never kept open by the pool (and ClearAllPools in another test cannot close it).
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Creates the standard v1 database with the rows the tests expect (see each test). Rollback-journal mode, no WAL.</summary>
    public void CreateStandard()
    {
        using var writer = OpenWriter();
        Exec(writer, SessionDdl);
        Exec(writer, MessageDdl);
        Exec(writer, PartDdl);
        Exec(writer, ProjectDdl);

        InsertSession(writer, RootTwo, ProjectTwo, "fixture session two", BaseTime + 100, BaseTime + 9000);
        InsertSession(writer, RootOne, ProjectOne, "fixture session one", BaseTime, BaseTime + 5000);
        InsertSession(writer, ArchivedRoot, ProjectOne, "fixture session archived", BaseTime + 200, BaseTime + 1000, archived: BaseTime + 1500);
        InsertSession(writer, ChildOfRootOne, ProjectOne, "fixture child session", BaseTime + 300, BaseTime + 400, parent: RootOne);
        InsertSession(writer, EmptyFolderRoot, string.Empty, "fixture empty folder", BaseTime + 500, BaseTime + 600);
        InsertSession(writer, BadTimeRoot, ProjectOne, "fixture bad time", "not a time", BaseTime + 600);
        InsertSession(writer, BadIdRow, ProjectOne, "fixture bad id", BaseTime, BaseTime);

        // The messages are inserted out of time order on purpose: the reader orders them by time_created, then id.
        InsertMessage(writer, "msg-2-assistant", RootOne, 2000, "assistant");
        InsertMessage(writer, "msg-1-user", RootOne, 1000, "user");
        InsertMessage(writer, "msg-3-user", RootOne, 3000, "user");
        InsertMessage(writer, "msg-4-assistant", RootOne, 4000, "assistant");
        InsertMessage(writer, "msg-5-user", RootOne, 5000, "user");
        InsertMessage(writer, "msg-6-system", RootOne, 6000, "system");
        InsertMessage(writer, "msg-r2-user", RootTwo, 100, "user");

        InsertPart(writer, "prt-1", "msg-1-user", RootOne, 1001, Text("fixture text one"));
        InsertPart(writer, "prt-2", "msg-2-assistant", RootOne, 2001, Text("fixture reply one"));
        InsertPart(writer, "prt-3", "msg-2-assistant", RootOne, 2002, "{\"type\":\"tool\",\"tool\":\"fixture-tool\"}");
        InsertPart(writer, "prt-4", "msg-3-user", RootOne, 3001, "{\"type\":\"text\",\"text\":\"fixture synthetic note\",\"synthetic\":true}");
        InsertPart(writer, "prt-5", "msg-3-user", RootOne, 3002, Text("fixture text two"));
        InsertPart(writer, "prt-6", "msg-5-user", RootOne, 5001, "{\"type\":\"text\",\"text\":\"fixture ignored text\",\"ignored\":true}");
        InsertPart(writer, "prt-7", "msg-4-assistant", RootOne, 4001, "{\"type\":\"step-start\"}");
        InsertPart(writer, "prt-8", "msg-6-system", RootOne, 6001, Text("fixture system text"));
        InsertPart(writer, "prt-r2", "msg-r2-user", RootTwo, 100, Text("fixture root two text"));
    }

    /// <summary>Adds one session with one user message and <paramref name="parts"/> text parts (<c>fixture part 1</c> and so on), for the cap test.</summary>
    public void AddLongSession(int parts)
    {
        using var writer = OpenWriter();
        AddLongSession(writer, parts);
    }

    /// <summary>The same, on a writer the caller already holds (the WAL test keeps one writer open, so no second connection closes).</summary>
    public void AddLongSession(SqliteConnection writer, int parts)
    {
        InsertSession(writer, CapRoot, ProjectOne, "fixture long session", BaseTime, BaseTime);
        InsertMessage(writer, "msg-cap-user", CapRoot, 1, "user");
        using var command = writer.CreateCommand();
        command.CommandText =
            "INSERT INTO part (id, message_id, session_id, time_created, time_updated, data) "
            + "WITH RECURSIVE numbers(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM numbers WHERE i < @parts) "
            + "SELECT printf('prt-cap-%06d', i), 'msg-cap-user', @session, i, i, "
            + "'{\"type\":\"text\",\"text\":\"fixture part ' || i || '\"}' FROM numbers";
        command.Parameters.AddWithValue("@parts", parts);
        command.Parameters.AddWithValue("@session", CapRoot);
        command.ExecuteNonQuery();
    }

    /// <summary>Writes raw bytes to the database file (a file that is not SQLite).</summary>
    public void WriteRawDatabase(byte[] bytes)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Database, bytes);
    }

    /// <summary>The SHA-256 of a file, read with sharing, or null when the file is not there.</summary>
    public static byte[]? HashOf(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }

    /// <summary>The text of a text part's data, as opencode writes it.</summary>
    public static string Text(string text) => "{\"type\":\"text\",\"text\":\"" + text + "\"}";

    private static void InsertSession(
        SqliteConnection connection,
        string id,
        string directory,
        string title,
        object created,
        object updated,
        string? parent = null,
        long? archived = null) => Exec(
        connection,
        "INSERT INTO session (id, parent_id, directory, title, time_created, time_updated, time_archived) "
        + "VALUES (@id, @parent, @directory, @title, @created, @updated, @archived)",
        ("@id", id),
        ("@parent", parent),
        ("@directory", directory),
        ("@title", title),
        ("@created", created),
        ("@updated", updated),
        ("@archived", archived));

    private static void InsertMessage(SqliteConnection connection, string id, string sessionId, long time, string role) => Exec(
        connection,
        "INSERT INTO message (id, session_id, time_created, time_updated, data) VALUES (@id, @session, @time, @time, @data)",
        ("@id", id),
        ("@session", sessionId),
        ("@time", time),
        ("@data", "{\"role\":\"" + role + "\"}"));

    private static void InsertPart(SqliteConnection connection, string id, string messageId, string sessionId, long time, string data) => Exec(
        connection,
        "INSERT INTO part (id, message_id, session_id, time_created, time_updated, data) VALUES (@id, @message, @session, @time, @time, @data)",
        ("@id", id),
        ("@message", messageId),
        ("@session", sessionId),
        ("@time", time),
        ("@data", data));

    /// <summary>Runs one statement with named values (test code only). A null value is stored as NULL.</summary>
    public static void Exec(SqliteConnection connection, string sql, params (string Name, object? Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // Pooled connections keep their files open; closing them first lets the folder go (a locked file is left behind, not fatal).
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(Folder)) return;
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

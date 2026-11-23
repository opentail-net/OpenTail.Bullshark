using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1b, the read-only guarantee (OC-D2, OC-D15): the connection string asks for Mode=ReadOnly; a full list and a full transcript read leave the
/// database, its -wal and its -shm unchanged (a rollback-journal fixture, and a WAL fixture with its writer still open); the connection refuses writes;
/// and no SQL string in the three new source files has a write verb. The fixtures are temp copies; no owner database is opened.
/// </summary>
public sealed class OpenCodeReadOnlyTests : IDisposable
{
    private static readonly string[] WriteVerbs =
    [
        "INSERT", "UPDATE", "DELETE", "REPLACE", "CREATE", "DROP", "ALTER", "ATTACH", "DETACH", "VACUUM", "BEGIN", "COMMIT",
    ];

    private static readonly Regex StringLiteral = new("\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"", RegexOptions.CultureInvariant);

    private readonly OpenCodeFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void TheConnectionStringAsksForReadOnly()
    {
        string text = OpenCodeDatabase.ConnectionString(_fixture.Database);

        Assert.Contains("Mode=ReadOnly", text);
    }

    [Fact]
    public async Task AFullListAndTranscriptReadLeaveTheDatabaseFilesUnchanged()
    {
        _fixture.CreateStandard();
        byte[]? databaseBefore = OpenCodeFixture.HashOf(_fixture.Database);
        byte[]? walBefore = OpenCodeFixture.HashOf(_fixture.Database + "-wal");
        byte[]? shmBefore = OpenCodeFixture.HashOf(_fixture.Database + "-shm");

        var listed = await new OpenCodeHistorySource(_fixture.Location).ListAsync(CancellationToken.None);
        var page = await new OpenCodeTranscriptReader(_fixture.Location).ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.Equal(3, listed.Count);
        Assert.Equal(4, page.Lines.Count);
        Assert.Equal(databaseBefore, OpenCodeFixture.HashOf(_fixture.Database));
        Assert.Equal(walBefore, OpenCodeFixture.HashOf(_fixture.Database + "-wal"));
        Assert.Equal(shmBefore, OpenCodeFixture.HashOf(_fixture.Database + "-shm"));
    }

    [Fact]
    public async Task AWalDatabaseIsReadAndItsWalIsUnchanged()
    {
        _fixture.CreateStandard();
        using var writer = _fixture.OpenWriter();
        using (var walMode = writer.CreateCommand())
        {
            walMode.CommandText = "PRAGMA journal_mode = WAL";
            string? mode = walMode.ExecuteScalar()?.ToString();
            Assert.Equal("wal", mode);
        }
        _fixture.AddLongSession(writer, 50);
        Assert.True(File.Exists(_fixture.Database + "-wal"), "the fixture must have a -wal file while its writer is open; files: " + string.Join(",", Directory.GetFiles(_fixture.Folder).Select(Path.GetFileName)));
        byte[]? walBefore = OpenCodeFixture.HashOf(_fixture.Database + "-wal");
        Assert.NotNull(walBefore);

        var listed = await new OpenCodeHistorySource(_fixture.Location).ListAsync(CancellationToken.None);
        var page = await new OpenCodeTranscriptReader(_fixture.Location).ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.Contains(listed, session => session.Key.SessionId == OpenCodeFixture.RootOne);
        Assert.Equal(4, page.Lines.Count);
        Assert.Equal(walBefore, OpenCodeFixture.HashOf(_fixture.Database + "-wal"));
    }

    [Fact]
    public void AnOpenedConnectionRefusesWrites()
    {
        _fixture.CreateStandard();
        var (status, connection) = OpenCodeDatabase.Open(_fixture.Location);
        Assert.Equal(OpenCodeHistoryStatus.Ok, status);
        using var owner = connection;

        using var insert = connection!.CreateCommand();
        insert.CommandText = "INSERT INTO session (id, directory, title, time_created, time_updated) VALUES ('x', 'x', 'x', 1, 1)";
        Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());
    }

    [Fact]
    public async Task ReadingLeavesNoHandleOpen()
    {
        _fixture.CreateStandard();
        var listed = await new OpenCodeHistorySource(_fixture.Location).ListAsync(CancellationToken.None);
        Assert.NotEmpty(listed);

        // A second, test-only connection in read-write mode, not pooled: it opens the fixture file and takes the write lock without an error.
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _fixture.Database,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ConnectionString);
        writer.Open();
        using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            begin.ExecuteNonQuery();
        }
        using (var rollback = writer.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK";
            rollback.ExecuteNonQuery();
        }
    }

    [Fact]
    public void NoSqlStringInTheNewFilesHasAWriteVerb()
    {
        string folder = SourceFolder();
        string[] files = ["OpenCodeDatabase.cs", "OpenCodeHistorySource.cs", "OpenCodeTranscriptReader.cs"];

        var found = new List<string>();
        var literals = 0;
        foreach (var file in files)
        {
            string text = File.ReadAllText(Path.Combine(folder, file));
            foreach (Match literal in StringLiteral.Matches(text))
            {
                literals++;
                foreach (var verb in WriteVerbs)
                {
                    if (Regex.IsMatch(literal.Value, $"\\b{verb}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        found.Add($"{file}: {verb} in {literal.Value}");
                    }
                }
            }
        }

        Assert.True(literals > 10, "the scan must see the string literals of the new files");
        Assert.Empty(found);
    }

    /// <summary>The folder of the new OpenCode source files, found from this test file's own path (tests\...\OpenCode, up to the repo root, then src).</summary>
    private static string SourceFolder([CallerFilePath] string thisFile = "")
    {
        string testFolder = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testFolder, "..", "..", "..", "src", "OpenTail.Bullshark.Core", "OpenCode"));
    }
}

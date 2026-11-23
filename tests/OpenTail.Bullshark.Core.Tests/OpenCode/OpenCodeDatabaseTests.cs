using Microsoft.Data.Sqlite;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1b (OC-D2, OC-D7): where the database is (the environment rule, pure), how it is opened (read-only, schema guard), and the fixed refusal
/// texts. Only fixture files in a temp folder are opened; the environment is passed in, never read.
/// </summary>
public sealed class OpenCodeDatabaseTests : IDisposable
{
    private readonly OpenCodeFixture _fixture = new();

    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "bullshark-opencode-profile");

    public void Dispose() => _fixture.Dispose();

    private static Dictionary<string, string?> Env(params (string Name, string? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value);

    [Fact]
    public void AnAbsoluteOpenCodeDbIsUsedAsItIs()
    {
        string path = Path.Combine(Path.GetTempPath(), "fixture-opencode", "custom.db");

        var location = OpenCodeDatabase.Resolve(Env((OpenCodeDatabase.DatabaseVariable, path)), Profile);

        Assert.Equal(path, location.FilePath);
        Assert.Null(location.Refusal);
    }

    [Theory]
    [InlineData("relative\\opencode.db")]
    [InlineData(":memory:")]
    public void ARelativeOrMemoryOpenCodeDbIsRefused(string value)
    {
        var location = OpenCodeDatabase.Resolve(Env((OpenCodeDatabase.DatabaseVariable, value)), Profile);

        Assert.Null(location.FilePath);
        Assert.Equal("OPENCODE_DB is not an absolute path; not supported", location.Refusal);
    }

    [Fact]
    public void ABlankOpenCodeDbIsUnset()
    {
        var location = OpenCodeDatabase.Resolve(Env((OpenCodeDatabase.DatabaseVariable, "   ")), Profile);

        Assert.Equal(Path.Combine(Profile, ".local", "share", "opencode", "opencode.db"), location.FilePath);
    }

    [Fact]
    public void WithNoOpenCodeDbTheXdgDataHomeIsUsedWhenSet()
    {
        string xdg = Path.Combine(Path.GetTempPath(), "fixture-xdg");

        var location = OpenCodeDatabase.Resolve(Env((ProviderPaths.XdgDataHomeVariable, xdg)), Profile);

        Assert.Equal(Path.Combine(xdg, "opencode", "opencode.db"), location.FilePath);
        Assert.Null(location.Refusal);
    }

    [Fact]
    public void WithNoOpenCodeDbAndNoXdgTheProfileFolderIsUsed()
    {
        var location = OpenCodeDatabase.Resolve(Env(), Profile);

        Assert.Equal(Path.Combine(Profile, ".local", "share", "opencode", "opencode.db"), location.FilePath);
    }

    [Fact]
    public void ABlankXdgDataHomeIsUnset()
    {
        var location = OpenCodeDatabase.Resolve(Env((ProviderPaths.XdgDataHomeVariable, " ")), Profile);

        Assert.Equal(Path.Combine(Profile, ".local", "share", "opencode", "opencode.db"), location.FilePath);
    }

    [Fact]
    public void ARelativeXdgDataHomeIsRefusedBecauseTheFolderIsNotAbsolute()
    {
        var location = OpenCodeDatabase.Resolve(Env((ProviderPaths.XdgDataHomeVariable, "relative-data")), Profile);

        Assert.Null(location.FilePath);
        Assert.Equal("the opencode data folder is not an absolute path; not supported", location.Refusal);
    }

    [Fact]
    public void ResolveDoesNotCheckThatTheFileExists()
    {
        string missing = Path.Combine(Path.GetTempPath(), "bullshark-missing-" + Guid.NewGuid().ToString("N"), "opencode.db");

        var location = OpenCodeDatabase.Resolve(Env((OpenCodeDatabase.DatabaseVariable, missing)), Profile);

        Assert.Equal(missing, location.FilePath);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void TheConnectionStringIsReadOnlyWithTheDefaultCacheAndAFiveSecondTimeout()
    {
        string text = OpenCodeDatabase.ConnectionString(_fixture.Database);

        Assert.Contains("Mode=ReadOnly", text);
        var builder = new SqliteConnectionStringBuilder(text);
        Assert.Equal(SqliteOpenMode.ReadOnly, builder.Mode);
        Assert.Equal(SqliteCacheMode.Default, builder.Cache);
        Assert.Equal(5, builder.DefaultTimeout);
        Assert.Equal(_fixture.Database, builder.DataSource);
    }

    [Fact]
    public void TheConnectionStringIsNotPooled()
    {
        string text = OpenCodeDatabase.ConnectionString(_fixture.Database);

        Assert.Contains("Pooling=False", text);
        Assert.False(new SqliteConnectionStringBuilder(text).Pooling);
    }

    [Fact]
    public void ARefusedLocationIsNotOpened()
    {
        var (status, connection) = OpenCodeDatabase.Open(new DatabasePath(null, OpenCodeDatabase.RelativeDatabaseRefusal));

        Assert.Equal(OpenCodeHistoryStatus.Refused, status);
        Assert.Null(connection);
    }

    [Fact]
    public void AMissingFileGivesNoDatabaseAndIsNotCreated()
    {
        var (status, connection) = OpenCodeDatabase.Open(_fixture.Location);

        Assert.Equal(OpenCodeHistoryStatus.NoDatabase, status);
        Assert.Null(connection);
        Assert.False(File.Exists(_fixture.Database));
    }

    [Fact]
    public void AGoodDatabaseOpensReadOnlyWithQueryOnlyOn()
    {
        _fixture.CreateStandard();

        var (status, connection) = OpenCodeDatabase.Open(_fixture.Location);

        Assert.Equal(OpenCodeHistoryStatus.Ok, status);
        Assert.NotNull(connection);
        using var connectionOwner = connection;
        using var query = connection!.CreateCommand();
        query.CommandText = "PRAGMA query_only";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public void ANonSqliteFileGivesFailedAndNoConnection()
    {
        _fixture.WriteRawDatabase(Enumerable.Range(0, 4096).Select(i => (byte)(i * 37 % 251 + 1)).ToArray());

        var (status, connection) = OpenCodeDatabase.Open(_fixture.Location);

        Assert.Equal(OpenCodeHistoryStatus.Failed, status);
        Assert.Null(connection);
    }

    [Fact]
    public void ADatabaseWithoutThePartTableIsNotAnOpenCodeDatabase()
    {
        using (var writer = _fixture.OpenWriter())
        {
            OpenCodeFixture.Exec(writer, OpenCodeFixture.SessionDdl);
            OpenCodeFixture.Exec(writer, OpenCodeFixture.MessageDdl);
        }

        var (status, connection) = OpenCodeDatabase.Open(_fixture.Location);

        Assert.Equal(OpenCodeHistoryStatus.NotOpenCodeDatabase, status);
        Assert.Null(connection);
    }

    [Fact]
    public void ADatabaseWithoutTheArchiveColumnIsNotAnOpenCodeDatabase()
    {
        using (var writer = _fixture.OpenWriter())
        {
            OpenCodeFixture.Exec(writer, OpenCodeFixture.SessionDdlWithoutArchive);
            OpenCodeFixture.Exec(writer, OpenCodeFixture.MessageDdl);
            OpenCodeFixture.Exec(writer, OpenCodeFixture.PartDdl);
        }

        var (status, connection) = OpenCodeDatabase.Open(_fixture.Location);

        Assert.Equal(OpenCodeHistoryStatus.NotOpenCodeDatabase, status);
        Assert.Null(connection);
    }
}

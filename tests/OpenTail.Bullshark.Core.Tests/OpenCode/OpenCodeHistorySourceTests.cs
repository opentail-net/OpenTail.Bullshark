using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1b (OC-D3 to OC-D7, OC-D14): the rail rows from a synthetic v1 database. Root sessions only, archived sessions marked, the counts from one
/// query, the skipped rows counted, and the fixed status texts. Every row is made up and the fixture is a temp copy.
/// </summary>
public sealed class OpenCodeHistorySourceTests : IDisposable
{
    private readonly OpenCodeFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ListsRootSessionsNewestFirstAndNoChildSession()
    {
        _fixture.CreateStandard();
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        var ids = listed.Select(session => session.Key.SessionId).ToList();
        Assert.Equal(new[] { OpenCodeFixture.RootTwo, OpenCodeFixture.RootOne, OpenCodeFixture.ArchivedRoot }, ids);
        Assert.DoesNotContain(OpenCodeFixture.ChildOfRootOne, ids);
        Assert.Equal(OpenCodeHistoryStatus.Ok, source.Status);
    }

    [Fact]
    public async Task ASessionCarriesItsTitleFolderTimesAndKey()
    {
        _fixture.CreateStandard();
        var source = new OpenCodeHistorySource(_fixture.Location);

        var one = (await source.ListAsync(CancellationToken.None)).Single(session => session.Key.SessionId == OpenCodeFixture.RootOne);

        Assert.Equal(new SessionKey(ProviderId.OpenCode, OpenCodeFixture.RootOne), one.Key);
        Assert.Equal("fixture session one", one.Title);
        Assert.Equal(_fixture.ProjectOne, one.Folder);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(OpenCodeFixture.BaseTime), one.FirstActivity);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(OpenCodeFixture.BaseTime + 5000), one.LastActivity);
        Assert.Null(one.Model);
        Assert.Null(one.Compactions);
    }

    [Fact]
    public async Task AnArchivedSessionIsListedAndItsKeyIsMarked()
    {
        _fixture.CreateStandard();
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        var archived = new SessionKey(ProviderId.OpenCode, OpenCodeFixture.ArchivedRoot);
        Assert.Contains(listed, session => session.Key == archived);
        Assert.Single(source.ArchivedKeys);
        Assert.Contains(archived, source.ArchivedKeys);
    }

    [Fact]
    public async Task TheUserAndAssistantCountsComeFromTheMessages()
    {
        _fixture.CreateStandard();
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        // RootOne: three user messages (1, 3, 5), two assistant messages (2, 4); the system message (6) is in neither count.
        var one = listed.Single(session => session.Key.SessionId == OpenCodeFixture.RootOne);
        Assert.Equal(3, one.UserMessages);
        Assert.Equal(2, one.AssistantMessages);
        var two = listed.Single(session => session.Key.SessionId == OpenCodeFixture.RootTwo);
        Assert.Equal(1, two.UserMessages);
        Assert.Equal(0, two.AssistantMessages);
        var archived = listed.Single(session => session.Key.SessionId == OpenCodeFixture.ArchivedRoot);
        Assert.Equal(0, archived.UserMessages);
        Assert.Equal(0, archived.AssistantMessages);
    }

    [Fact]
    public async Task RowsThatCannotBeListedAreSkippedAndCounted()
    {
        _fixture.CreateStandard();
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        // The bad id, the empty folder and the time that is text are not listed; each is counted once.
        Assert.Equal(3, source.SkippedRows);
        Assert.DoesNotContain(listed, session => session.Key.SessionId == OpenCodeFixture.BadTimeRoot);
        Assert.DoesNotContain(listed, session => session.Key.SessionId == OpenCodeFixture.EmptyFolderRoot);
        Assert.DoesNotContain(listed, session => session.Key.SessionId == OpenCodeFixture.BadIdRow);
    }

    [Fact]
    public async Task ABadJsonRowIsSkippedAndCounted()
    {
        _fixture.CreateStandard();
        using (var writer = _fixture.OpenWriter())
        {
            // A message and a part whose data is not JSON: neither the list nor the transcript fails, and both rows are left out.
            OpenCodeFixture.Exec(
                writer,
                "INSERT INTO message (id, session_id, time_created, time_updated, data) VALUES (@id, @session, @time, @time, @data)",
                ("@id", "msg-bad-json"),
                ("@session", OpenCodeFixture.RootOne),
                ("@time", 2500L),
                ("@data", "not json"));
            OpenCodeFixture.Exec(
                writer,
                "INSERT INTO part (id, message_id, session_id, time_created, time_updated, data) VALUES (@id, @message, @session, @time, @time, @data)",
                ("@id", "prt-bad-json"),
                ("@message", "msg-3-user"),
                ("@session", OpenCodeFixture.RootOne),
                ("@time", 3003L),
                ("@data", "not json"));
        }
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);
        var page = await new OpenCodeTranscriptReader(_fixture.Location).ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.Equal(OpenCodeHistoryStatus.Ok, source.Status);
        Assert.Equal(
            new[] { OpenCodeFixture.RootTwo, OpenCodeFixture.RootOne, OpenCodeFixture.ArchivedRoot },
            listed.Select(session => session.Key.SessionId).ToList());
        var one = listed.Single(session => session.Key.SessionId == OpenCodeFixture.RootOne);
        Assert.Equal(3, one.UserMessages);
        Assert.Equal(2, one.AssistantMessages);
        var two = listed.Single(session => session.Key.SessionId == OpenCodeFixture.RootTwo);
        Assert.Equal(1, two.UserMessages);
        Assert.Equal(0, two.AssistantMessages);
        Assert.Equal(
            new[] { "you: fixture text one", "opencode: fixture reply one", "you: fixture text two", "you: fixture ignored text" },
            page.Lines);
        // The three rows the standard fixture does not list, and the bad message row.
        Assert.Equal(4, source.SkippedRows);
    }

    [Fact]
    public async Task AnEmptyDatabaseIsOkWithNoSessions()
    {
        using (var writer = _fixture.OpenWriter())
        {
            OpenCodeFixture.Exec(writer, OpenCodeFixture.SessionDdl);
            OpenCodeFixture.Exec(writer, OpenCodeFixture.MessageDdl);
            OpenCodeFixture.Exec(writer, OpenCodeFixture.PartDdl);
        }
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        Assert.Empty(listed);
        Assert.Equal(OpenCodeHistoryStatus.Ok, source.Status);
        Assert.Equal("opencode 1.4 tables", source.StatusText);
    }

    [Fact]
    public async Task AMissingDatabaseGivesNoDatabaseAndAnEmptyListAndCreatesNothing()
    {
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        Assert.Empty(listed);
        Assert.Equal(OpenCodeHistoryStatus.NoDatabase, source.Status);
        Assert.Equal("history not available: no opencode database was found", source.StatusText);
        Assert.False(File.Exists(_fixture.Database));
    }

    [Fact]
    public async Task ADatabaseWithoutThePartTableGivesNotOpenCodeDatabase()
    {
        using (var writer = _fixture.OpenWriter())
        {
            OpenCodeFixture.Exec(writer, OpenCodeFixture.SessionDdl);
            OpenCodeFixture.Exec(writer, OpenCodeFixture.MessageDdl);
        }
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        Assert.Empty(listed);
        Assert.Equal(OpenCodeHistoryStatus.NotOpenCodeDatabase, source.Status);
        Assert.Equal("history not available: the database is not an opencode 1.4 database", source.StatusText);
        Assert.DoesNotContain(_fixture.Folder, source.StatusText);
    }

    [Fact]
    public async Task ANonSqliteFileGivesFailedWithTheFixedTextAndNoThrow()
    {
        _fixture.WriteRawDatabase(Enumerable.Range(0, 4096).Select(i => (byte)(i * 37 % 251 + 1)).ToArray());
        var source = new OpenCodeHistorySource(_fixture.Location);

        var listed = await source.ListAsync(CancellationToken.None);

        Assert.Empty(listed);
        Assert.Equal(OpenCodeHistoryStatus.Failed, source.Status);
        Assert.Equal("history not available: the database could not be read", source.StatusText);
        Assert.Empty(source.ArchivedKeys);
    }

    [Fact]
    public async Task ARefusedLocationGivesRefusedAndAnEmptyList()
    {
        var refused = OpenCodeDatabase.Resolve(new Dictionary<string, string?> { [OpenCodeDatabase.DatabaseVariable] = "relative\\opencode.db" }, _fixture.Folder);
        var source = new OpenCodeHistorySource(refused);

        var listed = await source.ListAsync(CancellationToken.None);

        Assert.Empty(listed);
        Assert.Equal(OpenCodeHistoryStatus.Refused, source.Status);
        Assert.Equal("history not available: the database location is not an absolute path; not supported", source.StatusText);
    }

    [Fact]
    public async Task ACancelledTokenIsHonoured()
    {
        _fixture.CreateStandard();
        var source = new OpenCodeHistorySource(_fixture.Location);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListAsync(cancelled.Token));
    }

    [Fact]
    public void EveryStatusHasADistinctFixedPhraseWithoutAPath()
    {
        var statuses = Enum.GetValues<OpenCodeHistoryStatus>();
        var texts = statuses.Select(OpenCodeHistorySource.TextOf).ToList();

        Assert.Equal(statuses.Length, texts.Distinct().Count());
        foreach (var text in texts)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("\\", text);
            Assert.DoesNotContain("/", text);
        }
    }
}

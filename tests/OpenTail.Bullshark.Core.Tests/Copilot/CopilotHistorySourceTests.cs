using System.Text;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.2 (G-D3, G-D16, G-D19): Copilot's history from synthetic session folders. Plain text on both targets, so no net11.0 split. Every
/// folder is under a temp home that the test deletes.
/// </summary>
public sealed class CopilotHistorySourceTests : IDisposable
{
    private readonly CopilotHome _home = new();

    public void Dispose() => _home.Dispose();

    private CopilotHistorySource Source() => new(_home.Paths());

    [Fact]
    public async Task ASessionIsListedWithItsSharedFieldsAndItsCopilotFacts()
    {
        _home.WriteWorkspace(CopilotHome.SessionId, CopilotHome.Workspace(CopilotHome.SessionId));
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));
        var source = Source();

        var listing = Assert.Single(await source.ListDetailedAsync(CancellationToken.None));

        var session = listing.Summary;
        Assert.Equal(new SessionKey(ProviderId.Copilot, CopilotHome.SessionId), session.Key);
        Assert.Equal("Fixture session name", session.Title);
        Assert.Equal("C:\\Fixture\\Demo", session.Folder);
        Assert.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), session.FirstActivity);
        Assert.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero), session.LastActivity);
        Assert.Null(session.IsSubagent);
        Assert.Equal(2, session.UserMessages);
        Assert.Equal(2, session.AssistantMessages);
        Assert.Null(session.Compactions);
        Assert.Equal("fixture-model-b", session.Model);
        Assert.Equal(2, listing.Info.Turns);
        Assert.Equal("C:\\Fixture\\Demo", listing.Info.GitRoot);
        Assert.Equal("fixture-branch", listing.Info.Branch);
        Assert.Equal("fixture/demo", listing.Info.Repository);
        Assert.Equal<bool?>(false, listing.Info.UserNamed);
        Assert.Equal(0, source.SkippedFiles);
        Assert.Equal(0, source.SkippedLines);
    }

    [Fact]
    public async Task ASessionWithNoWorkspaceNameIsTitledByItsFirstUserMessage()
    {
        _home.WriteWorkspace(CopilotHome.SessionId, CopilotHome.Workspace(CopilotHome.SessionId, name: string.Empty));
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal(CopilotHome.FirstMessage, session.Title);
    }

    [Fact]
    public async Task TheFirstUserMessageIsCollapsedAndCutAtEightyCharacters()
    {
        var long100 = new string('a', 99) + "z";
        _home.WriteEvents(CopilotHome.SessionId,
            CopilotHome.SessionStart(CopilotHome.SessionId),
            CopilotHome.UserMessage("2026-11-20T10:00:02.000Z", "  Fix\n\n   the\tparser  "),
            CopilotHome.UserMessage("2026-11-20T10:00:03.000Z", long100));

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal("Fix the parser", session.Title);

        var secondId = CopilotHome.SecondId;
        _home.WriteEvents(secondId, CopilotHome.SessionStart(secondId), CopilotHome.UserMessage("2026-11-20T10:00:02.000Z", long100));
        var cut = (await Source().ListAsync(CancellationToken.None)).Single(s => s.Key.SessionId == secondId);
        Assert.Equal(80, cut.Title!.Length);
        Assert.Equal(long100[..80], cut.Title);
    }

    [Fact]
    public async Task AFolderWithNoEventsFileIsAnEmptySessionCountedAndNotListed()
    {
        _home.WriteWorkspace(CopilotHome.EmptyId, CopilotHome.Workspace(CopilotHome.EmptyId));
        var source = Source();

        var sessions = await source.ListAsync(CancellationToken.None);

        Assert.Empty(sessions);
        Assert.Equal(1, source.EmptySessions);
        Assert.Equal(0, source.SkippedFiles);
    }

    [Fact]
    public async Task AFolderWhoseSessionIdIsNotItsFolderNameIsDamagedAndNotListed()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.SessionStart(CopilotHome.SecondId));
        var source = Source();

        var sessions = await source.ListAsync(CancellationToken.None);

        Assert.Empty(sessions);
        Assert.Equal(1, source.SkippedFiles);
        Assert.Equal(0, source.EmptySessions);
    }

    [Fact]
    public async Task AFolderWhoseSessionStartHasNoIdIsDamagedAndNotListed()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Event("session.start", "2026-11-20T10:00:00.000Z", "{\"version\":1}"));
        var source = Source();

        Assert.Empty(await source.ListAsync(CancellationToken.None));
        Assert.Equal(1, source.SkippedFiles);
    }

    [Fact]
    public async Task AFolderWithNoSessionStartAtAllIsDamagedAndNotListed()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.UserMessage("2026-11-20T10:00:02.000Z", "orphan"));
        var source = Source();

        Assert.Empty(await source.ListAsync(CancellationToken.None));
        Assert.Equal(1, source.SkippedFiles);
    }

    [Fact]
    public async Task AMissingCwdIsNullAndTheContextCwdIsTheFallback()
    {
        // The workspace has no cwd and the session start has none either: the folder is unknown, never guessed.
        _home.WriteWorkspace(CopilotHome.SessionId, "name: No cwd", "updated_at: 2026-11-20T10:05:00.000Z");
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.SessionStart(CopilotHome.SessionId, cwd: null));
        var none = Assert.Single(await Source().ListAsync(CancellationToken.None));
        Assert.Null(none.Folder);

        // The workspace has no cwd but the session start does: the start's cwd is used.
        _home.WriteWorkspace(CopilotHome.SecondId, "name: Context cwd", "updated_at: 2026-11-20T10:05:00.000Z");
        _home.WriteEvents(CopilotHome.SecondId, CopilotHome.SessionStart(CopilotHome.SecondId, cwd: "C:\\Fixture\\Context"));
        var fallback = (await Source().ListAsync(CancellationToken.None)).Single(s => s.Key.SessionId == CopilotHome.SecondId);
        Assert.Equal("C:\\Fixture\\Context", fallback.Folder);
    }

    [Fact]
    public async Task ABadLineIsSkippedAndCountedAndTheSessionIsStillListed()
    {
        _home.WriteEvents(CopilotHome.SessionId,
            CopilotHome.SessionStart(CopilotHome.SessionId),
            "this is not json",
            CopilotHome.UserMessage("2026-11-20T10:00:02.000Z", CopilotHome.FirstMessage));
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(1, source.SkippedLines);
        Assert.Equal(0, source.SkippedFiles);
        Assert.Equal(1, session.UserMessages);
    }

    [Fact]
    public async Task ALineOverSixteenMegabytesIsSkippedAndCounted()
    {
        var path = _home.WriteEvents(CopilotHome.SessionId, CopilotHome.SessionStart(CopilotHome.SessionId));
        // One line of 16 MB plus ten characters, between two good lines: it is skipped without being stored.
        var huge = new string('x', 16 * 1024 * 1024 + 10);
        File.WriteAllText(path, CopilotHome.SessionStart(CopilotHome.SessionId) + "\n" + huge + "\n" +
            CopilotHome.UserMessage("2026-11-20T10:00:02.000Z", CopilotHome.FirstMessage) + "\n", new UTF8Encoding(false));
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(1, source.SkippedLines);
        Assert.Equal(1, session.UserMessages);
    }

    [Fact]
    public async Task AKnownZeroIsZeroAndTheModelFallsBackToTheStartModel()
    {
        // A session with a start and a shutdown only: no messages, no turns, no model change. The counts are zero, not null (G-D16).
        _home.WriteEvents(CopilotHome.EmptyId, CopilotHome.SessionStart(CopilotHome.EmptyId, selectedModel: "fixture-model-c"), CopilotHome.Shutdown("2026-11-20T10:00:07.000Z"));
        var source = Source();

        var listing = Assert.Single(await source.ListDetailedAsync(CancellationToken.None));

        Assert.Equal(0, listing.Summary.UserMessages);
        Assert.Equal(0, listing.Summary.AssistantMessages);
        Assert.Equal(0, listing.Info.Turns);
        Assert.Equal("fixture-model-c", listing.Summary.Model);
        Assert.Null(listing.Summary.Title);
        Assert.Null(listing.Summary.LastActivity);
    }

    [Fact]
    public async Task TheLastModelChangeWinsOverTheStartModel()
    {
        _home.WriteEvents(CopilotHome.SessionId,
            CopilotHome.SessionStart(CopilotHome.SessionId, selectedModel: "fixture-model-c"),
            CopilotHome.ModelChange("2026-11-20T10:00:01.000Z", "fixture-model-d"),
            CopilotHome.ModelChange("2026-11-20T10:00:02.000Z", "fixture-model-e"));

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal("fixture-model-e", session.Model);
    }

    [Fact]
    public async Task AnUnreadableWorkspaceIsCountedAndTheSessionIsListedWithoutItsValues()
    {
        _home.WriteWorkspace(CopilotHome.SessionId, "name: fine", "  nested: not understood");
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(1, source.SkippedFiles);
        Assert.Equal(CopilotHome.FirstMessage, session.Title);
        Assert.Equal("C:\\Fixture\\Demo", session.Folder);
        Assert.Null(session.LastActivity);
    }

    [Fact]
    public async Task AMissingHistoryRootGivesAnEmptyListWithNoCounts()
    {
        var paths = ProviderPaths.ForCopilot(name => name == ProviderPaths.CopilotHomeVariable ? Path.Combine(_home.Home, "absent") : null);
        var source = new CopilotHistorySource(paths);

        Assert.Empty(await source.ListAsync(CancellationToken.None));
        Assert.Equal(0, source.SkippedFiles);
        Assert.Equal(0, source.EmptySessions);
    }

    [Fact]
    public async Task ACancelledListingThrows()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source().ListAsync(cancelled.Token));
    }
}

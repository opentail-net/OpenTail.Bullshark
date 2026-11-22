using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// The newest-in-folder candidates (R7): the sessions of one folder, newest first, the count at the top rank, the earliest of the tied ones, and what is
/// left out. Synthetic values only: the folders are made-up paths and the ids are made-up.
/// </summary>
public sealed class CodexHistoryQueryTests
{
    private const string Folder = "C:\\Work\\Synthetic Folder";
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static SessionSummary Session(string id, string? folder, DateTimeOffset? last, DateTimeOffset? first) =>
        new(new SessionKey(ProviderId.Codex, id), "Synthetic title", folder, first, last, false, null, null, null, null);

    [Fact]
    public void TheSessionsOfTheFolderAreNewestFirstAndOtherFoldersAreLeftOut()
    {
        var sessions = new[]
        {
            Session("00000000-0000-7000-8000-000000000001", Folder, T0.AddHours(1), T0),
            Session("00000000-0000-7000-8000-000000000002", "c:\\work\\synthetic folder", T0.AddHours(3), T0),
            Session("00000000-0000-7000-8000-000000000003", "C:\\Work\\Synthetic Folder 2", T0.AddHours(5), T0),
            Session("00000000-0000-7000-8000-000000000004", null, T0.AddHours(6), T0),
            Session("00000000-0000-7000-8000-000000000005", Folder, T0.AddHours(2), T0),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        var expected = new[]
        {
            "00000000-0000-7000-8000-000000000002",
            "00000000-0000-7000-8000-000000000005",
            "00000000-0000-7000-8000-000000000001",
        };
        Assert.Equal(expected, candidates.Sessions.Select(s => s.Key.SessionId).ToArray());
        Assert.Equal("00000000-0000-7000-8000-000000000002", candidates.NewestId);
        Assert.Equal(1, candidates.TopRankCount);
    }

    [Fact]
    public void TheCountAtTheTopRankCountsTheSessionsSharingTheNewestTime()
    {
        var sessions = new[]
        {
            Session("00000000-0000-7000-8000-00000000000a", Folder, T0.AddHours(3), T0.AddHours(2)),
            Session("00000000-0000-7000-8000-00000000000b", Folder, T0.AddHours(3), T0),
            Session("00000000-0000-7000-8000-00000000000c", Folder, T0.AddHours(1), T0),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Equal(2, candidates.TopRankCount);
        Assert.Equal(3, candidates.Sessions.Count);
    }

    [Fact]
    public void TheEarliestOfTheTiedIsTheOneThatStartedFirst()
    {
        var sessions = new[]
        {
            Session("00000000-0000-7000-8000-00000000000a", Folder, T0.AddHours(3), T0.AddHours(2)),
            Session("00000000-0000-7000-8000-00000000000b", Folder, T0.AddHours(3), T0),
            Session("00000000-0000-7000-8000-00000000000c", Folder, T0.AddHours(1), T0.AddHours(-5)),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Equal("00000000-0000-7000-8000-00000000000b", candidates.EarliestTopRankId);
    }

    [Fact]
    public void ATiedCandidateWithAnUnknownStartIsNotTheEarliest()
    {
        var sessions = new[]
        {
            Session("00000000-0000-7000-8000-00000000000a", Folder, T0.AddHours(3), null),
            Session("00000000-0000-7000-8000-00000000000b", Folder, T0.AddHours(3), T0.AddHours(1)),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Equal(2, candidates.TopRankCount);
        Assert.Equal("00000000-0000-7000-8000-00000000000b", candidates.EarliestTopRankId);
    }

    [Fact]
    public void AnEqualStartIsBrokenByTheLowerId()
    {
        var sessions = new[]
        {
            Session("00000000-0000-7000-8000-00000000000b", Folder, T0.AddHours(3), T0),
            Session("00000000-0000-7000-8000-00000000000a", Folder, T0.AddHours(3), T0),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Equal("00000000-0000-7000-8000-00000000000a", candidates.EarliestTopRankId);
    }

    [Fact]
    public void ASessionWithNoLastActivityIsLeftOut()
    {
        var sessions = new[]
        {
            Session("00000000-0000-7000-8000-00000000000a", Folder, null, T0),
            Session("00000000-0000-7000-8000-00000000000b", Folder, T0.AddHours(1), T0),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Equal("00000000-0000-7000-8000-00000000000b", Assert.Single(candidates.Sessions).Key.SessionId);
        Assert.Equal(1, candidates.TopRankCount);
    }

    [Fact]
    public void TheSavedSessionIsLeftOutEvenWhenItIsTheNewest()
    {
        const string saved = "00000000-0000-7000-8000-00000000000a";
        var sessions = new[]
        {
            Session(saved, Folder, T0.AddHours(9), T0),
            Session("00000000-0000-7000-8000-00000000000b", Folder, T0.AddHours(1), T0),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder, saved);

        Assert.Equal("00000000-0000-7000-8000-00000000000b", candidates.NewestId);
        Assert.Equal(1, candidates.TopRankCount);
    }

    [Fact]
    public void ASessionIdThatAppearsTwiceIsCountedOnce()
    {
        const string id = "00000000-0000-7000-8000-00000000000a";
        var sessions = new[]
        {
            Session(id, Folder, T0.AddHours(2), T0),
            Session(id.ToUpperInvariant(), Folder, T0.AddHours(2), T0),
        };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Single(candidates.Sessions);
        Assert.Equal(1, candidates.TopRankCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankOrMissingFolderHasNoCandidates(string? folder)
    {
        var sessions = new[] { Session("00000000-0000-7000-8000-00000000000a", Folder, T0, T0) };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, folder);

        Assert.Empty(candidates.Sessions);
        Assert.Equal(0, candidates.TopRankCount);
        Assert.Null(candidates.NewestId);
        Assert.Null(candidates.EarliestTopRankId);
    }

    [Fact]
    public void NoSessionsInTheFolderGiveNoNewestAndNoEarliest()
    {
        var sessions = new[] { Session("00000000-0000-7000-8000-00000000000a", "C:\\Work\\Other", T0, T0) };

        var candidates = CodexHistoryQuery.NewestInFolderCandidates(sessions, Folder);

        Assert.Empty(candidates.Sessions);
        Assert.Equal(0, candidates.TopRankCount);
        Assert.Null(candidates.NewestId);
        Assert.Null(candidates.EarliestTopRankId);
    }
}

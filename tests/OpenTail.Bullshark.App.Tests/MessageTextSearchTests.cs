using OpenTail.Bullshark.Core.Providers;
using Xunit;
using System.IO;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// PP-D4 (2026-11-23): the message-text search. The matcher takes message texts as more fields, and the per-window cache reads each session once per
/// activity time. The cache is tested with a fake source, so no provider file is read here.
/// </summary>
public sealed class MessageTextSearchTests
{
    /// <summary>A source that counts its reads and answers from a function of the session id.</summary>
    private sealed class CountingSource(Func<string, IReadOnlyList<string>> texts) : IMessageTextSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(texts(sessionId));
        }
    }

    /// <summary>A source whose read fails with an I/O error.</summary>
    private sealed class FailingSource : IMessageTextSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct)
        {
            Calls++;
            return Task.FromException<IReadOnlyList<string>>(new IOException("the file is locked"));
        }
    }

    private static SessionSummary Session(string id, DateTimeOffset? lastActivity) =>
        new(new SessionKey(ProviderId.Codex, id), "A title", null, null, lastActivity, null, null, null, null, null);

    [Fact]
    public void AWordOnlyInAMessageMatches()
    {
        Assert.True(ProviderSearch.Matches("needle", "A title", null, null, null, new[] { "a needle in a message" }));
        Assert.False(ProviderSearch.Matches("needle", "A title", null, null, null, new[] { "nothing here" }));
    }

    [Fact]
    public void WordsMaySplitBetweenTheTitleAndAMessage()
    {
        Assert.True(ProviderSearch.Matches("title needle", "A title", null, null, null, new[] { "needle" }));
    }

    [Fact]
    public void NoMessagesKeepsTheOldRule()
    {
        Assert.False(ProviderSearch.Matches("needle", "A title", null, null, null, null));
        Assert.True(ProviderSearch.Matches("title", "A title", null, null, null, null));
    }

    [Fact]
    public async Task TheTextsAreReadOnceForAnUnchangedSession()
    {
        var cache = new MessageTextCache();
        var session = Session("s1", DateTimeOffset.UnixEpoch.AddHours(1));
        var source = new CountingSource(_ => new[] { "one", "two" });

        Assert.Empty(cache.Known(new[] { session }));
        var first = await cache.LoadAllAsync(new[] { session }, source, CancellationToken.None);
        await cache.LoadAllAsync(new[] { session }, source, CancellationToken.None);

        Assert.Equal(1, source.Calls);
        Assert.Equal(new[] { "one", "two" }, first["s1"]);
        Assert.Equal(new[] { "one", "two" }, cache.Known(new[] { session })["s1"]);
    }

    [Fact]
    public async Task TheTextsAreReadAgainWhenTheActivityMoves()
    {
        var cache = new MessageTextCache();
        var source = new CountingSource(_ => new[] { "one" });

        await cache.LoadAllAsync(new[] { Session("s1", DateTimeOffset.UnixEpoch.AddHours(1)) }, source, CancellationToken.None);
        var moved = Session("s1", DateTimeOffset.UnixEpoch.AddHours(2));
        Assert.Empty(cache.Known(new[] { moved }));
        await cache.LoadAllAsync(new[] { moved }, source, CancellationToken.None);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task AFailedReadIsEmptyAndNotRetriedForTheSameActivity()
    {
        var cache = new MessageTextCache();
        var session = Session("s1", DateTimeOffset.UnixEpoch.AddHours(1));
        var source = new FailingSource();

        var first = await cache.LoadAllAsync(new[] { session }, source, CancellationToken.None);
        await cache.LoadAllAsync(new[] { session }, source, CancellationToken.None);

        Assert.Empty(first["s1"]);
        Assert.Equal(1, source.Calls);
    }
}

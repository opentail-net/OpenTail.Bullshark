using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Stage C1 and C6 (a)-(f), for both targets. The content tests need the compressed reader, so they are in
/// <see cref="CodexHistorySourceReadingTests"/> (net11.0 only); here are the tests that hold on net10.0 too: the empty list, the cancellation,
/// and the compressed fixture (empty on net10.0, read on net11.0).
/// </summary>
public sealed class CodexHistorySourceTests : IDisposable
{
    private readonly CodexHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task AMissingHistoryRootGivesAnEmptyList()
    {
        // The home exists, but it has no sessions folder (a Codex that was never run here).
        var source = new CodexHistorySource(_home.Paths());

        var sessions = await source.ListAsync(CancellationToken.None);

        Assert.Empty(sessions);
        Assert.Equal(0, source.SkippedFiles);
        Assert.Equal(0, source.SkippedLines);
    }

    [Fact]
    public async Task ACompressedRolloutIsReadOnlyOnNet11AndEmptyOnNet10()
    {
        // C-D2: a compressed rollout is read on net11.0 only. On net10.0 the list is empty, whatever the files hold.
#if NET11_0_OR_GREATER
        _home.WriteCompressed(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var source = new CodexHistorySource(_home.Paths());

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(new SessionKey(ProviderId.Codex, CodexHome.SessionId), session.Key);
        Assert.Equal(CodexHome.FirstMessage, session.Title);
        Assert.Equal(2, session.UserMessages);
        Assert.Equal(1, session.AssistantMessages);
        Assert.Equal(0, source.SkippedFiles);
        Assert.Equal(1, source.SkippedLines);
#else
        _home.WritePlain("rollout-compressed-on-net10.jsonl.zst", CodexHome.Standard(CodexHome.SessionId));
        var source = new CodexHistorySource(_home.Paths());

        Assert.Empty(await source.ListAsync(CancellationToken.None));
        Assert.Equal(0, source.SkippedFiles);
#endif
    }

    [Fact]
    public async Task APlainRolloutIsListedOnNet11AndNotOnNet10()
    {
        // C-D2 applies to the whole history source: on net10.0 even a plain rollout gives no sessions.
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var source = new CodexHistorySource(_home.Paths());

        var sessions = await source.ListAsync(CancellationToken.None);

#if NET11_0_OR_GREATER
        Assert.Single(sessions);
#else
        Assert.Empty(sessions);
#endif
    }

    [Fact]
    public async Task ACancelledListingThrows()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var source = new CodexHistorySource(_home.Paths());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListAsync(cancelled.Token));
    }
}

/// <summary>
/// Stage C1 and C6 (a), (c), (d), (e): the content of the summaries, read from synthetic rollouts. The reader needs the net11.0 build for
/// compressed files; the plain files here are read in the same way, and the whole class is net11.0 only because C-D2 makes the history empty on net10.0.
/// </summary>
#if NET11_0_OR_GREATER
public sealed class CodexHistorySourceReadingTests : IDisposable
{
    private readonly CodexHome _home = new();

    public void Dispose() => _home.Dispose();

    private CodexHistorySource Source() => new(_home.Paths());

    [Fact]
    public async Task APlainRolloutGivesOneSummaryWithItsFields()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(new SessionKey(ProviderId.Codex, CodexHome.SessionId), session.Key);
        Assert.Equal(CodexHome.FirstMessage, session.Title);
        Assert.Equal("C:\\work\\demo", session.Folder);
        Assert.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), session.FirstActivity);
        Assert.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 11, 20, 10, 0, 6, TimeSpan.Zero), session.LastActivity);
        Assert.Null(session.IsSubagent);
        Assert.Equal(2, session.UserMessages);
        Assert.Equal(1, session.AssistantMessages);
        Assert.Equal(0, session.Compactions);
        Assert.Equal("gpt-test-1", session.Model);
        // The one line that is not JSON is counted; the unknown record type is not an error and is not a skipped line.
        Assert.Equal(1, source.SkippedLines);
        Assert.Equal(0, source.SkippedFiles);
    }

    [Fact]
    public async Task TheIndexNameWinsOverTheFirstUserMessage()
    {
        _home.WriteIndex(
            "{\"id\":\"" + CodexHome.SessionId + "\",\"thread_name\":\"Named by the index\",\"updated_at\":\"2026-11-20T10:05:00Z\"}");
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal("Named by the index", session.Title);
    }

    [Fact]
    public async Task WithoutAnIndexNameTheTitleIsTheFirstUserTextCutAt80Characters()
    {
        var longText = new string('a', 100);
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Message("2026-11-20T10:00:02.000Z", "user", longText),
        });

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal(new string('a', CodexHistorySource.MaxTitleLength), session.Title);
    }

    [Fact]
    public async Task TheFirstUserTextHasItsWhiteSpaceMadeSingle()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Message("2026-11-20T10:00:02.000Z", "user", "Line one\\nLine   two"),
        });

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal("Line one Line two", session.Title);
    }

    [Fact]
    public async Task ASessionWithoutAnIdUsesTheUuidInItsFileName()
    {
        // No id in the session record: the key is the UUID at the end of the file name.
        _home.WritePlain(CodexHome.NameFor(CodexHome.NoIdUuid), CodexHome.Standard(null));

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal(new SessionKey(ProviderId.Codex, CodexHome.NoIdUuid), session.Key);
    }

    [Fact]
    public async Task AnUnreadableFileIsSkippedAndCountedAndTheOthersAreListed()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        // Random bytes that are neither zstd nor JSON, once as a compressed name and once as a plain one.
        var garbage = new byte[] { 0x00, 0xFF, 0x42, 0x13, 0x37, 0x80, 0x01 };
        var badCompressed = _home.PathFor("rollout-2026-11-20T10-00-00-" + CodexHome.SecondId + ".jsonl.zst");
        Directory.CreateDirectory(Path.GetDirectoryName(badCompressed)!);
        File.WriteAllBytes(badCompressed, garbage);
        _home.WritePlain("rollout-2026-11-20T10-00-01-" + CodexHome.NoIdUuid + ".jsonl", new[] { "garbage", "more garbage" });
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(new SessionKey(ProviderId.Codex, CodexHome.SessionId), session.Key);
        Assert.Equal(2, source.SkippedFiles);
        // The good rollout has its own one non-JSON line (see CodexHome.Standard); the two bad files add none.
        Assert.Equal(1, source.SkippedLines);
    }
}
#endif

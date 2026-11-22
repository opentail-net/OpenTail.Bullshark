using System.Collections;
using System.Text;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Stage C1 and C6 (a)-(f), for both targets: the empty list, the cancellation, and the compressed fixture. The content tests are in
/// <see cref="CodexHistorySourceReadingTests"/>.
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
    public async Task ACompressedRolloutIsRead()
    {
        _home.WriteCompressed(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var source = new CodexHistorySource(_home.Paths());

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal(new SessionKey(ProviderId.Codex, CodexHome.SessionId), session.Key);
        Assert.Equal(CodexHome.FirstMessage, session.Title);
        Assert.Equal(2, session.UserMessages);
        Assert.Equal(1, session.AssistantMessages);
        Assert.Equal(0, source.SkippedFiles);
        Assert.Equal(1, source.SkippedLines);
    }

    [Fact]
    public async Task APlainRolloutIsListed()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var source = new CodexHistorySource(_home.Paths());

        var sessions = await source.ListAsync(CancellationToken.None);

        Assert.Single(sessions);
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

    [Fact]
    public void ACancelledLineReaderStopsBetweenLines()
    {
        // F-D: the token is checked before each line, not only before each file. Cancelling after the first line makes the next step throw.
        // RolloutReader is internal and Core has no InternalsVisibleTo, so it is reached by reflection (as IndexCacheFieldTests does).
        var reader = typeof(CodexHistorySource).Assembly.GetType("OpenTail.Bullshark.Core.Codex.RolloutReader", throwOnError: true)!;
        using var cancellation = new CancellationTokenSource();
        var lines = (IEnumerable)reader.GetMethod("Lines")!.Invoke(null, new object[] { new StringReader("one\ntwo\nthree\n"), cancellation.Token })!;
        var enumerator = lines.GetEnumerator();

        Assert.True(enumerator.MoveNext());
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
    }
}

/// <summary>
/// Stage C1 and C6 (a), (c), (d), (e): the content of the summaries, read from synthetic rollouts, on both targets.
/// </summary>
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
    public async Task InjectedContextIsNeitherTheTitleNorACountedMessage()
    {
        // R-D5: injected context at the start of a session is not the owner's message. The title and the count leave it out, as the summary and the
        // start text do (synthetic text only).
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Message("2026-11-20T10:00:02.000Z", "user", "<environment_context>synthetic context</environment_context>"),
            CodexHome.Message("2026-11-20T10:00:03.000Z", "user", "The real question"),
            CodexHome.Message("2026-11-20T10:00:04.000Z", "assistant", "An answer"),
        });

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal("The real question", session.Title);
        Assert.Equal(1, session.UserMessages);
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

    [Fact]
    public async Task AnIndexNameWithANewLineAndLongTextIsOneLineCutAt80Characters()
    {
        // F-B: the index name gets the first-message rule, white space made single and then cut to 80 characters.
        _home.WriteIndex("{\"id\":\"" + CodexHome.SessionId + "\",\"thread_name\":\"First\\nSecond " + new string('b', 200) + "\"}");
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        var expected = "First Second " + new string('b', CodexHistorySource.MaxTitleLength - "First Second ".Length);
        Assert.Equal(expected, session.Title);
        Assert.DoesNotContain('\n', session.Title!);
        Assert.True(session.Title!.Length <= CodexHistorySource.MaxTitleLength);
    }

    [Fact]
    public async Task ACutThatWouldSplitAnEmojiLeavesNoLoneHighSurrogate()
    {
        // F-C: an emoji (a surrogate pair) at positions 79 and 80. A cut at 80 would leave the high surrogate alone at the end.
        var text = new string('a', 79) + "\U0001F600" + "bbbb";
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Message("2026-11-20T10:00:02.000Z", "user", text),
        });

        var session = Assert.Single(await Source().ListAsync(CancellationToken.None));

        Assert.Equal(new string('a', 79), session.Title);
        Assert.False(char.IsHighSurrogate(session.Title![^1]));
    }

    [Fact]
    public async Task ALineLongerThanTheCapIsSkippedAndCountedAndTheLinesAfterItAreRead()
    {
        // F-D: a line of 16 MB + 1 characters is discarded unread and counted as a skipped line. The message in it must not be counted, and
        // the line after it is read. The cap is RolloutReader.MaxLineLength (16 MB, in characters).
        const int cap = 16 * 1024 * 1024;
        const string prefix = "{\"timestamp\":\"2026-11-20T10:00:02.000Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"";
        const string suffix = "\"}]}}";
        var path = _home.PathFor(CodexHome.NameFor(CodexHome.SessionId));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.WriteLine(CodexHome.Meta(CodexHome.SessionId));
            writer.Write(prefix);
            var block = new string('x', 1024 * 1024);
            var remaining = cap + 1 - prefix.Length - suffix.Length;
            while (remaining > 0)
            {
                var take = Math.Min(block.Length, remaining);
                writer.Write(block.AsSpan(0, take));
                remaining -= take;
            }
            writer.WriteLine(suffix);
            writer.WriteLine(CodexHome.Message("2026-11-20T10:00:06.000Z", "user", "After the long line"));
        }
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal("After the long line", session.Title);
        Assert.Equal(1, session.UserMessages);
        Assert.Equal(1, source.SkippedLines);
        Assert.Equal(0, source.SkippedFiles);
    }

    [Fact]
    public async Task ATruncatedCompressedRolloutDoesNotStopTheListing()
    {
        // F-E (a): the first half of a valid compressed rollout ends in the middle of a frame. The listing must not throw; the file is either
        // skipped and counted, or listed with the counts it had. The good rollout is listed either way.
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var truncatedPath = _home.WriteCompressed(CodexHome.NameFor(CodexHome.SecondId), CodexHome.Standard(CodexHome.SecondId));
        var whole = File.ReadAllBytes(truncatedPath);
        File.WriteAllBytes(truncatedPath, whole[..(whole.Length / 2)]);
        var source = Source();

        var sessions = await source.ListAsync(CancellationToken.None);

        Assert.Contains(sessions, session => session.Key == new SessionKey(ProviderId.Codex, CodexHome.SessionId));
        Assert.Equal(2, sessions.Count + source.SkippedFiles);
    }

    [Fact]
    public async Task AMalformedLineInTheMiddleIsSkippedAndTheLinesAroundItAreRead()
    {
        // F-E (b): one line of JSON cut short sits between two good records. It is counted once; both neighbours are read.
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Message("2026-11-20T10:00:02.000Z", "user", "Before the bad line"),
            "{\"timestamp\":\"2026-11-20T10:00:03.000Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"Broken",
            CodexHome.Message("2026-11-20T10:00:04.000Z", "assistant", "After the bad line"),
        });

        var source = Source();
        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal("Before the bad line", session.Title);
        Assert.Equal(1, session.UserMessages);
        Assert.Equal(1, session.AssistantMessages);
        Assert.Equal(1, source.SkippedLines);
        Assert.Equal(0, source.SkippedFiles);
    }

    [Fact]
    public async Task AnUnknownRecordTypeIsIgnoredAndIsNotASkippedLine()
    {
        // F-E (c): a record type the reader does not know changes nothing in the counts and is not a skipped line.
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Message("2026-11-20T10:00:02.000Z", "user", "Hello"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "record_type_not_known_yet", "{\"x\":[1,2]}"),
            CodexHome.Message("2026-11-20T10:00:04.000Z", "assistant", "Hi"),
        });
        var source = Source();

        var session = Assert.Single(await source.ListAsync(CancellationToken.None));

        Assert.Equal("Hello", session.Title);
        Assert.Equal(1, session.UserMessages);
        Assert.Equal(1, session.AssistantMessages);
        Assert.Equal(0, source.SkippedLines);
        Assert.Equal(0, source.SkippedFiles);
    }
}

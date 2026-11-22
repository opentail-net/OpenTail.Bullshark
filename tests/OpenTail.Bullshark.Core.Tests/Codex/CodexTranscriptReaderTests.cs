using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>Stage C2 (Core part) and C6 (g): paging, clamping and refused ids of a Codex rollout. Plain fixtures run on both targets.</summary>
public sealed class CodexTranscriptReaderTests : IDisposable
{
    private readonly CodexHome _home = new();

    public void Dispose() => _home.Dispose();

    private CodexTranscriptReader Reader() => new(_home.Paths());

    [Fact]
    public async Task APageReturnsTheLinesFromTheFirstLineAndTheCount()
    {
        var path = _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var all = File.ReadAllLines(path);

        var page = await Reader().ReadPageAsync(CodexHome.SessionId, 1, 2, CancellationToken.None);

        Assert.Equal(all[1..3], page.Lines);
        Assert.Equal(1, page.FirstLine);
        Assert.Null(page.TotalLines);
    }

    [Fact]
    public async Task ThePageIsClampedToTheFileLength()
    {
        var path = _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var total = File.ReadAllLines(path).Length;
        var reader = Reader();

        // Past the end: no lines, and the first line is the length of the file.
        var past = await reader.ReadPageAsync(CodexHome.SessionId, 999, 5, CancellationToken.None);
        Assert.Empty(past.Lines);
        Assert.Equal(total, past.FirstLine);

        // A page that runs past the end gives the lines that exist.
        var tail = await reader.ReadPageAsync(CodexHome.SessionId, total - 2, 100, CancellationToken.None);
        Assert.Equal(2, tail.Lines.Count);
        Assert.Equal(total - 2, tail.FirstLine);

        // A negative first line starts at zero; a negative count gives no lines.
        var negativeFirst = await reader.ReadPageAsync(CodexHome.SessionId, -5, 1, CancellationToken.None);
        Assert.Equal(0, negativeFirst.FirstLine);
        Assert.Single(negativeFirst.Lines);
        var negativeCount = await reader.ReadPageAsync(CodexHome.SessionId, 2, -1, CancellationToken.None);
        Assert.Empty(negativeCount.Lines);
        Assert.Equal(2, negativeCount.FirstLine);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("019e*")]
    [InlineData("a?b")]
    [InlineData("..")]
    [InlineData("x/../y")]
    [InlineData("x\\y")]
    [InlineData("C:evil")]
    [InlineData("")]
    public async Task AnIdWithAWildcardSeparatorColonOrDotDotIsRefused(string sessionId)
    {
        // Such an id would make the search match another file, so it is refused before any file is looked at.
        await Assert.ThrowsAsync<ArgumentException>(() => Reader().ReadPageAsync(sessionId, 0, 1, CancellationToken.None));
    }

    [Fact]
    public async Task AnIdWithNoRolloutThrowsFileNotFound()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Reader().ReadPageAsync(CodexHome.NoIdUuid, 0, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ARolloutWhoseNameHasNoIdIsFoundByTheIdInItsSessionRecord()
    {
        // The file name has no UUID of the session; the session record names the id, and the search falls back to it.
        _home.WritePlain("rollout-no-uuid-in-name.jsonl", CodexHome.Standard(CodexHome.SecondId));

        var page = await Reader().ReadPageAsync(CodexHome.SecondId, 0, 1, CancellationToken.None);

        var line = Assert.Single(page.Lines);
        Assert.Contains(CodexHome.SecondId, line);
    }

    [Fact]
    public async Task ACompressedRolloutIsPaged()
    {
        // The compressed file is decoded as a stream, and its lines are the same as the plain file's. Both targets decode it (Z1).
        var lines = CodexHome.Standard(CodexHome.SessionId);
        _home.WriteCompressed(CodexHome.NameFor(CodexHome.SessionId), lines);

        var page = await Reader().ReadPageAsync(CodexHome.SessionId, 2, 3, CancellationToken.None);

        Assert.Equal(lines[2..5], page.Lines);
        Assert.Equal(2, page.FirstLine);
    }
}

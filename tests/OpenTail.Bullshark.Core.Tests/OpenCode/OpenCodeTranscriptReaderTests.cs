using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1b (OC-D5): the transcript lines of a synthetic v1 database. The order, the prefixes, the synthetic and ignored parts, the tool parts, the
/// page slice, the cap, and the invalid id (which gives an empty page before any query). Every text is made up.
/// </summary>
public sealed class OpenCodeTranscriptReaderTests : IDisposable
{
    private readonly OpenCodeFixture _fixture = new();

    private static readonly string[] RootOneLines =
    [
        "you: fixture text one",
        "opencode: fixture reply one",
        "you: fixture text two",
        "you: fixture ignored text",
    ];

    public void Dispose() => _fixture.Dispose();

    private OpenCodeTranscriptReader Reader() => new(_fixture.Location);

    [Fact]
    public async Task TheLinesAreInOrderWithTheirPrefixes()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.Equal(RootOneLines, page.Lines);
        Assert.Equal(0, page.FirstLine);
        Assert.Null(page.TotalLines);
    }

    [Fact]
    public async Task ASyntheticPartIsLeftOut()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.DoesNotContain(page.Lines, line => line.Contains("synthetic note", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnIgnoredPartIsKept()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.Contains("you: fixture ignored text", page.Lines);
    }

    [Fact]
    public async Task AToolPartAndAStepPartAreLeftOut()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.DoesNotContain(page.Lines, line => line.Contains("fixture-tool", StringComparison.Ordinal));
        Assert.Equal(RootOneLines.Length, page.Lines.Count);
    }

    [Fact]
    public async Task ASystemMessageIsSkipped()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.DoesNotContain(page.Lines, line => line.Contains("fixture system text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APageReturnsTheRightSlice()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 1, 2, CancellationToken.None);

        Assert.Equal(new[] { RootOneLines[1], RootOneLines[2] }, page.Lines);
        Assert.Equal(1, page.FirstLine);
        Assert.Null(page.TotalLines);
    }

    [Fact]
    public async Task AStartPastTheEndGivesNoLinesAndTheClampedFirstLine()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 10, 3, CancellationToken.None);

        Assert.Empty(page.Lines);
        Assert.Equal(RootOneLines.Length, page.FirstLine);
    }

    [Fact]
    public async Task ANegativeStartOrCountGivesTheFirstLinesOrNone()
    {
        _fixture.CreateStandard();
        var reader = Reader();

        var fromNegativeStart = await reader.ReadPageAsync(OpenCodeFixture.RootOne, -3, 1, CancellationToken.None);
        var negativeCount = await reader.ReadPageAsync(OpenCodeFixture.RootOne, 0, -1, CancellationToken.None);

        Assert.Equal(new[] { RootOneLines[0] }, fromNegativeStart.Lines);
        Assert.Equal(0, fromNegativeStart.FirstLine);
        Assert.Empty(negativeCount.Lines);
    }

    [Fact]
    public async Task TheCapCutsTheTranscriptAtTheSharedLimit()
    {
        _fixture.CreateStandard();
        const int extra = 3;
        _fixture.AddLongSession(TranscriptLimits.MaxMessages + extra);

        var page = await Reader().ReadPageAsync(OpenCodeFixture.CapRoot, 0, int.MaxValue, CancellationToken.None);

        Assert.Equal(OpenCodeTranscriptReader.MaxLines, page.Lines.Count);
        Assert.Equal(TranscriptLimits.MaxMessages, OpenCodeTranscriptReader.MaxLines);
        Assert.Equal("you: fixture part 1", page.Lines[0]);
        Assert.Equal($"you: fixture part {TranscriptLimits.MaxMessages}", page.Lines[^1]);
    }

    [Fact]
    public async Task TheResumeReadGivesEveryLineOfALongSessionInOrder()
    {
        _fixture.CreateStandard();
        const int total = TranscriptLimits.MaxMessages + 3;
        _fixture.AddLongSession(total);

        var page = await new OpenCodeTranscriptReader(_fixture.Location, int.MaxValue)
            .ReadPageAsync(OpenCodeFixture.CapRoot, 0, int.MaxValue, CancellationToken.None);

        Assert.Equal(total, page.Lines.Count);
        Assert.Equal(Enumerable.Range(1, total).Select(i => $"you: fixture part {i}"), page.Lines);
    }

    [Fact]
    public async Task TheResumeReadsLastLineIsTheSessionsLastMessage()
    {
        _fixture.CreateStandard();
        const int total = TranscriptLimits.MaxMessages + 3;
        _fixture.AddLongSession(total);

        var page = await new OpenCodeTranscriptReader(_fixture.Location, int.MaxValue)
            .ReadPageAsync(OpenCodeFixture.CapRoot, 0, int.MaxValue, CancellationToken.None);

        Assert.Equal($"you: fixture part {total}", page.Lines[^1]);
    }

    [Fact]
    public async Task TheViewReaderStillStopsAtTheSharedLimitOnTheSameSession()
    {
        _fixture.CreateStandard();
        const int total = TranscriptLimits.MaxMessages + 3;
        _fixture.AddLongSession(total);

        var view = await Reader().ReadPageAsync(OpenCodeFixture.CapRoot, 0, int.MaxValue, CancellationToken.None);
        var resume = await new OpenCodeTranscriptReader(_fixture.Location, int.MaxValue)
            .ReadPageAsync(OpenCodeFixture.CapRoot, 0, int.MaxValue, CancellationToken.None);

        Assert.Equal(TranscriptLimits.MaxMessages, view.Lines.Count);
        Assert.Equal($"you: fixture part {TranscriptLimits.MaxMessages}", view.Lines[^1]);
        Assert.Equal(total, resume.Lines.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ses_not-a-valid-id")]
    [InlineData("ses_0a1b2c3d4e5fAbCdEfGhIjKl' OR 1=1 --")]
    public async Task AnInvalidIdGivesAnEmptyPage(string sessionId)
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(sessionId, 0, 100, CancellationToken.None);

        Assert.Empty(page.Lines);
        Assert.Equal(0, page.FirstLine);
        Assert.Null(page.TotalLines);
    }

    [Fact]
    public async Task AMissingDatabaseGivesAnEmptyPageAndCreatesNothing()
    {
        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootOne, 0, 100, CancellationToken.None);

        Assert.Empty(page.Lines);
        Assert.False(File.Exists(_fixture.Database));
    }

    [Fact]
    public async Task ASessionWithNoMessagesGivesAnEmptyPage()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.ArchivedRoot, 0, 100, CancellationToken.None);

        Assert.Empty(page.Lines);
    }

    [Fact]
    public async Task AnotherSessionsMessagesAreNotIncluded()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.RootTwo, 0, 100, CancellationToken.None);

        Assert.Equal(new[] { "you: fixture root two text" }, page.Lines);
    }

    [Fact]
    public async Task AnUnknownButValidIdGivesAnEmptyPage()
    {
        _fixture.CreateStandard();

        var page = await Reader().ReadPageAsync(OpenCodeFixture.UnknownId, 0, 100, CancellationToken.None);

        Assert.Empty(page.Lines);
    }
}

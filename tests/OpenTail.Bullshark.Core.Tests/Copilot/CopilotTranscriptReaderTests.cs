using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>Stage G1.3: the messages of a Copilot session, in order, with paging, clamping, refused ids and the 5,000 cap. Plain fixtures.</summary>
public sealed class CopilotTranscriptReaderTests : IDisposable
{
    private readonly CopilotHome _home = new();

    public void Dispose() => _home.Dispose();

    private CopilotTranscriptReader Reader() => new(_home.Paths());

    [Fact]
    public void TheMessagesAreInOrderAsYouAndCopilotLinesWithTheirKinds()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));

        var messages = CopilotTranscriptReader.ReadSession(_home.Paths(), CopilotHome.SessionId, CancellationToken.None);

        Assert.Equal(new[] { "you", "copilot", "you", "copilot" }, messages.Select(m => m.Kind).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, messages.Select(m => m.Number).ToArray());
        Assert.Equal(
            new[] { CopilotHome.FirstMessage, "Done", "Second question", "Answer two" },
            messages.Select(m => m.Text).ToArray());
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 2, TimeSpan.Zero), messages[0].Time);
    }

    [Fact]
    public async Task APageReturnsTheMessagesFromTheFirstOneAsKindAndText()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));

        var page = await Reader().ReadPageAsync(CopilotHome.SessionId, 1, 2, CancellationToken.None);

        Assert.Equal(new[] { "copilot: Done", "you: Second question" }, page.Lines);
        Assert.Equal(1, page.FirstLine);
        Assert.Null(page.TotalLines);
    }

    [Fact]
    public async Task ThePageIsClampedToTheMessageCountAndANegativeCountGivesNoLines()
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));
        var reader = Reader();

        var past = await reader.ReadPageAsync(CopilotHome.SessionId, 999, 5, CancellationToken.None);
        Assert.Empty(past.Lines);
        Assert.Equal(4, past.FirstLine);

        var tail = await reader.ReadPageAsync(CopilotHome.SessionId, 2, 100, CancellationToken.None);
        Assert.Equal(2, tail.Lines.Count);
        Assert.Equal(2, tail.FirstLine);

        var negativeFirst = await reader.ReadPageAsync(CopilotHome.SessionId, -5, 1, CancellationToken.None);
        Assert.Equal(0, negativeFirst.FirstLine);
        Assert.Single(negativeFirst.Lines);

        var negativeCount = await reader.ReadPageAsync(CopilotHome.SessionId, 2, -1, CancellationToken.None);
        Assert.Empty(negativeCount.Lines);
        Assert.Equal(2, negativeCount.FirstLine);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("3f2a*")]
    [InlineData("a?b")]
    [InlineData("..")]
    [InlineData("x/../y")]
    [InlineData("x\\y")]
    [InlineData("C:evil")]
    [InlineData("")]
    public async Task AnIdWithAWildcardSeparatorColonOrDotDotIsRefused(string sessionId)
    {
        _home.WriteEvents(CopilotHome.SessionId, CopilotHome.Standard(CopilotHome.SessionId));

        await Assert.ThrowsAsync<ArgumentException>(() => Reader().ReadPageAsync(sessionId, 0, 1, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => CopilotTranscriptReader.ReadSession(_home.Paths(), "*", CancellationToken.None));
    }

    [Fact]
    public async Task ASessionWithNoEventsFileThrowsFileNotFound()
    {
        _home.WriteWorkspace(CopilotHome.SessionId, CopilotHome.Workspace(CopilotHome.SessionId));

        await Assert.ThrowsAsync<FileNotFoundException>(() => Reader().ReadPageAsync(CopilotHome.SessionId, 0, 1, CancellationToken.None));
    }

    [Fact]
    public void ASkippedKindAndABadLineAndAMessageWithNoTextAreLeftOut()
    {
        _home.WriteEvents(CopilotHome.SessionId,
            CopilotHome.SessionStart(CopilotHome.SessionId),
            "not json at all",
            CopilotHome.SystemMessage("2026-11-20T10:00:01.000Z"),
            CopilotHome.TurnEnd("2026-11-20T10:00:02.000Z"),
            CopilotHome.Event("user.message", "2026-11-20T10:00:03.000Z", "{\"transformedContent\":\"no content key\"}"),
            CopilotHome.AssistantMessage("2026-11-20T10:00:04.000Z", "   "),
            CopilotHome.UserMessage("2026-11-20T10:00:05.000Z", "Kept"));

        var messages = CopilotTranscriptReader.ReadSession(_home.Paths(), CopilotHome.SessionId, CancellationToken.None);

        var message = Assert.Single(messages);
        Assert.Equal("you", message.Kind);
        Assert.Equal("Kept", message.Text);
    }

    [Fact]
    public void TheTranscriptShowsAtMostTheFirstFiveThousandMessagesButASummaryCanReadAll()
    {
        var lines = new List<string> { CopilotHome.SessionStart(CopilotHome.SessionId) };
        for (var i = 1; i <= CopilotTranscriptReader.MaxMessages + 1; i++)
        {
            lines.Add(CopilotHome.UserMessage("2026-11-20T10:00:00.000Z", "message " + i));
        }
        _home.WriteEvents(CopilotHome.SessionId, lines.ToArray());

        var capped = CopilotTranscriptReader.ReadSession(_home.Paths(), CopilotHome.SessionId, CancellationToken.None);
        var all = CopilotTranscriptReader.ReadSession(_home.Paths(), CopilotHome.SessionId, CancellationToken.None, int.MaxValue);

        Assert.Equal(CopilotTranscriptReader.MaxMessages, capped.Count);
        Assert.Equal(CopilotTranscriptReader.MaxMessages + 1, all.Count);
        Assert.Equal("message " + (CopilotTranscriptReader.MaxMessages + 1), all[^1].Text);
    }
}

using System.Globalization;
using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The pure parts of the Copilot transcript view (parity P7): the plain-text copy, the cap, the count and cap lines, the scope line and the time label.
/// No window is made.
/// </summary>
public sealed class CopilotTranscriptTextTests
{
    private static TranscriptLine Line(int number, string kind, string text, DateTimeOffset? time = null) =>
        new(number, kind, time, text, Collapsed: false);

    [Fact]
    public void TheCopyLabelsEachMessageAndSeparatesTheBlocksWithABlankLine()
    {
        var lines = new[] { Line(1, "you", "Tidy the notes."), Line(2, "copilot", "The notes are tidy.") };
        var blank = Environment.NewLine + Environment.NewLine;

        Assert.Equal("you: Tidy the notes." + blank + "copilot: The notes are tidy.", CopilotTranscriptText.PlainText(lines));
    }

    [Fact]
    public void TheCopyKeepsTheLineBreaksOfAMessage()
    {
        var lines = new[] { Line(1, "copilot", "First line." + Environment.NewLine + "Second line.") };

        Assert.Equal("copilot: First line." + Environment.NewLine + "Second line.", CopilotTranscriptText.PlainText(lines));
    }

    [Fact]
    public void TheCopyOfNoMessagesIsEmpty()
    {
        Assert.Equal(string.Empty, CopilotTranscriptText.PlainText(Array.Empty<TranscriptLine>()));
    }

    [Fact]
    public void TheCapKeepsTheFirstFiveThousandMessagesInOrder()
    {
        var lines = Enumerable.Range(1, 5001).Select(n => Line(n, n % 2 == 0 ? "copilot" : "you", "message " + n)).ToList();

        var shown = CopilotTranscriptText.Capped(lines);

        Assert.Equal(5000, CopilotTranscriptText.MaxMessages);
        Assert.Equal(5000, shown.Count);
        Assert.Equal(1, shown[0].Number);
        Assert.Equal(5000, shown[^1].Number);
    }

    [Fact]
    public void ASessionAtOrUnderTheCapIsNotCut()
    {
        var lines = Enumerable.Range(1, CopilotTranscriptText.MaxMessages).Select(n => Line(n, "you", "m" + n)).ToList();

        Assert.Same(lines, CopilotTranscriptText.Capped(lines));
    }

    [Fact]
    public void TheCapLineSaysHowManyOfHowManyAreShown()
    {
        Assert.Equal("Showing the first 5,000 of 5,001 messages.", CopilotTranscriptText.CapNote(5001, 5000));
        Assert.Equal("Showing the first 5,000 of 12,345 messages.", CopilotTranscriptText.CapNote(12345, 5000));
    }

    [Fact]
    public void TheCapLineIsAbsentWhenEveryMessageIsShown()
    {
        Assert.Null(CopilotTranscriptText.CapNote(5000, 5000));
        Assert.Null(CopilotTranscriptText.CapNote(4, 4));
        Assert.Null(CopilotTranscriptText.CapNote(0, 0));
    }

    [Fact]
    public void TheCountTextSaysZeroOneOrManyMessages()
    {
        Assert.Equal("0 messages", CopilotTranscriptText.MessageCount(0));
        Assert.Equal("1 message", CopilotTranscriptText.MessageCount(1));
        Assert.Equal("4 messages", CopilotTranscriptText.MessageCount(4));
        Assert.Equal("5,001 messages", CopilotTranscriptText.MessageCount(5001));
    }

    [Fact]
    public void TheScopeLineSaysThatOnlyMessagesAreShown()
    {
        Assert.Equal("Messages only. Tool calls and their output are not shown.", CopilotTranscriptText.ScopeLine);
    }

    [Fact]
    public void TheTimeLabelIsEmptyWhenTheRecordHasNoTimeAndLocalShortFormOtherwise()
    {
        Assert.Equal(string.Empty, CopilotTranscriptText.TimeLabel(null));

        var at = new DateTimeOffset(2026, 11, 21, 9, 5, 0, TimeSpan.Zero);
        var expected = at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal(expected, CopilotTranscriptText.TimeLabel(at));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", CopilotTranscriptText.TimeLabel(at));
    }
}

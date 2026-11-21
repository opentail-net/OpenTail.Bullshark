using System.Globalization;
using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The pure parts of the Codex transcript view (parity P7): the plain-text copy, the cap, the count and cap lines, the scope line and the time label.
/// No window is made.
/// </summary>
public sealed class CodexTranscriptTextTests
{
    private static TranscriptLine Line(int number, string kind, string text, DateTimeOffset? time = null) =>
        new(number, kind, time, text, Collapsed: false);

    [Fact]
    public void TheCopyLabelsEachMessageAndSeparatesTheBlocksWithABlankLine()
    {
        var lines = new[] { Line(1, "you", "Tidy the notes."), Line(2, "codex", "The notes are tidy.") };
        var blank = Environment.NewLine + Environment.NewLine;

        Assert.Equal("you: Tidy the notes." + blank + "codex: The notes are tidy.", CodexTranscriptText.PlainText(lines));
    }

    [Fact]
    public void TheCopyKeepsTheLineBreaksOfAMessage()
    {
        var lines = new[] { Line(1, "codex", "First line." + Environment.NewLine + "Second line.") };

        Assert.Equal("codex: First line." + Environment.NewLine + "Second line.", CodexTranscriptText.PlainText(lines));
    }

    [Fact]
    public void TheCopyOfNoMessagesIsEmpty()
    {
        Assert.Equal(string.Empty, CodexTranscriptText.PlainText(Array.Empty<TranscriptLine>()));
    }

    [Fact]
    public void TheCapKeepsTheFirstFiveThousandMessagesInOrder()
    {
        var lines = Enumerable.Range(1, 5001).Select(n => Line(n, n % 2 == 0 ? "codex" : "you", "message " + n)).ToList();

        var shown = CodexTranscriptText.Capped(lines);

        Assert.Equal(5000, CodexTranscriptText.MaxMessages);
        Assert.Equal(5000, shown.Count);
        Assert.Equal(1, shown[0].Number);
        Assert.Equal(5000, shown[^1].Number);
    }

    [Fact]
    public void ASessionAtOrUnderTheCapIsNotCut()
    {
        var lines = Enumerable.Range(1, CodexTranscriptText.MaxMessages).Select(n => Line(n, "you", "m" + n)).ToList();

        Assert.Same(lines, CodexTranscriptText.Capped(lines));
    }

    [Fact]
    public void TheCapLineSaysHowManyOfHowManyAreShown()
    {
        Assert.Equal("Showing the first 5,000 of 5,001 messages.", CodexTranscriptText.CapNote(5001, 5000));
        Assert.Equal("Showing the first 5,000 of 12,345 messages.", CodexTranscriptText.CapNote(12345, 5000));
    }

    [Fact]
    public void TheCapLineIsAbsentWhenEveryMessageIsShown()
    {
        Assert.Null(CodexTranscriptText.CapNote(5000, 5000));
        Assert.Null(CodexTranscriptText.CapNote(4, 4));
        Assert.Null(CodexTranscriptText.CapNote(0, 0));
    }

    [Fact]
    public void TheCountTextSaysZeroOneOrManyMessages()
    {
        Assert.Equal("0 messages", CodexTranscriptText.MessageCount(0));
        Assert.Equal("1 message", CodexTranscriptText.MessageCount(1));
        Assert.Equal("4 messages", CodexTranscriptText.MessageCount(4));
        Assert.Equal("5,001 messages", CodexTranscriptText.MessageCount(5001));
    }

    [Fact]
    public void TheScopeLineSaysThatOnlyMessagesAreShown()
    {
        Assert.Equal("Messages only. Tool calls and their output are not shown.", CodexTranscriptText.ScopeLine);
    }

    [Fact]
    public void TheTimeLabelIsEmptyWhenTheRecordHasNoTimeAndLocalShortFormOtherwise()
    {
        Assert.Equal(string.Empty, CodexTranscriptText.TimeLabel(null));

        var at = new DateTimeOffset(2026, 11, 21, 9, 5, 0, TimeSpan.Zero);
        var expected = at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal(expected, CodexTranscriptText.TimeLabel(at));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", CodexTranscriptText.TimeLabel(at));
    }
}

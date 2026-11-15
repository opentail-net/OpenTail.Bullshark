using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Tests.Resume;

public sealed class ResumeTextTests
{
    private static TranscriptLine Line(string kind, string text, bool collapsed = false) =>
        new(0, kind, null, text, collapsed);

    /// <summary>Each message is a "you" line followed by a reply, "reply 0", "reply 1", and so on.</summary>
    private static List<TranscriptLine> Conversation(params string[] messages)
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < messages.Length; i++)
        {
            lines.Add(Line("you", messages[i]));
            lines.Add(Line("claude", "reply " + i));
        }
        return lines;
    }

    [Fact]
    public void TheTailKeepsTheLastTurnsWordForWord()
    {
        var tail = ResumeText.Tail(Conversation("first", "second", "third"), turns: 2);

        Assert.DoesNotContain("first", tail);
        Assert.Contains("you: second", tail);
        Assert.Contains("claude: reply 2", tail);
    }

    [Fact]
    public void ToolCallsAndResultsStayOnOneLineEach()
    {
        var lines = new List<TranscriptLine>
        {
            Line("you", "run it"),
            Line("tool", "▸ Bash ls\nsecond line", collapsed: true),
            Line("result", "result: a\nb\nc", collapsed: true),
            Line("claude", "done"),
        };

        var tailLines = ResumeText.Tail(lines, turns: 1).Split('\n');

        Assert.Equal(4, tailLines.Length);
        Assert.Equal("▸ Bash ls second line", tailLines[1]);
        Assert.Equal("result: a b c", tailLines[2]);
    }

    [Fact]
    public void WhenOverTheCapTheOldestTurnGoesFirst()
    {
        var big = new string('x', 400);

        var tail = ResumeText.Tail(Conversation(big, big, big), turns: 3, maxTokens: 250);

        Assert.Equal(2, tail.Split("you: ").Length - 1);
        Assert.DoesNotContain("claude: reply 0", tail);
        Assert.Contains("claude: reply 2", tail);
    }

    [Fact]
    public void TheLatestTurnIsKeptWholeEvenWhenItAloneIsOverTheCap()
    {
        var huge = new string('y', 4000);

        var tail = ResumeText.Tail(Conversation("older", huge), turns: 2, maxTokens: 50);

        Assert.Contains(huge, tail);
        Assert.DoesNotContain("older", tail);
    }

    [Fact]
    public void ZeroTurnsGivesNoTail() =>
        Assert.Equal(string.Empty, ResumeText.Tail(Conversation("a"), turns: 0));

    [Fact]
    public void TheSummaryInputKeepsTheNewestTextAndSaysWhatWasLeftOut()
    {
        var input = ResumeText.SummaryInput(Conversation("old message", "new message"), maxChars: 45);

        Assert.Contains("you: new message", input);
        Assert.DoesNotContain("old message", input);
        Assert.Contains("left out", input);
    }

    [Theory]
    [InlineData(SummaryLevel.Minimal, "120 words")]
    [InlineData(SummaryLevel.Normal, "400 words")]
    [InlineData(SummaryLevel.Complete, "1,200 words")]
    public void EachLevelStatesItsSize(SummaryLevel level, string size) =>
        Assert.Contains(size, ResumeText.Instructions(level));

    [Fact]
    public void TheStartTextHasTheSummaryThenTheTail()
    {
        var text = ResumeText.Compose("Goal: X.", SummaryLevel.Normal, "you: hi", turns: 1);

        var summaryAt = text.IndexOf("## Summary (normal)", StringComparison.Ordinal);
        var tailAt = text.IndexOf("## The last 1 turns", StringComparison.Ordinal);
        Assert.True(summaryAt > 0);
        Assert.True(tailAt > summaryAt);
        Assert.Contains("Goal: X.", text);
    }

    [Fact]
    public void TailOnlyHasNoSummarySection()
    {
        var text = ResumeText.Compose(null, null, "you: hi", turns: 1);

        Assert.DoesNotContain("## Summary", text);
        Assert.Contains("you: hi", text);
    }
}
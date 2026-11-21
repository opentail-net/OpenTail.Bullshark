using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Step R2: the start text of a Codex session (decision R-D5), copied from Claude's <c>ResumeText</c>. Pure: the lines are synthetic.
/// </summary>
public sealed class CodexResumeTextTests
{
    private const string Header = "This session continues an earlier Codex CLI session that had gone cold. What follows is not the full conversation.";

    private const string Closing = "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    private static TranscriptLine Line(int number, string kind, string text) => new(number, kind, null, text, Collapsed: false);

    [Fact]
    public void ALeadingBlockBeforeTheFirstOwnerLineIsATurnOfItsOwn()
    {
        var lines = new[] { Line(1, "codex", "greeting"), Line(2, "you", "first question"), Line(3, "codex", "answer"), Line(4, "you", "second") };

        var turns = CodexResumeText.SplitTurns(lines);

        Assert.Equal(3, turns.Count);
        Assert.Equal(new[] { 1 }, turns[0].Select(l => l.Number).ToArray());
        Assert.Equal(new[] { 2, 3 }, turns[1].Select(l => l.Number).ToArray());
        Assert.Equal(new[] { 4 }, turns[2].Select(l => l.Number).ToArray());
    }

    [Fact]
    public void NoLinesGiveNoTurns()
    {
        Assert.Empty(CodexResumeText.SplitTurns(Array.Empty<TranscriptLine>()));
    }

    [Fact]
    public void TheLastTenTurnsAreKeptByDefault()
    {
        var lines = Enumerable.Range(1, 12).Select(i => Line(i, "you", $"turn {i:00}")).ToList();

        var tail = CodexResumeText.Tail(lines);

        Assert.Contains("you: turn 03", tail);
        Assert.Contains("you: turn 12", tail);
        Assert.DoesNotContain("turn 02", tail);
    }

    [Fact]
    public void TheOldestTurnGoesFirstWhenTheTailIsOverTheTokenCap()
    {
        var body = new string('x', 4000);
        var lines = new[] { Line(1, "you", "T1 " + body), Line(2, "you", "T2 " + body), Line(3, "you", "T3 " + body) };

        var tail = CodexResumeText.Tail(lines, maxTokens: 2500);

        Assert.DoesNotContain("T1 ", tail);
        Assert.Contains("T2 ", tail);
        Assert.Contains("T3 ", tail);
        Assert.True(ResumeText.EstimateTokens(tail) <= 2500, "the tail is over the token cap");
    }

    [Fact]
    public void TheLatestTurnIsNeverRemovedForTheTokenCap()
    {
        var big = new string('y', 10_000);
        var lines = new[] { Line(1, "you", "old"), Line(2, "you", big) };

        var tail = CodexResumeText.Tail(lines, maxTokens: 100);

        Assert.Equal("you: " + big, tail);
    }

    [Fact]
    public void WhenOverTheCharacterCapTheOldestTurnGoesAndTheLatestIsCutInItsMiddle()
    {
        var lines = new[]
        {
            Line(1, "you", "OLD question"),
            Line(2, "codex", "reply"),
            Line(3, "you", "start-" + new string('x', 1000) + "-the-end"),
        };

        var tail = CodexResumeText.Tail(lines, maxChars: 300);

        Assert.True(tail.Length <= 300, $"{tail.Length} characters for a cap of 300");
        Assert.DoesNotContain("OLD", tail);
        Assert.StartsWith("you: start-", tail);
        Assert.EndsWith("-the-end", tail);
        Assert.Contains(" [cut ", tail);
    }

    [Fact]
    public void TheTailLabelsOwnerAndCodexLinesAndLeavesOutOtherKindsAndNoTurnsGiveNothing()
    {
        var lines = new[] { Line(1, "you", "question"), Line(2, "codex", "answer"), Line(3, "tool", "not in the tail") };

        Assert.Equal("you: question\ncodex: answer", CodexResumeText.Tail(lines));
        Assert.Equal(string.Empty, CodexResumeText.Tail(lines, turns: 0));
    }

    [Fact]
    public void TheHeaderNamesTheContinuedSessionAndSaysItIsCodexAndNotTheWholeConversation()
    {
        var text = CodexResumeText.Compose("A summary.", SummaryLevel.Normal, "you: hi", 10, "019e1000-0000-7000-8000-000000000001");

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("Continues session: 019e1000-0000-7000-8000-000000000001", lines[0]);
        Assert.Equal(Header, lines[1]);
        Assert.DoesNotContain("Claude", text);
    }

    [Fact]
    public void TheSessionLineIsLeftOutWhenNothingIsContinued()
    {
        var text = CodexResumeText.Compose(null, null, "you: hi", 10);

        Assert.StartsWith(Header, text);
        Assert.DoesNotContain("Continues session", text);
    }

    [Fact]
    public void TheSummaryHasItsLevelHeadingAndIsTrimmed()
    {
        var text = CodexResumeText.Compose("  The goal and the state.  \n", SummaryLevel.Complete, "you: hi", 10);

        Assert.Contains("## Summary (complete)", text);
        Assert.Contains("The goal and the state.", text);
        Assert.DoesNotContain("  The goal", text);
    }

    [Fact]
    public void ThereIsNoSummarySectionWithoutASummaryAndALevel()
    {
        Assert.DoesNotContain("## Summary", CodexResumeText.Compose(null, SummaryLevel.Normal, "you: hi", 10));
        Assert.DoesNotContain("## Summary", CodexResumeText.Compose("A summary.", null, "you: hi", 10));
    }

    [Fact]
    public void ASummaryLongerThanTheCapIsCutAndSaysSoAndOneAtTheCapIsNot()
    {
        var longSummary = new string('a', ResumeText.MaxSummaryChars + 500);
        var cut = CodexResumeText.Compose(longSummary, SummaryLevel.Normal, string.Empty, 0);
        Assert.Contains(new string('a', ResumeText.MaxSummaryChars) + " (the rest of the summary is left out)", cut);
        Assert.DoesNotContain(new string('a', ResumeText.MaxSummaryChars + 1), cut);

        var atCap = CodexResumeText.Compose(new string('b', ResumeText.MaxSummaryChars), SummaryLevel.Normal, string.Empty, 0);
        Assert.DoesNotContain("left out", atCap);
    }

    [Fact]
    public void TheTailSectionHasItsHeadingAndIsLeftOutWhenNoTurnsOrNoTailAreGiven()
    {
        var text = CodexResumeText.Compose(null, null, "you: hi\ncodex: hello", 10);
        Assert.Contains("## The last 10 turns, word for word (messages only)", text);
        Assert.Contains("you: hi\ncodex: hello", text);

        Assert.DoesNotContain("## The last", CodexResumeText.Compose("A summary.", SummaryLevel.Minimal, "you: hi", 0));
        Assert.DoesNotContain("## The last", CodexResumeText.Compose(null, null, string.Empty, 10));
    }

    [Fact]
    public void TheClosingLineEndsTheTextWhateverSectionsArePresent()
    {
        Assert.EndsWith(Closing, CodexResumeText.Compose("A summary.", SummaryLevel.Normal, "you: hi", 10, "id"));
        Assert.EndsWith(Closing, CodexResumeText.Compose(null, null, string.Empty, 0));
    }
}

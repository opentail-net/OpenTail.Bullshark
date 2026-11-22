using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.8: the start text of a Copilot session (a copy of the Codex resume text with Copilot's wording). Pure: the lines are synthetic. There are
/// no injected-context cases, because Copilot has no injected-context prefixes.
/// </summary>
public sealed class CopilotResumeTextTests
{
    private const string Header = "This session continues an earlier GitHub Copilot CLI session that had gone cold. What follows is not the full conversation.";

    private const string Closing = "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    private static TranscriptLine Line(int number, string kind, string text) => new(number, kind, null, text, Collapsed: false);

    [Fact]
    public void ALeadingBlockBeforeTheFirstOwnerLineIsATurnOfItsOwn()
    {
        var lines = new[] { Line(1, "copilot", "greeting"), Line(2, "you", "first question"), Line(3, "copilot", "answer"), Line(4, "you", "second") };

        var turns = CopilotResumeText.SplitTurns(lines);

        Assert.Equal(3, turns.Count);
        Assert.Equal(new[] { 1 }, turns[0].Select(l => l.Number).ToArray());
        Assert.Equal(new[] { 2, 3 }, turns[1].Select(l => l.Number).ToArray());
        Assert.Equal(new[] { 4 }, turns[2].Select(l => l.Number).ToArray());
    }

    [Fact]
    public void NoLinesGiveNoTurns()
    {
        Assert.Empty(CopilotResumeText.SplitTurns(Array.Empty<TranscriptLine>()));
    }

    [Fact]
    public void TheLastTenTurnsAreKeptByDefault()
    {
        var lines = Enumerable.Range(1, 12).Select(i => Line(i, "you", $"turn {i:00}")).ToList();

        var tail = CopilotResumeText.Tail(lines);

        Assert.Contains("you: turn 03", tail);
        Assert.Contains("you: turn 12", tail);
        Assert.DoesNotContain("turn 02", tail);
    }

    [Fact]
    public void TheOldestTurnGoesFirstWhenTheTailIsOverTheTokenCap()
    {
        var body = new string('x', 4000);
        var lines = new[] { Line(1, "you", "T1 " + body), Line(2, "you", "T2 " + body), Line(3, "you", "T3 " + body) };

        var tail = CopilotResumeText.Tail(lines, maxTokens: 2500);

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

        Assert.Equal("you: " + big, CopilotResumeText.Tail(lines, maxTokens: 100));
    }

    [Fact]
    public void WhenOverTheCharacterCapTheOldestTurnGoesAndTheLatestIsCutInItsMiddle()
    {
        var lines = new[]
        {
            Line(1, "you", "OLD question"),
            Line(2, "copilot", "reply"),
            Line(3, "you", "start-" + new string('x', 1000) + "-the-end"),
        };

        var tail = CopilotResumeText.Tail(lines, maxChars: 300);

        Assert.True(tail.Length <= 300, $"{tail.Length} characters for a cap of 300");
        Assert.DoesNotContain("OLD", tail);
        Assert.StartsWith("you: start-", tail);
        Assert.EndsWith("-the-end", tail);
        Assert.Contains(" [cut ", tail);
    }

    [Fact]
    public void TheTailLabelsOwnerAndCopilotLinesAndLeavesOutOtherKindsAndNoTurnsGiveNothing()
    {
        var lines = new[] { Line(1, "you", "question"), Line(2, "copilot", "answer"), Line(3, "tool", "not in the tail") };

        Assert.Equal("you: question\ncopilot: answer", CopilotResumeText.Tail(lines));
        Assert.Equal(string.Empty, CopilotResumeText.Tail(lines, turns: 0));
    }

    [Fact]
    public void TheHeaderNamesTheContinuedSessionAndSaysItIsCopilotAndNotTheWholeConversation()
    {
        var text = CopilotResumeText.Compose("A summary.", SummaryLevel.Normal, "you: hi", 10, "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51");

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("Continues session: 3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51", lines[0]);
        Assert.Equal(Header, lines[1]);
        Assert.DoesNotContain("Codex", text);
        Assert.DoesNotContain("Claude", text);
    }

    [Fact]
    public void TheSessionLineIsLeftOutWhenNothingIsContinued()
    {
        var text = CopilotResumeText.Compose(null, null, "you: hi", 10);

        Assert.StartsWith(Header, text);
        Assert.DoesNotContain("Continues session", text);
    }

    [Fact]
    public void TheSummaryHasItsLevelHeadingAndIsTrimmed()
    {
        var text = CopilotResumeText.Compose("  The goal and the state.  \n", SummaryLevel.Complete, "you: hi", 10);

        Assert.Contains("## Summary (complete)", text);
        Assert.Contains("The goal and the state.", text);
        Assert.DoesNotContain("  The goal", text);
    }

    [Fact]
    public void ThereIsNoSummarySectionWithoutASummaryAndALevel()
    {
        Assert.DoesNotContain("## Summary", CopilotResumeText.Compose(null, SummaryLevel.Normal, "you: hi", 10));
        Assert.DoesNotContain("## Summary", CopilotResumeText.Compose("A summary.", null, "you: hi", 10));
    }

    [Fact]
    public void ASummaryLongerThanTheCapIsCutAndSaysSoAndOneAtTheCapIsNot()
    {
        var longSummary = new string('a', ResumeText.MaxSummaryChars + 500);
        var cut = CopilotResumeText.Compose(longSummary, SummaryLevel.Normal, string.Empty, 0);
        Assert.Contains(new string('a', ResumeText.MaxSummaryChars) + " (the rest of the summary is left out)", cut);
        Assert.DoesNotContain(new string('a', ResumeText.MaxSummaryChars + 1), cut);

        var atCap = CopilotResumeText.Compose(new string('b', ResumeText.MaxSummaryChars), SummaryLevel.Normal, string.Empty, 0);
        Assert.DoesNotContain("left out", atCap);
    }

    [Fact]
    public void TheTailSectionHasItsHeadingAndIsLeftOutWhenNoTurnsOrNoTailAreGiven()
    {
        var text = CopilotResumeText.Compose(null, null, "you: hi\ncopilot: hello", 10);
        Assert.Contains("## The last 10 turns, word for word (messages only)", text);
        Assert.Contains("you: hi\ncopilot: hello", text);

        Assert.DoesNotContain("## The last", CopilotResumeText.Compose("A summary.", SummaryLevel.Minimal, "you: hi", 0));
        Assert.DoesNotContain("## The last", CopilotResumeText.Compose(null, null, string.Empty, 10));
    }

    [Fact]
    public void TheClosingLineEndsTheTextWhateverSectionsArePresent()
    {
        Assert.EndsWith(Closing, CopilotResumeText.Compose("A summary.", SummaryLevel.Normal, "you: hi", 10, "id"));
        Assert.EndsWith(Closing, CopilotResumeText.Compose(null, null, string.Empty, 0));
    }
}

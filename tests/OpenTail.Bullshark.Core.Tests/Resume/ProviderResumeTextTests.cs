using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>
/// Step R4: the helpers that the Codex and Copilot start texts share (<c>ProviderResumeText</c>), with the CLI phrase and the message label as
/// parameters. Pure: the lines are synthetic.
/// </summary>
public sealed class ProviderResumeTextTests
{
    private const string CodexHeader = "This session continues an earlier Codex CLI session that had gone cold. What follows is not the full conversation.";

    private const string CopilotHeader = "This session continues an earlier GitHub Copilot CLI session that had gone cold. What follows is not the full conversation.";

    private const string Closing = "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    private static TranscriptLine Line(int number, string kind, string text) => new(number, kind, null, text, Collapsed: false);

    [Fact]
    public void ALeadingBlockBeforeTheFirstOwnerLineIsATurnOfItsOwn()
    {
        var lines = new[] { Line(1, "codex", "greeting"), Line(2, "you", "first question"), Line(3, "codex", "answer"), Line(4, "you", "second") };

        var turns = ProviderResumeText.SplitTurns(lines);

        Assert.Equal(3, turns.Count);
        Assert.Equal(new[] { 1 }, turns[0].Select(l => l.Number).ToArray());
        Assert.Equal(new[] { 2, 3 }, turns[1].Select(l => l.Number).ToArray());
        Assert.Equal(new[] { 4 }, turns[2].Select(l => l.Number).ToArray());
    }

    [Fact]
    public void EachOwnerLineStartsATurnAndNoLinesGiveNoTurns()
    {
        var turns = ProviderResumeText.SplitTurns(new[] { Line(1, "you", "a"), Line(2, "you", "b") });

        Assert.Equal(2, turns.Count);
        Assert.Equal(new[] { 2 }, turns[1].Select(l => l.Number).ToArray());
        Assert.Empty(ProviderResumeText.SplitTurns(Array.Empty<TranscriptLine>()));
    }

    [Fact]
    public void TheTailKeepsOnlyTheLastTurns()
    {
        var lines = Enumerable.Range(1, 12).Select(i => Line(i, "you", $"turn {i:00}")).ToList();

        Assert.Equal("you: turn 10\n\nyou: turn 11\n\nyou: turn 12", ProviderResumeText.Tail(lines, "codex", turns: 3));
    }

    [Fact]
    public void TheOldestTurnGoesFirstWhenTheTailIsOverTheTokenCap()
    {
        var body = new string('x', 4000);
        var lines = new[] { Line(1, "you", "T1 " + body), Line(2, "you", "T2 " + body), Line(3, "you", "T3 " + body) };

        var tail = ProviderResumeText.Tail(lines, "codex", maxTokens: 2500);

        Assert.Equal(ProviderResumeText.Joined(new[] { "you: T2 " + body, "you: T3 " + body }), tail);
        Assert.True(ResumeText.EstimateTokens(tail) <= 2500, "the tail is over the token cap");
    }

    [Fact]
    public void TheLatestTurnIsNeverRemovedForTheTokenCap()
    {
        var big = new string('y', 10_000);
        var lines = new[] { Line(1, "you", "old"), Line(2, "you", big) };

        Assert.Equal("you: " + big, ProviderResumeText.Tail(lines, "codex", maxTokens: 100));
    }

    [Fact]
    public void WhenOverTheCharacterCapTheOldestTurnGoesAndTheLatestIsCutInItsMiddle()
    {
        var latest = "you: start-" + new string('x', 1000) + "-the-end";
        var lines = new[]
        {
            Line(1, "you", "OLD question"),
            Line(2, "codex", "reply"),
            Line(3, "you", "start-" + new string('x', 1000) + "-the-end"),
        };

        var tail = ProviderResumeText.Tail(lines, "codex", maxChars: 300);

        Assert.Equal(ResumeText.CutMiddle(latest, 300), tail);
        Assert.True(tail.Length <= 300, $"{tail.Length} characters for a cap of 300");
        Assert.DoesNotContain("OLD", tail);
        Assert.StartsWith("you: start-", tail);
        Assert.EndsWith("-the-end", tail);
        Assert.Contains(" [cut ", tail);
    }

    [Fact]
    public void NoTurnsNoCharactersOrNoLinesGiveNothing()
    {
        var lines = new[] { Line(1, "you", "question") };

        Assert.Equal(string.Empty, ProviderResumeText.Tail(lines, "codex", turns: 0));
        Assert.Equal(string.Empty, ProviderResumeText.Tail(lines, "codex", maxChars: 0));
        Assert.Equal(string.Empty, ProviderResumeText.Tail(Array.Empty<TranscriptLine>(), "codex"));
    }

    [Fact]
    public void TheAssistantWordDecidesWhichCliLinesAreKeptAndHowTheyAreLabelled()
    {
        var lines = new[] { Line(1, "you", "question"), Line(2, "codex", "answer"), Line(3, "copilot", "other"), Line(4, "tool", "not in the tail") };

        Assert.Equal("you: question\ncodex: answer", ProviderResumeText.Tail(lines, "codex"));
        Assert.Equal("you: question\ncopilot: other", ProviderResumeText.Tail(lines, "copilot"));
    }

    [Fact]
    public void RenderLabelsTheOwnerAndTheAssistantAndLeavesOutOtherKinds()
    {
        var turn = new[] { Line(1, "you", "a"), Line(2, "tool", "t"), Line(3, "codex", "b"), Line(4, "result", "r") };

        Assert.Equal("you: a\ncodex: b", ProviderResumeText.Render(turn, "codex"));
        Assert.Equal(string.Empty, ProviderResumeText.Render(Array.Empty<TranscriptLine>(), "codex"));
    }

    [Fact]
    public void JoinedPutsABlankLineBetweenTurns()
    {
        Assert.Equal("a\n\nb", ProviderResumeText.Joined(new[] { "a", "b" }));
        Assert.Equal(string.Empty, ProviderResumeText.Joined(Array.Empty<string>()));
    }

    [Fact]
    public void CapLeavesAShortSummaryAndCutsALongOneWithANote()
    {
        Assert.Equal("short", ProviderResumeText.Cap("short"));

        var atCap = new string('b', ResumeText.MaxSummaryChars);
        Assert.Equal(atCap, ProviderResumeText.Cap(atCap));

        var over = new string('a', ResumeText.MaxSummaryChars + 500);
        Assert.Equal(new string('a', ResumeText.MaxSummaryChars) + " (the rest of the summary is left out)", ProviderResumeText.Cap(over));
    }

    [Fact]
    public void TheHeaderNamesTheCodexCliPhraseAndTheSessionItContinues()
    {
        var text = ProviderResumeText.Compose("Codex CLI", "A summary.", SummaryLevel.Normal, "you: hi", 10, "019e1000-0000-7000-8000-000000000001");

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("Continues session: 019e1000-0000-7000-8000-000000000001", lines[0]);
        Assert.Equal(CodexHeader, lines[1]);
        Assert.EndsWith(Closing, text);
    }

    [Fact]
    public void TheHeaderNamesTheGitHubCopilotCliPhrase()
    {
        var text = ProviderResumeText.Compose("GitHub Copilot CLI", null, null, "you: hi", 10);

        Assert.StartsWith(CopilotHeader, text);
    }

    [Fact]
    public void TheSessionLineIsLeftOutWhenNothingIsContinued()
    {
        Assert.DoesNotContain("Continues session", ProviderResumeText.Compose("Codex CLI", null, null, string.Empty, 0));
        Assert.DoesNotContain("Continues session", ProviderResumeText.Compose("Codex CLI", null, null, string.Empty, 0, string.Empty));
    }

    [Fact]
    public void ThereIsNoSummarySectionWithoutASummaryAndALevel()
    {
        Assert.DoesNotContain("## Summary", ProviderResumeText.Compose("Codex CLI", null, SummaryLevel.Normal, "you: hi", 10));
        Assert.DoesNotContain("## Summary", ProviderResumeText.Compose("Codex CLI", "A summary.", null, "you: hi", 10));
        Assert.Contains("## Summary (normal)", ProviderResumeText.Compose("Codex CLI", "A summary.", SummaryLevel.Normal, string.Empty, 0));
    }

    [Fact]
    public void TheSummaryHasItsLevelHeadingAndIsTrimmed()
    {
        var text = ProviderResumeText.Compose("GitHub Copilot CLI", "  The goal and the state.  \n", SummaryLevel.Complete, "you: hi", 10);

        Assert.Contains("## Summary (complete)", text);
        Assert.Contains("The goal and the state.", text);
        Assert.DoesNotContain("  The goal", text);
    }

    [Fact]
    public void TheTailSectionIsLeftOutWhenThereAreNoTurnsOrNoTail()
    {
        var text = ProviderResumeText.Compose("GitHub Copilot CLI", null, null, "you: hi\ncopilot: hello", 10);
        Assert.Contains("## The last 10 turns, word for word (messages only)", text);
        Assert.Contains("you: hi\ncopilot: hello", text);

        Assert.DoesNotContain("## The last", ProviderResumeText.Compose("GitHub Copilot CLI", "A summary.", SummaryLevel.Minimal, "you: hi", 0));
        Assert.DoesNotContain("## The last", ProviderResumeText.Compose("GitHub Copilot CLI", null, null, string.Empty, 10));
    }

    [Fact]
    public void TheClosingLineEndsTheTextWhateverSectionsArePresent()
    {
        Assert.EndsWith(Closing, ProviderResumeText.Compose("Codex CLI", "A summary.", SummaryLevel.Normal, "you: hi", 10, "id"));
        Assert.EndsWith(Closing, ProviderResumeText.Compose("Codex CLI", null, null, string.Empty, 0));
    }
}

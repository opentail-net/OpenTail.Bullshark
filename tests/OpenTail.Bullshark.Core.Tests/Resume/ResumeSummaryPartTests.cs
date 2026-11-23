using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>
/// The summary's part (RS-D20, RS-D23): <see cref="ResumeText.SummaryPart"/> is the messages before the tail's first turn, so the summary part and the
/// tail's turns cover every turn once, for each tail setting and cap. The summary's input for a provider reads only the part. Pure: the lines are synthetic.
/// </summary>
public sealed class ResumeSummaryPartTests
{
    private static TranscriptLine Line(string kind, string text) => new(0, kind, null, text, Collapsed: false);

    /// <summary>Each turn is an owner's message and a reply, and each message starts with its turn number, so a lost or repeated turn shows.</summary>
    private static List<TranscriptLine> Session(int turnCount, string reply = "claude")
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < turnCount; i++)
        {
            lines.Add(Line("you", $"turn {i} " + new string('q', 40)));
            lines.Add(Line(reply, $"turn {i} reply " + new string('r', 40)));
        }
        return lines;
    }

    private static int ClaudeTokens(IEnumerable<TranscriptLine> turn) => ResumeText.TurnTokens(turn);

    private static int CodexTokens(IEnumerable<TranscriptLine> turn) => ResumeText.EstimateTokens(ProviderResumeText.Render(turn, "codex"));

    private static IEnumerable<string> TextsOf(IEnumerable<TranscriptLine> lines) => lines.Select(l => l.Text);

    [Theory]
    [InlineData(-1, int.MaxValue)]
    [InlineData(0, int.MaxValue)]
    [InlineData(1, int.MaxValue)]
    [InlineData(3, int.MaxValue)]
    [InlineData(10, int.MaxValue)]
    [InlineData(3, 100)]
    [InlineData(6, 60)]
    [InlineData(6, 1)]
    public void TheClaudeSummaryPartAndTheTailCoverEveryTurnOnce(int turns, int maxTokens)
    {
        var lines = Session(6);
        var all = ResumeText.SplitTurns(lines);
        var summaryPart = ResumeText.SummaryPart(lines, turns, maxTokens, ClaudeTokens);
        var summaryTurns = ResumeText.SplitTurns(summaryPart).Count;
        var tailLines = all.Skip(summaryTurns).SelectMany(t => t).ToList();

        Assert.Equal(TextsOf(lines), TextsOf(summaryPart).Concat(TextsOf(tailLines)));
        Assert.Equal(all.Count - summaryTurns, ResumeText.TailWithTurns(lines, turns, maxTokens).Turns);
        Assert.Equal(ResumeText.Tail(tailLines, int.MaxValue, int.MaxValue), ResumeText.Tail(lines, turns, maxTokens));
    }

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(2, int.MaxValue)]
    [InlineData(6, 90)]
    [InlineData(3, 60)]
    [InlineData(6, 1)]
    public void TheProviderSummaryPartAndTheTailCoverEveryTurnOnce(int turns, int maxTokens)
    {
        var lines = Session(6, reply: "codex");
        var all = ResumeText.SplitTurns(lines);
        var summaryPart = ResumeText.SummaryPart(lines, turns, maxTokens, CodexTokens);
        var summaryTurns = ResumeText.SplitTurns(summaryPart).Count;
        var tailLines = all.Skip(summaryTurns).SelectMany(t => t).ToList();

        Assert.Equal(TextsOf(lines), TextsOf(summaryPart).Concat(TextsOf(tailLines)));
        Assert.Equal(all.Count - summaryTurns, ProviderResumeText.TailWithTurns(lines, "codex", turns, maxTokens).Turns);
    }

    [Fact]
    public void AZeroTailSummarisesTheWholeSession()
    {
        var lines = Session(4);
        Assert.Equal(TextsOf(lines), TextsOf(ResumeText.SummaryPart(lines, 0, int.MaxValue, ClaudeTokens)));
        Assert.Equal(TextsOf(lines), TextsOf(ResumeText.SummaryPart(lines, -1, int.MaxValue, ClaudeTokens)));
    }

    [Fact]
    public void ATailThatKeepsEveryTurnLeavesNothingToSummarise()
    {
        var lines = Session(4);
        Assert.Empty(ResumeText.SummaryPart(lines, 4, int.MaxValue, ClaudeTokens));
        Assert.Empty(ResumeText.SummaryPart(lines, 10, int.MaxValue, ClaudeTokens));
        Assert.Empty(ResumeText.SummaryPart([], 3, int.MaxValue, ClaudeTokens));
    }

    [Fact]
    public void TheSummaryPartIsTheTurnsBeforeTheTailNotTheTailsCut()
    {
        var lines = Session(6);

        // Three turns kept: the summary reads turns 0 to 2, and the tail is turns 3 to 5.
        var part = ResumeText.SummaryPart(lines, 3, int.MaxValue, ClaudeTokens);
        Assert.Equal(6, part.Count);
        Assert.StartsWith("turn 0 ", part[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("turn 2 reply", part[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProviderSummaryInputReadsOnlyThePart()
    {
        var lines = Session(6, reply: "codex");
        var part = ResumeText.SummaryPart(lines, 2, int.MaxValue, CodexTokens);

        var input = ProviderResumeText.SummaryInput(part, "codex");

        Assert.Contains("you: turn 0 ", input, StringComparison.Ordinal);
        Assert.Contains("codex: turn 3 reply", input, StringComparison.Ordinal);
        Assert.DoesNotContain("turn 4 ", input, StringComparison.Ordinal);
        Assert.DoesNotContain("turn 5 ", input, StringComparison.Ordinal);
    }
}

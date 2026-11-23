using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The rules of the Codex resume screen (steps R5 and R7): labels, cost lines, the turns check, the size line and the Start rule. Pure: no window is made.
/// </summary>
public sealed class CodexResumeRulesTests
{
    [Fact]
    public void TheThreeLevelsAreLabelledWithTheirLengths()
    {
        Assert.Equal("Minimal · about 120 words", CodexResumeRules.LevelLabel(SummaryLevel.Minimal));
        Assert.Equal("Normal · about 400 words", CodexResumeRules.LevelLabel(SummaryLevel.Normal));
        Assert.Equal("Complete · about 1,200 words", CodexResumeRules.LevelLabel(SummaryLevel.Complete));
    }

    [Fact]
    public void TheSummaryModeSaysWhichModelItCallsAndHowMuchOfTheSessionIsSent()
    {
        Assert.Equal(
            "One Codex call on gpt-6-luna. Up to 120,000 characters of this session are sent.",
            CodexResumeRules.CostLine(CodexResumeMode.Summary));
    }

    [Fact]
    public void TheLastTurnsModeSaysItMakesNoSummaryCall()
    {
        Assert.Equal("No summary call. The text is shown before anything starts.", CodexResumeRules.CostLine(CodexResumeMode.LastTurns));
    }

    [Fact]
    public void InFullSaysOpenTalkSendsNoTextAndDoesNotUseTheShownTextLine()
    {
        var line = CodexResumeRules.CostLine(CodexResumeMode.Full);

        Assert.Equal(CodexResumeRules.FullCostLine, line);
        Assert.StartsWith("No model call from OpenTail.", line);
        Assert.DoesNotContain("is shown before anything starts", line);
    }

    [Theory]
    [InlineData("10", true, 10)]
    [InlineData(" 0 ", true, 0)]
    [InlineData("200", true, 200)]
    [InlineData("201", false, 10)]
    [InlineData("-1", false, 10)]
    [InlineData("lots", false, 10)]
    [InlineData("", false, 10)]
    [InlineData("2.5", false, 10)]
    public void TheTurnsBoxAcceptsAWholeNumberFromZeroTo200(string text, bool valid, int turns)
    {
        var result = CodexResumeRules.TryTurns(text, out var parsed);

        Assert.Equal(valid, result);
        Assert.Equal(turns, parsed);
    }

    [Fact]
    public void TheDefaultTurnsAreTenAndTheLimitIs200()
    {
        Assert.Equal(10, CodexResumeRules.DefaultTurns);
        Assert.Equal(200, CodexResumeRules.MaxTurns);
        Assert.Equal("Enter a whole number from 0 to 200 for the turns.", CodexResumeRules.TurnsMessage);
    }

    [Fact]
    public void TheNoShimLineIsNonEmptyAndNamesCodexPs1()
    {
        Assert.False(string.IsNullOrWhiteSpace(CodexResumeRules.NoShimOnPath));
        Assert.Contains("codex.ps1", CodexResumeRules.NoShimOnPath);
    }

    [Fact]
    public void ASizeLineCountsTokensAsCharactersOverFourRounded()
    {
        Assert.Equal(
            "About 2 tokens (9 characters). Start sends exactly this text.",
            CodexResumeRules.SizeLine(9));
        Assert.Equal(
            "About 250 tokens (1,000 characters). Start sends exactly this text.",
            CodexResumeRules.SizeLine(1000));
    }
}

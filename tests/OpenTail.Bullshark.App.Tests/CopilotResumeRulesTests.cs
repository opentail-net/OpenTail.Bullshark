using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The rules of the Copilot resume screen (G3.1): labels, cost lines, the turns check, the size line and the Start rule, copied from the Codex rules and
/// set to Copilot's words; and the refusal and the started line of a resume (G-D12, G3.2). Pure: no window is made.
/// </summary>
public sealed class CopilotResumeRulesTests
{
    [Fact]
    public void TheThreeModesAreLabelledAsTheScreenShowsThem()
    {
        Assert.Equal("In full: the whole conversation, with copilot --resume", CopilotResumeRules.ModeLabel(CopilotResumeMode.Full));
        Assert.Equal("From the last turns (no model call)", CopilotResumeRules.ModeLabel(CopilotResumeMode.LastTurns));
        Assert.Equal("From a Copilot summary (one Copilot call)", CopilotResumeRules.ModeLabel(CopilotResumeMode.Summary));
    }

    [Fact]
    public void TheThreeLevelsAreLabelledWithTheirLengths()
    {
        Assert.Equal("Minimal · about 120 words", CopilotResumeRules.LevelLabel(SummaryLevel.Minimal));
        Assert.Equal("Normal · about 400 words", CopilotResumeRules.LevelLabel(SummaryLevel.Normal));
        Assert.Equal("Complete · about 1,200 words", CopilotResumeRules.LevelLabel(SummaryLevel.Complete));
    }

    [Fact]
    public void TheSummaryModeSaysItMakesOneCopilotCallAndHowMuchOfTheSessionIsSent()
    {
        Assert.Equal(
            "One Copilot call. Up to 120,000 characters of this session are sent.",
            CopilotResumeRules.CostLine(CopilotResumeMode.Summary));
    }

    [Fact]
    public void TheLastTurnsModeSaysItMakesNoModelCall()
    {
        Assert.Equal("No model call. The text is shown before anything starts.", CopilotResumeRules.CostLine(CopilotResumeMode.LastTurns));
    }

    [Fact]
    public void InFullSaysOpenTalkSendsNoTextAndDoesNotUseTheShownTextLine()
    {
        var line = CopilotResumeRules.CostLine(CopilotResumeMode.Full);

        Assert.Equal(CopilotResumeRules.FullCostLine, line);
        Assert.StartsWith("No model call from OpenTail.", line);
        Assert.Contains("copilot --resume", line);
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
        var result = CopilotResumeRules.TryTurns(text, out var parsed);

        Assert.Equal(valid, result);
        Assert.Equal(turns, parsed);
    }

    [Fact]
    public void TheDefaultTurnsAreTenAndTheLimitIs200()
    {
        Assert.Equal(10, CopilotResumeRules.DefaultTurns);
        Assert.Equal(200, CopilotResumeRules.MaxTurns);
        Assert.Equal("Enter a whole number from 0 to 200 for the turns.", CopilotResumeRules.TurnsMessage);
    }

    [Fact]
    public void InFullStartIsAlwaysOn()
    {
        Assert.True(CopilotResumeRules.CanStart(CopilotResumeMode.Full, previewReady: false, keptMatches: false));
        Assert.True(CopilotResumeRules.CanStart(CopilotResumeMode.Full, previewReady: true, keptMatches: true));
    }

    [Fact]
    public void FromTheLastTurnsStartNeedsOnlyAReadyPreview()
    {
        Assert.False(CopilotResumeRules.CanStart(CopilotResumeMode.LastTurns, previewReady: false, keptMatches: false));
        Assert.True(CopilotResumeRules.CanStart(CopilotResumeMode.LastTurns, previewReady: true, keptMatches: false));
    }

    [Fact]
    public void FromASummaryStartNeedsAReadyPreviewAndASummaryForTheChoice()
    {
        Assert.False(CopilotResumeRules.CanStart(CopilotResumeMode.Summary, previewReady: false, keptMatches: false));
        Assert.False(CopilotResumeRules.CanStart(CopilotResumeMode.Summary, previewReady: false, keptMatches: true));
        Assert.False(CopilotResumeRules.CanStart(CopilotResumeMode.Summary, previewReady: true, keptMatches: false));
        Assert.True(CopilotResumeRules.CanStart(CopilotResumeMode.Summary, previewReady: true, keptMatches: true));
    }

    [Fact]
    public void ASizeLineCountsTokensAsCharactersOverFourRounded()
    {
        Assert.Equal(
            "About 2 tokens (9 characters). Start sends exactly this text.",
            CopilotResumeRules.SizeLine(9));
        Assert.Equal(
            "About 250 tokens (1,000 characters). Start sends exactly this text.",
            CopilotResumeRules.SizeLine(1000));
    }

    [Fact]
    public void ASessionNotInTheListIsRefusedWhateverItsFolder()
    {
        Assert.Equal(CopilotRailText.NotListed, CopilotResumeRules.RefusalFor(listed: false, folder: @"C:\Work\example", folderExists: true));
        Assert.Equal(CopilotRailText.NotListed, CopilotResumeRules.RefusalFor(listed: false, folder: null, folderExists: false));
    }

    [Fact]
    public void ASessionWhoseFolderIsGoneIsRefusedWithTheFolderNamed()
    {
        var folder = @"C:\Work\gone";

        Assert.Equal(CopilotRailText.FolderMissing(folder), CopilotResumeRules.RefusalFor(listed: true, folder: folder, folderExists: false));
        Assert.Equal(
            "Not resumed: the session's folder is missing: C:\\Work\\gone",
            CopilotResumeRules.RefusalFor(listed: true, folder: folder, folderExists: false));
    }

    [Fact]
    public void ASessionWithNoFolderOnRecordIsRefused()
    {
        Assert.Equal(CopilotRailText.FolderMissing(null), CopilotResumeRules.RefusalFor(listed: true, folder: null, folderExists: false));
        Assert.Equal(CopilotRailText.FolderMissing("  "), CopilotResumeRules.RefusalFor(listed: true, folder: "  ", folderExists: false));
    }

    [Fact]
    public void AListedSessionWithItsFolderMayGoOn()
    {
        Assert.Null(CopilotResumeRules.RefusalFor(listed: true, folder: @"C:\Work\example", folderExists: true));
    }

    [Fact]
    public void TheStartedLineNamesTheSourceAndSaysThereIsAModelTurn()
    {
        Assert.Equal(
            "Started a Copilot session from the last 10 turns. It starts with a model turn. If the tab reports that copilot.ps1 is missing, the start text is kept in the Starts folder beside the state file.",
            CopilotResumeRules.StartedLine(level: null, turns: 10));
        Assert.Equal(
            "Started a Copilot session from a Copilot summary (normal) and the last 4 turns. It starts with a model turn. If the tab reports that copilot.ps1 is missing, the start text is kept in the Starts folder beside the state file.",
            CopilotResumeRules.StartedLine(SummaryLevel.Normal, turns: 4));
    }

    [Fact]
    public void ASecondSummaryWhileOneRunsIsRefusedWithAStatusLine()
    {
        Assert.Equal("A Copilot summary is already being made. Wait for it to finish.", CopilotResumeRules.SummaryRunning);
    }

    [Fact]
    public void TheNoShimLineNamesCopilotPs1AndSaysTheTextIsInThePreview()
    {
        Assert.Contains("copilot.ps1", CopilotResumeRules.NoShimOnPath);
        Assert.Contains("preview", CopilotResumeRules.NoShimOnPath);
    }

    [Fact]
    public void TheSummaryNeedsGuardLineIsSetAndSaysItWantsTheLock()
    {
        Assert.False(string.IsNullOrWhiteSpace(CopilotResumeRules.SummaryNeedsGuard));
        Assert.Contains("lock", CopilotResumeRules.SummaryNeedsGuard);
    }
}

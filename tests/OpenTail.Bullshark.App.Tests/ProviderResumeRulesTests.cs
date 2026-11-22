using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The shared resume rules (R6), tested with a made-up provider's wording: the rules must not depend on which words a provider gives them. The Codex and
/// Copilot screens are tested by their own rules tests. Pure: no window is made.
/// </summary>
public sealed class ProviderResumeRulesTests
{
    private static readonly ProviderResumeWording Demo = new("Demo", "demo resume", "demo.ps1", () => "One Demo call.");

    private static readonly KeptSummary KeptNormal = new(SummaryLevel.Normal, "An example summary.", Saved: true);

    [Fact]
    public void TheFullLineAndTheModeLabelsNameTheProvidersResumeCommandAndName()
    {
        Assert.Equal(
            "No model call from OpenTail. The session opens in a terminal tab with demo resume, and OpenTail sends it no text.",
            ProviderResumeRules.FullCostLine(Demo));
        Assert.Equal("In full: the whole conversation, with demo resume", ProviderResumeRules.ModeLabel(ProviderResumeMode.Full, Demo));
        Assert.Equal("From a Demo summary (one Demo call)", ProviderResumeRules.ModeLabel(ProviderResumeMode.Summary, Demo));
        Assert.Equal("From the last turns (no model call)", ProviderResumeRules.ModeLabel(ProviderResumeMode.LastTurns, Demo));
    }

    [Fact]
    public void TheCostLineOfTheSummaryIsTheProvidersOwnAndTheLastTurnsSaysNoModelCall()
    {
        Assert.Equal("One Demo call.", ProviderResumeRules.CostLine(ProviderResumeMode.Summary, Demo));
        Assert.Equal(ProviderResumeRules.NoModelCallLine, ProviderResumeRules.CostLine(ProviderResumeMode.LastTurns, Demo));
    }

    [Fact]
    public void TheStartedLineNamesTheProviderAndItsShim()
    {
        Assert.Equal(
            "Started a Demo session from the last 3 turns. It starts with a model turn. If the tab reports that demo.ps1 is missing, the start text is kept in the Starts folder beside the state file.",
            ProviderResumeRules.StartedLine(Demo, level: null, turns: 3));
        Assert.Equal(
            "Started a Demo session from a Demo summary (complete) and the last 3 turns. It starts with a model turn. If the tab reports that demo.ps1 is missing, the start text is kept in the Starts folder beside the state file.",
            ProviderResumeRules.StartedLine(Demo, SummaryLevel.Complete, turns: 3));
    }

    [Fact]
    public void AKeptSummaryMatchesOnlyTheLevelItWasMadeAt()
    {
        Assert.True(ProviderResumeRules.KeptMatches(SummaryLevel.Normal, KeptNormal));
        Assert.False(ProviderResumeRules.KeptMatches(SummaryLevel.Minimal, KeptNormal));
        Assert.False(ProviderResumeRules.KeptMatches(null, KeptNormal));
        Assert.False(ProviderResumeRules.KeptMatches(SummaryLevel.Normal, null));
    }

    [Fact]
    public void AKeptSummaryIsDroppedOnlyWhenASummaryAtAnotherLevelIsChosen()
    {
        Assert.Null(ProviderResumeRules.KeptAfterChange(summaryChosen: true, SummaryLevel.Minimal, KeptNormal));
        Assert.Same(KeptNormal, ProviderResumeRules.KeptAfterChange(summaryChosen: true, SummaryLevel.Normal, KeptNormal));
        Assert.Same(KeptNormal, ProviderResumeRules.KeptAfterChange(summaryChosen: false, SummaryLevel.Minimal, KeptNormal));
        Assert.Same(KeptNormal, ProviderResumeRules.KeptAfterChange(summaryChosen: false, level: null, KeptNormal));
        Assert.Null(ProviderResumeRules.KeptAfterChange(summaryChosen: true, SummaryLevel.Minimal, null));
    }

    [Fact]
    public void TheTextIsBuiltAtOnceOnlyForTheLastTurnsOrAKeptSummary()
    {
        Assert.True(ProviderResumeRules.RunsAtOnce(ProviderResumeMode.LastTurns, keptMatches: false));
        Assert.True(ProviderResumeRules.RunsAtOnce(ProviderResumeMode.Summary, keptMatches: true));
        Assert.False(ProviderResumeRules.RunsAtOnce(ProviderResumeMode.Summary, keptMatches: false));
        Assert.False(ProviderResumeRules.RunsAtOnce(ProviderResumeMode.Full, keptMatches: true));
    }

    [Fact]
    public void ARefusalUsesTheTextsItIsGiven()
    {
        string Missing(string? folder) => "missing: " + (folder ?? "none");

        Assert.Equal("not listed", ProviderResumeRules.RefusalFor(false, "C:\\x", true, "not listed", Missing));
        Assert.Equal("missing: none", ProviderResumeRules.RefusalFor(true, null, false, "not listed", Missing));
        Assert.Equal("missing: C:\\x", ProviderResumeRules.RefusalFor(true, "C:\\x", false, "not listed", Missing));
        Assert.Null(ProviderResumeRules.RefusalFor(true, "C:\\x", true, "not listed", Missing));
    }

    [Fact]
    public void AnUnknownModeIsRefusedByTheLabelsAndTheCostLineButNotByStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexResumeRules.ModeLabel((CodexResumeMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexResumeRules.CostLine((CodexResumeMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => CopilotResumeRules.ModeLabel((CopilotResumeMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => CopilotResumeRules.CostLine((CopilotResumeMode)99));
        Assert.False(CodexResumeRules.CanStart((CodexResumeMode)99, previewReady: true, keptMatches: true));
        Assert.False(CopilotResumeRules.CanStart((CopilotResumeMode)99, previewReady: true, keptMatches: true));
        Assert.False(CopilotResumeRules.RunsAtOnce((CopilotResumeMode)99, keptMatches: true));
    }
}

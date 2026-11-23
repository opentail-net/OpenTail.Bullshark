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

    [Fact]
    public void TheFullLineNamesTheProvidersResumeCommand()
    {
        Assert.Equal(
            "No model call from OpenTail. The session opens in a terminal tab with demo resume, and OpenTail sends it no text.",
            ProviderResumeRules.FullCostLine(Demo));
    }

    [Fact]
    public void TheCostLineOfTheSummaryIsTheProvidersOwnAndTheLastTurnsSaysNoSummaryCall()
    {
        Assert.Equal("One Demo call.", ProviderResumeRules.CostLine(ProviderResumeMode.Summary, Demo));
        Assert.Equal(ProviderResumeRules.NoSummaryCallLine, ProviderResumeRules.CostLine(ProviderResumeMode.LastTurns, Demo));
        Assert.DoesNotContain("model call", ProviderResumeRules.NoSummaryCallLine, StringComparison.OrdinalIgnoreCase);
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
    public void ARefusalUsesTheTextsItIsGiven()
    {
        string Missing(string? folder) => "missing: " + (folder ?? "none");

        Assert.Equal("not listed", ProviderResumeRules.RefusalFor(false, "C:\\x", true, "not listed", Missing));
        Assert.Equal("missing: none", ProviderResumeRules.RefusalFor(true, null, false, "not listed", Missing));
        Assert.Equal("missing: C:\\x", ProviderResumeRules.RefusalFor(true, "C:\\x", false, "not listed", Missing));
        Assert.Null(ProviderResumeRules.RefusalFor(true, "C:\\x", true, "not listed", Missing));
    }

    [Fact]
    public void AnUnknownModeIsRefusedByTheCostLine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexResumeRules.CostLine((CodexResumeMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => CopilotResumeRules.CostLine((CopilotResumeMode)99));
    }
}

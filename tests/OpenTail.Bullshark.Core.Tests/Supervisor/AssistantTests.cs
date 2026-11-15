using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>The optional assistant for the coders (D-S41): off by default, a third session when chosen.</summary>
public sealed class AssistantTests
{
    [Fact]
    public void WithoutAnAssistantTheRunIsTheSameAsBefore()
    {
        var specs = RunPlan.Build(PatternSpec.Find("plan-code")!, @"C:\W", "task", "ab12");

        Assert.Equal(2, specs.Count);
        Assert.DoesNotContain(specs, s => s.Role == SessionRole.Assistant);
    }

    [Fact]
    public void WithAnAssistantItJoinsTheRunAndTheCoderKnowsIt()
    {
        var specs = RunPlan.Build(PatternSpec.Find("plan-code")!, @"C:\W", "task", "ab12", withAssistant: true);

        var assistant = Assert.Single(specs, s => s.Role == SessionRole.Assistant);
        Assert.Equal("bullshark-assistant-ab12", assistant.Name);
        var coder = Assert.Single(specs, s => s.Role == SessionRole.Coder);
        Assert.Contains("Your assistant is bullshark-assistant-ab12", coder.Brief);
    }

    [Fact]
    public void TheAssistantAnswersInAFewLinesAndDoesNoWork()
    {
        var specs = RunPlan.Build(PatternSpec.Find("plan-many")!, @"C:\W", "task", "ab12", coders: 2, withAssistant: true);

        var assistant = Assert.Single(specs, s => s.Role == SessionRole.Assistant);
        Assert.Contains("answer it in a few lines", assistant.Brief);
        Assert.Contains("Do not write code", assistant.Brief);
    }

    [Fact]
    public void SoloHasNoAssistant()
    {
        var specs = RunPlan.Build(PatternSpec.Find("solo")!, @"C:\W", "task", "ab12", withAssistant: true);

        Assert.Single(specs);
    }

    [Fact]
    public void TheChosenAssistantModelAndEffortAreUsed()
    {
        var request = new SetupRequest("plan-code", @"C:\W", "task", 1, "opus", "high", "haiku", "medium",
            Assistant: "sonnet", AssistantEffort: "low");

        var assistant = Assert.Single(SetupPlan.Build(request, "ab12"), s => s.Role == SessionRole.Assistant);

        Assert.Equal("sonnet", assistant.Model);
        Assert.Equal("low", assistant.Effort);
    }

    [Fact]
    public void AnAssistantIsRefusedOnSolo()
    {
        var request = new SetupRequest("solo", @"C:\W", "task", 1, "opus", "high", "haiku", "medium", Assistant: "haiku");

        var check = SetupPlan.Validate(request, Path.Combine(Path.GetTempPath(), "bullshark-no-claude.json"));

        Assert.Contains(check.Errors, error => error.Contains("solo run has no coder", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePatternRemembersNoAssistantUnlessOneIsChosen()
    {
        Assert.Equal("none", PatternStore.Defaults("plan-code").Assistant);
    }
}

using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>The assistant "reviewer" (D-S41, amended): it uses the reviewer's setting.</summary>
public sealed class ReviewerAssistantTests
{
    private static readonly string NoClaudeJson = Path.Combine(Path.GetTempPath(), "bullshark-no-claude.json");

    [Fact]
    public void ReviewerSelfMakesThePlannerTheAssistantWithNoExtraSession()
    {
        var request = new SetupRequest("plan-code", @"C:\W", "task", 1, "opus", "high", "haiku", "medium",
            Reviewer: "self", Assistant: "reviewer");

        var specs = SetupPlan.Build(request, "ab12");

        Assert.DoesNotContain(specs, s => s.Role == SessionRole.Assistant);
        var coder = Assert.Single(specs, s => s.Role == SessionRole.Coder);
        Assert.Contains("Your reviewer is bullshark-planner-ab12", coder.Brief);
        Assert.Contains("Ask rather than guess", coder.Brief);
    }

    [Fact]
    public void ReviewerAModelMakesASessionOnThatModel()
    {
        var request = new SetupRequest("plan-code", @"C:\W", "task", 1, "opus", "high", "haiku", "medium",
            Reviewer: "opus", Assistant: "reviewer", AssistantEffort: "low");

        var assistant = Assert.Single(SetupPlan.Build(request, "ab12"), s => s.Role == SessionRole.Assistant);

        Assert.Equal("opus", assistant.Model);
        Assert.Equal("low", assistant.Effort);
    }

    [Fact]
    public void ReviewerIsAnOfferedAssistantChoice()
    {
        var request = new SetupRequest("plan-code", @"C:\W", "task", 1, "opus", "high", "haiku", "medium",
            Reviewer: "sonnet", Assistant: "reviewer");

        var check = SetupPlan.Validate(request, NoClaudeJson);

        Assert.DoesNotContain(check.Errors, error => error.Contains("Assistant model", StringComparison.Ordinal));
    }
}

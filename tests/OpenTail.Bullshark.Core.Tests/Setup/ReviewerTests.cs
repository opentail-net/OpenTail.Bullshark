using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The reviewer setting (D-S36): "self" is the planner's own review and keeps the default wording; any other reviewer sends
/// each review to a subagent on that model, and the batches carry their acceptance criteria.
/// </summary>
public sealed class ReviewerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-reviewer-" + Guid.NewGuid().ToString("N")[..8]);

    public ReviewerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SetupRequest Request(string folder, string pattern = "plan-code", int coders = 1, string reviewer = "sonnet") =>
        new(pattern, folder, "Build it", coders, "opus", "high", "haiku", "medium", Reviewer: reviewer);

    [Fact]
    public void SelfAddsNothingToThePlannerBrief()
    {
        var withSelf = RunPlan.PlannerBrief(TestSpecs.PlannerName, [TestSpecs.CoderName], "task", "self");

        Assert.Equal(TestSpecs.PlannerBrief("task"), withSelf);
        Assert.Equal(string.Empty, RunPlan.ReviewClause("self"));
    }

    [Fact]
    public void AModelReviewerIsNamedAndTheBatchesCarryTheirCriteria()
    {
        var run = RunPlan.Build(PatternSpec.Find("plan-code")!, _dir, "task", "ab12", reviewer: "sonnet");

        var planner = run.Single(s => s.Role == SessionRole.Planner);
        Assert.Contains("review it through a subagent with model sonnet", planner.Brief);
        Assert.Contains("acceptance criteria", planner.Brief);
        Assert.Contains("accept, or a numbered list of findings with a file and line each", planner.Brief);
    }

    [Fact]
    public void TheCodersAreUnchangedByTheReviewer()
    {
        var self = RunPlan.Build(PatternSpec.Find("plan-many")!, _dir, "task", "ab12", coders: 2, reviewer: "self");
        var sonnet = RunPlan.Build(PatternSpec.Find("plan-many")!, _dir, "task", "ab12", coders: 2, reviewer: "sonnet");

        Assert.Equal(self.Where(s => s.Role == SessionRole.Coder).Select(s => s.Brief),
            sonnet.Where(s => s.Role == SessionRole.Coder).Select(s => s.Brief));
    }

    [Fact]
    public void SoloHasNoReviewToSendAway()
    {
        var solo = RunPlan.Build(PatternSpec.Find("solo")!, _dir, "task", "ab12", reviewer: "sonnet");

        Assert.DoesNotContain("subagent", Assert.Single(solo).Brief);
    }

    [Fact]
    public void SetupRefusesAReviewerThatIsNotOffered()
    {
        var check = SetupPlan.Validate(Request(_dir, reviewer: "gpt"), Path.Combine(_dir, ".claude.json"));

        Assert.Contains(check.Errors, e => e.Contains("Reviewer 'gpt'"));
    }

    [Fact]
    public void SetupPassesTheChosenReviewerToThePlanner()
    {
        var specs = SetupPlan.Build(Request(_dir, reviewer: "opus"), "ab12");

        Assert.Contains("review it through a subagent with model opus", specs.Single(s => s.Role == SessionRole.Planner).Brief);
    }

    [Fact]
    public void APatternRemembersItsReviewer()
    {
        var store = new PatternStore(Path.Combine(_dir, "patterns.json"));

        store.Save(PatternStore.Defaults("plan-code") with { Reviewer = "fable" });

        Assert.Equal("fable", store.For("plan-code").Reviewer);
        Assert.Equal("sonnet", PatternStore.Defaults("plan-code").Reviewer);
    }
}

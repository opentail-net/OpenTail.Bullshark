using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// A coder may ask its planner a short question by message and keep working (owner decision, 2026-11-15). It must not
/// wait for the answer: a session that waits for a peer shows as blocked (F29).
/// </summary>
public sealed class PeerQuestionTests
{
    [Fact]
    public void TheCoderMayAskThePlannerAndKeepWorking()
    {
        var brief = TestSpecs.CoderBrief();

        Assert.Contains($"send {TestSpecs.PlannerName} one short question with SendMessage", brief);
        Assert.Contains("carry on with the parts you can do", brief);
        Assert.Contains("Do not stop to wait for the answer", brief);
    }

    [Fact]
    public void ThePlannerAnswersAQuestionInOneMessage()
    {
        var brief = TestSpecs.PlannerBrief("task");

        Assert.Contains($"If {TestSpecs.CoderName} sends you a question, answer it in one short message with SendMessage", brief);
    }

    [Fact]
    public void WithManyCodersThePlannerAnswersAnyCoder()
    {
        var run = RunPlan.Build(PatternSpec.Find("plan-many")!, @"C:\W", "task", "ab12", coders: 3);

        var planner = run.First(s => s.Role == SessionRole.Planner);
        Assert.Contains("If a coder sends you a question, answer it in one short message with SendMessage", planner.Brief);
        Assert.All(run.Where(s => s.Role == SessionRole.Coder),
            c => Assert.Contains("Do not stop to wait for the answer", c.Brief));
    }
}

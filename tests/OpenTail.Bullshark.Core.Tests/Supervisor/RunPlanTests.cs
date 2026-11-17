using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class RunPlanTests
{
    private const string WorkDir = @"C:\W";
    private const string RunId = "ab12";

    private static PatternSpec Pattern(PatternKind kind) => PatternSpec.Shipped.Single(p => p.Kind == kind);

    [Fact]
    public void ThreeShippedPatternsHaveDistinctKeys()
    {
        Assert.Equal(3, PatternSpec.Shipped.Count);
        Assert.Equal(3, PatternSpec.Shipped.Select(p => p.Key).Distinct().Count());
        Assert.Equal(PatternKind.Solo, PatternSpec.Find("solo")?.Kind);
        Assert.Null(PatternSpec.Find("no-such-pattern"));
    }

    [Fact]
    public void PlanAndCodeHasOnePlannerAndOneCoderWithSuffixedNames()
    {
        var specs = RunPlan.Build(Pattern(PatternKind.PlanAndCode), WorkDir, "task", RunId);

        Assert.Equal(2, specs.Count);
        Assert.Equal(SessionRole.Planner, specs[0].Role);
        Assert.Equal("bullshark-planner-ab12", specs[0].Name);
        Assert.Equal(SessionRole.Coder, specs[1].Role);
        Assert.Equal("bullshark-coder-ab12", specs[1].Name);
    }

    [Fact]
    public void PlanAndManyCodersNumbersEachCoderUniquely()
    {
        var specs = RunPlan.Build(Pattern(PatternKind.PlanAndManyCoders), WorkDir, "task", RunId, coders: 3);

        Assert.Equal(4, specs.Count);
        var names = specs.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(new[] { "bullshark-coder-ab12-1", "bullshark-coder-ab12-2", "bullshark-coder-ab12-3" }, names.Skip(1));
    }

    [Fact]
    public void SoloIsOnePlannerRoleSessionWithNoPeer()
    {
        var specs = RunPlan.Build(Pattern(PatternKind.Solo), WorkDir, "task", RunId);

        var only = Assert.Single(specs);
        Assert.Equal(SessionRole.Planner, only.Role);
        Assert.Equal("bullshark-planner-ab12", only.Name);
        Assert.Contains("Task: task", only.Brief);
        Assert.DoesNotContain("bullshark-planner-ab12", only.Brief);
        Assert.DoesNotContain("Your peer is", only.Brief);
        Assert.DoesNotContain("bullshark-coder", only.Brief);
        Assert.DoesNotContain("bullshark-coder", only.ResumePrompt);
    }

    [Fact]
    public void OneCoderBriefsNameTheirPeers()
    {
        var planner = RunPlan.PlannerBrief(TestSpecs.PlannerName, [TestSpecs.CoderName], "task");
        var coder = RunPlan.CoderBrief(TestSpecs.CoderName, TestSpecs.PlannerName);

        Assert.Contains($"Your peer is {TestSpecs.CoderName}", planner);
        Assert.Contains($"Your peer is {TestSpecs.PlannerName}", coder);
    }

    [Fact]
    public void ResumePromptsNameTheirPeers()
    {
        var specs = RunPlan.Build(Pattern(PatternKind.PlanAndCode), WorkDir, "task", RunId);

        Assert.Contains("bullshark-coder-ab12", specs[0].ResumePrompt);
        Assert.Contains("bullshark-planner-ab12", specs[1].ResumePrompt);
    }

    [Fact]
    public void EverySessionOfARunSharesOnePermissionMode()
    {
        var specs = RunPlan.Build(Pattern(PatternKind.PlanAndManyCoders), WorkDir, "task", RunId, coders: 2, permissionMode: "acceptEdits");

        Assert.All(specs, s => Assert.Equal("acceptEdits", s.PermissionMode));
    }

    [Fact]
    public void ACoderCountThatDoesNotFitThePatternIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunPlan.Build(Pattern(PatternKind.PlanAndCode), WorkDir, "task", RunId, coders: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunPlan.Build(Pattern(PatternKind.PlanAndManyCoders), WorkDir, "task", RunId, coders: 1));
    }

    [Fact]
    public void RunNamesAreFormattedAsDecided()
    {
        Assert.Equal("bullshark-planner-ab12", RunNames.Planner("ab12"));
        Assert.Equal("bullshark-coder-ab12", RunNames.Coder("ab12", 1, 1));
        Assert.Equal("bullshark-coder-ab12-2", RunNames.Coder("ab12", 2, 3));
    }

    [Fact]
    public void NewRunIdIsFourHexDigitsAndNeverAPartOfATakenName()
    {
        var taken = new[] { "bullshark-coder-ab12", "bullshark-planner-0f0f", "bullshark-coder-ab12-1" };
        var forbidden = taken.SelectMany(n => n.Split('-')).ToHashSet();

        for (int i = 0; i < 300; i++)
        {
            var id = RunNames.NewRunId(taken);
            Assert.Matches("^[0-9a-f]{4}$", id);
            Assert.DoesNotContain(id, forbidden);
        }
    }

}

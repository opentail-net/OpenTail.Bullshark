using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

public sealed class SetupPlanTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-setup-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _claudeJson;

    public SetupPlanTests()
    {
        Directory.CreateDirectory(_dir);
        _claudeJson = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PlainFolder()
    {
        var folder = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private SetupRequest Request(string folder, string pattern = "plan-code", int coders = 1, string task = "Build it") =>
        new(pattern, folder, task, coders, "opus", "high", "haiku", "medium");

    [Fact]
    public void AValidRequestPasses()
    {
        var check = SetupPlan.Validate(Request(PlainFolder()), _claudeJson);

        Assert.True(check.IsValid, string.Join("; ", check.Errors));
    }

    [Fact]
    public void AMissingFolderIsRefused()
    {
        var check = SetupPlan.Validate(Request(Path.Combine(_dir, "nope")), _claudeJson);

        Assert.Contains(check.Errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public void AnUntrustedRepositoryIsRefused()
    {
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(_claudeJson, "{\"projects\":{}}");

        var check = SetupPlan.Validate(Request(repo), _claudeJson);

        Assert.False(check.IsValid);
        Assert.Contains(check.Errors, e => e.Contains("not been told to trust"));
    }

    [Fact]
    public void AnEmptyTaskIsRefused()
    {
        var check = SetupPlan.Validate(Request(PlainFolder(), task: "   "), _claudeJson);

        Assert.Contains(check.Errors, e => e.Contains("task is empty"));
    }

    [Fact]
    public void AModelOrEffortOutsideTheOfferedListsIsRefused()
    {
        var request = Request(PlainFolder()) with { PlannerModel = "gpt", CoderEffort = "extreme" };

        var check = SetupPlan.Validate(request, _claudeJson);

        Assert.Contains(check.Errors, e => e.Contains("'gpt'"));
        Assert.Contains(check.Errors, e => e.Contains("'extreme'"));
    }

    [Fact]
    public void PlanAndCodeTakesExactlyOneCoderAndManyCodersTakesTwoToSix()
    {
        var folder = PlainFolder();

        Assert.Contains(SetupPlan.Validate(Request(folder, coders: 2), _claudeJson).Errors, e => e.Contains("exactly one coder"));
        Assert.Contains(SetupPlan.Validate(Request(folder, "plan-many", coders: 1), _claudeJson).Errors, e => e.Contains("2 to 6"));
        Assert.Contains(SetupPlan.Validate(Request(folder, "plan-many", coders: 7), _claudeJson).Errors, e => e.Contains("2 to 6"));
        Assert.True(SetupPlan.Validate(Request(folder, "plan-many", coders: 6), _claudeJson).IsValid);
    }

    [Fact]
    public void ModelsAndEffortsGoToTheRightRoles()
    {
        var request = Request(PlainFolder(), "plan-many", coders: 2) with
        {
            PlannerModel = "sonnet", PlannerEffort = "low", CoderModel = "fable", CoderEffort = "max",
        };

        var specs = SetupPlan.Build(request, "ab12");

        Assert.Equal(("sonnet", "low"), (specs[0].Model, specs[0].Effort));
        Assert.All(specs.Skip(1), s => Assert.Equal(("fable", "max"), (s.Model, s.Effort)));
    }

    [Fact]
    public void OwnerTextIsAddedToItsOwnBriefUnderALabelAndTheBaseBriefIsKept()
    {
        var request = Request(PlainFolder()) with { PlannerExtra = "Prefer small batches.", CoderExtra = "Run the tests first." };

        var specs = SetupPlan.Build(request, "ab12");
        var baseSpecs = RunPlan.Build(PatternSpec.Find("plan-code")!, Path.GetFullPath(request.Folder), "Build it", "ab12", reviewer: "sonnet");

        Assert.Equal(baseSpecs[0].Brief + "\n\nFrom the owner: Prefer small batches.", specs[0].Brief);
        Assert.Equal(baseSpecs[1].Brief + "\n\nFrom the owner: Run the tests first.", specs[1].Brief);
        Assert.Contains("Do not ask questions", specs[0].Brief);
    }

    [Fact]
    public void BlankOwnerTextLeavesTheBriefUnchanged()
    {
        var folder = PlainFolder();
        var specs = SetupPlan.Build(Request(folder), "ab12");
        var baseSpecs = RunPlan.Build(PatternSpec.Find("plan-code")!, Path.GetFullPath(folder), "Build it", "ab12", reviewer: "sonnet");

        Assert.Equal(baseSpecs[0].Brief, specs[0].Brief);
        Assert.Equal(baseSpecs[1].Brief, specs[1].Brief);
    }

    [Fact]
    public void ResumePromptsAreNotChangedByTheOwnersText()
    {
        var request = Request(PlainFolder()) with { CoderExtra = "Something extra." };

        var specs = SetupPlan.Build(request, "ab12");

        Assert.Equal(RunPlan.CoderResumePrompt("bullshark-planner-ab12"), specs[1].ResumePrompt);
        Assert.DoesNotContain("Something extra.", specs[1].ResumePrompt);
    }

    [Fact]
    public void SoloIsOneSessionWithThePlannerSettings()
    {
        var request = Request(PlainFolder(), "solo", coders: 0) with { PlannerModel = "sonnet", PlannerExtra = "Keep it short." };

        var specs = SetupPlan.Build(request, "ab12");

        var only = Assert.Single(specs);
        Assert.Equal(SessionRole.Planner, only.Role);
        Assert.Equal("sonnet", only.Model);
        Assert.EndsWith("From the owner: Keep it short.", only.Brief);
    }
}

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
    public void OwnerTextIsNotAddedWhenNoBriefIsSaved()
    {
        var request = Request(PlainFolder()) with { PlannerExtra = "Prefer small batches.", CoderExtra = "Run the tests first." };

        var specs = SetupPlan.Build(request, "ab12");
        var baseSpecs = RunPlan.Build(PatternSpec.Find("plan-code")!, Path.GetFullPath(request.Folder), "Build it", "ab12", reviewer: "sonnet");

        Assert.Equal(baseSpecs[0].Brief, specs[0].Brief);
        Assert.Equal(baseSpecs[1].Brief, specs[1].Brief);
        Assert.DoesNotContain("From the owner", specs[0].Brief);
    }

    [Fact]
    public void WithNoSavedBriefTheLaunchedBriefIsTheExpandedTemplate()
    {
        var request = Request(PlainFolder()) with { PlannerExtra = "Prefer small batches." };

        var launched = SetupPlan.Build(request, "ab12");
        var expanded = SetupPlan.Build(request with
        {
            PlannerBrief = SetupPlan.DefaultBriefTemplate(request, SessionRole.Planner),
            CoderBrief = SetupPlan.DefaultBriefTemplate(request, SessionRole.Coder),
        }, "ab12");

        Assert.Equal(expanded.Select(s => s.Brief), launched.Select(s => s.Brief));
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
    public void SavedBriefsExpandTaskSessionNamesAndReviewerForEachCoder()
    {
        var request = Request(PlainFolder(), "plan-many", coders: 2) with
        {
            Reviewer = "opus",
            Assistant = "reviewer",
            PlannerBrief = "Task: {task}. {plannername} reviews {codernames} with {reviewername}.",
            CoderBrief = "{codername} reports to {plannername} and asks {reviewername}: {task}.",
        };

        var specs = SetupPlan.Build(request, "ab12");
        var planner = specs[0];
        var coders = specs.Where(s => s.Role == SessionRole.Coder).ToList();
        var assistant = specs.Single(s => s.Role == SessionRole.Assistant);

        Assert.Equal($"Task: Build it. {planner.Name} reviews {coders[0].Name}, {coders[1].Name} with opus.", planner.Brief);
        Assert.Equal($"{coders[0].Name} reports to {planner.Name} and asks {assistant.Name}: Build it.", coders[0].Brief);
        Assert.Equal($"{coders[1].Name} reports to {planner.Name} and asks {assistant.Name}: Build it.", coders[1].Brief);

        var selfRequest = request with
        {
            Reviewer = "self",
            Assistant = "none",
            PlannerBrief = "{reviewername}",
            CoderBrief = "{reviewername}",
        };
        var selfSpecs = SetupPlan.Build(selfRequest, "cd34");
        Assert.All(selfSpecs.Where(s => s.Role is SessionRole.Planner or SessionRole.Coder), s =>
            Assert.Equal(selfSpecs[0].Name, s.Brief));
    }

    [Fact]
    public void DefaultTemplatesUseTagsAndEndWithTheClauseTagOfTheirRole()
    {
        var request = Request(PlainFolder()) with { Assistant = "haiku", Reviewer = "opus" };

        var planner = SetupPlan.DefaultBriefTemplate(request, SessionRole.Planner);
        var coder = SetupPlan.DefaultBriefTemplate(request, SessionRole.Coder);

        Assert.Contains("{task}", planner);
        Assert.Contains("{plannername}", planner);
        Assert.EndsWith("{reviewclause}", planner);
        Assert.Contains("{codername}", coder);
        Assert.Contains("{plannername}", coder);
        Assert.EndsWith("{assistantclause}", coder);
        Assert.DoesNotContain("{reviewername}", planner);
        Assert.DoesNotContain("{assistantname}", coder);
        Assert.Contains("{codernames}", SetupPlan.DefaultBriefTemplate(Request(PlainFolder(), "plan-many", coders: 2), SessionRole.Planner));
        Assert.DoesNotContain("xxxx", planner);
        Assert.DoesNotContain("xxxx", coder);
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
        Assert.DoesNotContain("From the owner", only.Brief);
    }

    [Fact]
    public void TheDefaultCoderTemplateDoesNotDependOnTheAssistant()
    {
        var none = Request(PlainFolder(), "plan-many", coders: 2) with { Assistant = "none" };

        Assert.Equal(SetupPlan.DefaultBriefTemplate(none, SessionRole.Coder),
            SetupPlan.DefaultBriefTemplate(none with { Assistant = "sonnet" }, SessionRole.Coder));
        Assert.Equal(SetupPlan.DefaultBriefTemplate(none, SessionRole.Coder),
            SetupPlan.DefaultBriefTemplate(none with { Assistant = "reviewer", Reviewer = "opus" }, SessionRole.Coder));
    }

    [Fact]
    public void TheDefaultPlannerTemplateDoesNotDependOnTheReviewer()
    {
        var self = Request(PlainFolder(), "plan-many", coders: 2) with { Reviewer = "self" };

        Assert.Equal(SetupPlan.DefaultBriefTemplate(self, SessionRole.Planner),
            SetupPlan.DefaultBriefTemplate(self with { Reviewer = "opus" }, SessionRole.Planner));
    }

    [Fact]
    public void ASoloPlannerTemplateHasNoReviewClause()
    {
        var solo = Request(PlainFolder(), "solo", coders: 0);

        Assert.DoesNotContain("{reviewclause}", SetupPlan.DefaultBriefTemplate(solo, SessionRole.Planner));
    }

    [Theory]
    [InlineData("self", "none")]
    [InlineData("opus", "none")]
    [InlineData("self", "reviewer")]
    [InlineData("opus", "reviewer")]
    [InlineData("sonnet", "sonnet")]
    public void ADefaultTemplateExpandedForASettingIsTheBriefRunPlanGenerates(string reviewer, string assistant)
    {
        var request = Request(PlainFolder(), "plan-many", coders: 2) with { Reviewer = reviewer, Assistant = assistant };
        var reference = RunPlan.Build(PatternSpec.Find("plan-many")!, Path.GetFullPath(request.Folder), request.Task.Trim(), "ab12",
            request.Coders, request.PermissionMode, request.Reviewer, SetupPlan.ChoiceOf(request));

        var withDefaults = request with
        {
            PlannerBrief = SetupPlan.DefaultBriefTemplate(request, SessionRole.Planner),
            CoderBrief = SetupPlan.DefaultBriefTemplate(request, SessionRole.Coder),
        };
        var expanded = SetupPlan.Build(withDefaults, "ab12");

        Assert.Equal(reference.Select(s => s.Brief), expanded.Select(s => s.Brief));
    }

    [Fact]
    public void AnAssistantClauseWithNoAssistantLeavesNoTextAndNoGap()
    {
        var request = Request(PlainFolder()) with
        {
            Assistant = "none",
            CoderBrief = "You are {codername}, the coder. Do the work.{assistantclause} Report to {plannername}.",
        };

        var specs = SetupPlan.Build(request, "ab12");
        var coder = specs.Single(s => s.Role == SessionRole.Coder);
        var planner = specs.Single(s => s.Role == SessionRole.Planner);

        Assert.DoesNotContain("Your assistant", coder.Brief);
        Assert.DoesNotContain("Your reviewer", coder.Brief);
        Assert.DoesNotContain("  ", coder.Brief);
        Assert.Equal($"You are {coder.Name}, the coder. Do the work. Report to {planner.Name}.", coder.Brief);
    }

    [Fact]
    public void ATagTypedInsideTheTaskIsNotExpandedAgain()
    {
        var request = Request(PlainFolder()) with
        {
            Task = "Write {plannername} and {assistantclause} as typed",
            PlannerBrief = "Task: {task}. {plannername} plans it.",
        };

        var planner = SetupPlan.Build(request, "ab12").Single(s => s.Role == SessionRole.Planner);

        Assert.Equal($"Task: Write {{plannername}} and {{assistantclause}} as typed. {planner.Name} plans it.", planner.Brief);
    }
}

using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

public sealed class RunCreationTests
{
    private static SetupRequest Request(string folder, string pattern, int coders) =>
        new(pattern, folder, "Build it", coders, "opus", "high", "haiku", "medium");

    [Fact]
    public void EverySessionOfARunNamesTheRunItBelongsTo()
    {
        // The timeline tells a reused session name apart by the run id on each event (round 5 item 10), which comes from the spec.
        var run = SetupPlan.CreateRun(Request(Path.GetTempPath(), "plan-many", 2), ["ovc-coder"], DateTimeOffset.UtcNow);

        Assert.NotEmpty(run.Specs);
        Assert.All(run.Specs, spec => Assert.Equal(run.RunId, spec.RunId));
    }

    [Fact]
    public void ANewRunHasAFreshIdAndNamesNoExistingNameUses()
    {
        var taken = new[] { "ovc-planner", "ovc-coder", "bullshark-planner-ab12" };

        var run = SetupPlan.CreateRun(Request(Path.GetTempPath(), "plan-code", 1), taken, DateTimeOffset.UtcNow);

        Assert.Matches("^[0-9a-f]{4}$", run.RunId);
        Assert.Equal(2, run.Specs.Count);
        Assert.DoesNotContain(run.Specs.Select(s => s.Name), name => taken.Contains(name));
    }

    [Fact]
    public void ARunKeepsItsPatternFolderAndOnePermissionMode()
    {
        var folder = Path.GetTempPath();

        var run = SetupPlan.CreateRun(Request(folder, "plan-many", 3), [], DateTimeOffset.UtcNow);

        Assert.Equal("plan-many", run.PatternKey);
        Assert.Equal(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), run.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal(4, run.Specs.Count);
        Assert.All(run.Specs, s => Assert.Equal("auto", s.PermissionMode));
        Assert.Equal(run.Specs.Count, run.Specs.Select(s => s.Name).Distinct().Count());
    }
}

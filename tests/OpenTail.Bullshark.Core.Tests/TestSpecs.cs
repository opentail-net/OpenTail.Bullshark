using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Tests;

/// <summary>
/// Test fixtures for a planner and a coder. They are not production defaults: the app builds its specs from a pattern
/// (RunPlan.Build). The names are fixture names; the old ovc- names exist only in the tests that read old state files.
/// </summary>
public static class TestSpecs
{
    public const string PlannerName = "test-planner";
    public const string CoderName = "test-coder";

    public static SessionSpec Planner(string workingDirectory, string task) =>
        new(SessionRole.Planner, PlannerName, workingDirectory, "opus", "high", PlannerBrief(task), "auto");

    public static SessionSpec Coder(string workingDirectory) =>
        new(SessionRole.Coder, CoderName, workingDirectory, "haiku", "medium", CoderBrief(), "auto");

    public static string PlannerBrief(string task) => RunPlan.PlannerBrief(PlannerName, [CoderName], task);

    public static string CoderBrief() => RunPlan.CoderBrief(CoderName, PlannerName);
}

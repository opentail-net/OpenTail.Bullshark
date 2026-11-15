using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>The choices the setup page offers (D-S5, D-S19). Models and levels are the CLI's aliases and levels.</summary>
public static class Choices
{
    /// <summary>Claude's model aliases. Fable is the most expensive, so the page marks it.</summary>
    public static IReadOnlyList<string> Models { get; } = ["haiku", "sonnet", "opus", "fable"];

    /// <summary>The effort levels from `claude --help`. Whether each model accepts each level is not verified (S9c).</summary>
    public static IReadOnlyList<string> Efforts { get; } = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>
    /// The reviewers the setup page offers (D-S36). "self" is the planner reviewing in its own session, the baseline that costs
    /// the most. Any other value is a model the planner runs its review through, in a subagent. Sonnet is the default.
    /// </summary>
    public static IReadOnlyList<string> Reviewers { get; } = ["self", "sonnet", "opus", "fable"];

    /// <summary>The cap for plan &gt; many coders (D-S19).</summary>
    public const int MaxCoders = 6;

    /// <summary>The fewest coders in plan &gt; many coders.</summary>
    public const int MinManyCoders = 2;
}

/// <summary>What the owner chose on the setup page. Nothing here is written until the owner presses Start.</summary>
public sealed record SetupRequest(
    string PatternKey,
    string Folder,
    string Task,
    int Coders,
    string PlannerModel,
    string PlannerEffort,
    string CoderModel,
    string CoderEffort,
    string PlannerExtra = "",
    string CoderExtra = "",
    string PermissionMode = "auto",
    string Reviewer = "sonnet");

/// <summary>The result of checking a request: the problems to show, and the folder check.</summary>
public sealed record SetupCheck(IReadOnlyList<string> Errors, FolderCheckResult? Folder)
{
    public bool IsValid => Errors.Count == 0;
}

public static class SetupPlan
{
    /// <summary>
    /// Checks a request without writing anything. The folder must exist. An untrusted repository is refused, because
    /// background sessions would not start there (D-S2, F24). Trust is accepted in Claude, never by OpenTail.Bullshark.
    /// </summary>
    public static SetupCheck Validate(SetupRequest request, string claudeJsonPath)
    {
        var errors = new List<string>();

        var pattern = PatternSpec.Find(request.PatternKey);
        if (pattern is null) errors.Add($"Unknown pattern '{request.PatternKey}'.");

        var folder = FolderCheck.Check(request.Folder ?? string.Empty, claudeJsonPath);
        if (folder.Status == FolderStatus.Missing) errors.Add(folder.Message);
        if (folder.Status == FolderStatus.NotTrusted)
            errors.Add(folder.Message + " The Trust step for a chosen folder comes in S5.");

        if (string.IsNullOrWhiteSpace(request.Task))
            errors.Add("The task is empty. Say what the sessions should do.");

        if (!Choices.Models.Contains(request.PlannerModel)) errors.Add($"Planner model '{request.PlannerModel}' is not offered.");
        if (!Choices.Models.Contains(request.CoderModel)) errors.Add($"Coder model '{request.CoderModel}' is not offered.");
        if (!Choices.Efforts.Contains(request.PlannerEffort)) errors.Add($"Planner effort '{request.PlannerEffort}' is not offered.");
        if (!Choices.Efforts.Contains(request.CoderEffort)) errors.Add($"Coder effort '{request.CoderEffort}' is not offered.");
        if (!Choices.Reviewers.Contains(request.Reviewer)) errors.Add($"Reviewer '{request.Reviewer}' is not offered.");

        if (pattern is not null)
        {
            if (pattern.Kind == PatternKind.PlanAndCode && request.Coders != 1)
                errors.Add("plan > code has exactly one coder.");
            if (pattern.Kind == PatternKind.PlanAndManyCoders &&
                (request.Coders < Choices.MinManyCoders || request.Coders > Choices.MaxCoders))
                errors.Add($"plan > many coders needs {Choices.MinManyCoders} to {Choices.MaxCoders} coders.");
        }

        return new SetupCheck(errors, folder);
    }

    /// <summary>
    /// The sessions of a run as they would start: the pattern's sessions, with the chosen models and efforts, and the
    /// owner's extra text added to the brief of each role. The base brief, with its peer names, "do not ask questions"
    /// and the SendMessage handoff, is kept (D-S15, F21). Call only with a request that passed <see cref="Validate"/>.
    /// </summary>
    public static IReadOnlyList<SessionSpec> Build(SetupRequest request, string runId)
    {
        var pattern = PatternSpec.Find(request.PatternKey)
                      ?? throw new ArgumentException($"Unknown pattern '{request.PatternKey}'.", nameof(request));
        var coders = pattern.Kind == PatternKind.PlanAndCode ? 1 : request.Coders;

        var sessions = RunPlan.Build(pattern, Path.GetFullPath(request.Folder.Trim()), request.Task.Trim(), runId,
            coders, request.PermissionMode, request.Reviewer);

        return sessions.Select(s => s.Role == SessionRole.Planner
            ? s with
            {
                Model = request.PlannerModel,
                Effort = request.PlannerEffort,
                Brief = WithOwnerText(s.Brief, request.PlannerExtra),
            }
            : s with
            {
                Model = request.CoderModel,
                Effort = request.CoderEffort,
                Brief = WithOwnerText(s.Brief, request.CoderExtra),
            }).ToList();
    }

    /// <summary>
    /// The run a valid request creates. Its id is new, and no name in <paramref name="takenNames"/> uses it (F33): the
    /// names of the state file, of runs.json and of the agent listing are all passed in.
    /// </summary>
    public static RunRecord CreateRun(SetupRequest request, IEnumerable<string> takenNames, DateTimeOffset now)
    {
        var runId = RunNames.NewRunId(takenNames);
        var specs = Build(request, runId);
        return new RunRecord(runId, request.PatternKey, Path.GetFullPath(request.Folder.Trim()),
            request.PermissionMode, now, specs);
    }

    /// <summary>Appends the owner's text under a clear label. Blank text leaves the brief unchanged.</summary>
    public static string WithOwnerText(string brief, string extra) =>
        string.IsNullOrWhiteSpace(extra) ? brief : brief + "\n\nFrom the owner: " + extra.Trim();
}

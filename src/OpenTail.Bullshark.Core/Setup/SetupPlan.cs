using System.Text.RegularExpressions;
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
    string Reviewer = "sonnet",
    string Assistant = "none",
    string AssistantEffort = "medium",
    string? PlannerBrief = null,
    string? CoderBrief = null,
    DateTimeOffset? StartAt = null);

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
        if (request.Assistant is not ("none" or "reviewer") && !Choices.Models.Contains(request.Assistant))
            errors.Add($"Assistant model '{request.Assistant}' is not offered.");
        if (request.Assistant != "none" && !Choices.Efforts.Contains(request.AssistantEffort))
            errors.Add($"Assistant effort '{request.AssistantEffort}' is not offered.");
        if (request.Assistant != "none" && pattern?.Kind == PatternKind.Solo)
            errors.Add("A solo run has no coder, so it has no assistant.");

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
    /// The sessions of a run as they would start: the pattern's sessions, with chosen models and efforts. The brief is the saved
    /// brief, or the built-in template when none is saved, expanded for this launch. Call only with a request that passed <see cref="Validate"/>.
    /// </summary>
    public static IReadOnlyList<SessionSpec> Build(SetupRequest request, string runId)
    {
        var pattern = PatternSpec.Find(request.PatternKey)
                      ?? throw new ArgumentException($"Unknown pattern '{request.PatternKey}'.", nameof(request));
        var coders = pattern.Kind == PatternKind.PlanAndCode ? 1 : request.Coders;

        var sessions = RunPlan.Build(pattern, Path.GetFullPath(request.Folder.Trim()), request.Task.Trim(), runId,
            coders, request.PermissionMode, request.Reviewer, ChoiceOf(request));

        var configured = sessions.Select(s => s.Role switch
        {
            SessionRole.Planner => s with
            {
                Model = request.PlannerModel,
                Effort = request.PlannerEffort,
            },
            SessionRole.Assistant => s with
            {
                Model = request.Assistant == "reviewer" ? request.Reviewer : request.Assistant,
                Effort = request.AssistantEffort,
            },
            _ => s with
            {
                Model = request.CoderModel,
                Effort = request.CoderEffort,
            },
        }).ToList();

        var plannerName = configured.First(s => s.Role == SessionRole.Planner).Name;
        var coderNames = configured.Where(s => s.Role == SessionRole.Coder).Select(s => s.Name).ToList();
        var assistantName = configured.FirstOrDefault(s => s.Role == SessionRole.Assistant)?.Name;
        return configured.Select(s =>
        {
            // The brief a session starts with is the saved brief, or the built-in template when none is saved. Either way it is
            // expanded here, at launch, because the names carry the run id and the task changes each run.
            var source = s.Role switch
            {
                SessionRole.Planner => request.PlannerBrief ?? DefaultBriefTemplate(request, SessionRole.Planner),
                SessionRole.Coder => request.CoderBrief ?? DefaultBriefTemplate(request, SessionRole.Coder),
                _ => null,
            };
            if (source is null) return s;

            var coderName = s.Role == SessionRole.Coder ? s.Name : coderNames.FirstOrDefault() ?? string.Empty;
            var reviewerName = request.Reviewer == "self" ? plannerName : request.Reviewer;
            if (s.Role == SessionRole.Coder && request.Assistant == "reviewer" && assistantName is not null)
                reviewerName = assistantName;

            // The assistant as a coder's brief names it: the assistant session, or the planner when the reviewer is the assistant.
            var helper = ChoiceOf(request) switch
            {
                AssistantChoice.Session => assistantName,
                AssistantChoice.Planner => plannerName,
                _ => null,
            };
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["task"] = request.Task.Trim(),
                ["plannername"] = plannerName,
                ["codername"] = coderName,
                ["codernames"] = string.Join(", ", coderNames),
                ["reviewername"] = reviewerName,
                ["assistantname"] = assistantName ?? string.Empty,
                // The clause tags expand to the sentence the generated brief would add for the current settings, or to nothing.
                ["reviewclause"] = s.Role == SessionRole.Planner ? RunPlan.ReviewClause(request.Reviewer) : string.Empty,
                ["assistantclause"] = s.Role == SessionRole.Coder ? RunPlan.AssistantClause(helper, plannerName) : string.Empty,
            };

            // One pass: Regex.Replace does not rescan what it inserted, so a tag typed in the task text stays as typed.
            var brief = Regex.Replace(source,
                @"\{(task|plannername|codername|codernames|reviewername|assistantname|reviewclause|assistantclause)\}",
                match => values[match.Groups[1].Value]);
            return s with { Brief = brief };
        }).ToList();
    }

    /// <summary>
    /// The built-in template for a role. The app owns it: a brief starts as this template, and with no saved brief it is what a
    /// session starts with. It is the run's brief with no assistant and the reviewer "self", with the names as tags, and the clause
    /// tag for the role at its end. So the same text serves every reviewer and assistant choice. It does not read the owner's old
    /// extra text (PlannerExtra and CoderExtra), which is no longer used.
    /// </summary>
    public static string DefaultBriefTemplate(SetupRequest request, SessionRole role)
    {
        var pattern = PatternSpec.Find(request.PatternKey)
                      ?? throw new ArgumentException($"Unknown pattern '{request.PatternKey}'.", nameof(request));
        var coders = pattern.Kind == PatternKind.PlanAndCode ? 1 : request.Coders;
        var task = request.Task.Trim();

        // RunPlan, not Build: Build expands templates, so it must not be asked for one.
        var sessions = RunPlan.Build(pattern, Path.GetFullPath(request.Folder.Trim()), task, "xxxx", coders,
            request.PermissionMode, "self", AssistantChoice.None);
        var selected = sessions.FirstOrDefault(s => s.Role == role)
                       ?? throw new ArgumentException($"The pattern has no {role} brief.", nameof(role));
        var template = selected.Brief;
        var planner = sessions.First(s => s.Role == SessionRole.Planner).Name;
        var coderNames = sessions.Where(s => s.Role == SessionRole.Coder).Select(s => s.Name).ToList();

        if (coderNames.Count > 1)
            template = template.Replace(string.Join(", ", coderNames), "{codernames}", StringComparison.Ordinal);
        foreach (var coder in coderNames)
            template = template.Replace(coder, "{codername}", StringComparison.Ordinal);
        template = template.Replace(planner, "{plannername}", StringComparison.Ordinal);
        if (task.Length > 0)
            template = template.Replace($"Task: {task}", "Task: {task}", StringComparison.Ordinal);

        return role switch
        {
            // A solo planner has no reviewer and no coder, so its template gets no clause.
            SessionRole.Planner when coderNames.Count > 0 => template + "{reviewclause}",
            SessionRole.Coder => template + "{assistantclause}",
            _ => template,
        };
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
            request.PermissionMode, now, specs, request.StartAt);
    }

    /// <summary>
    /// How the assistant runs for a request (D-S41). "reviewer" uses the reviewer's setting: the planner when the reviewer is self,
    /// otherwise a separate session on the reviewer's model.
    /// </summary>
    public static AssistantChoice ChoiceOf(SetupRequest request) => request.Assistant switch
    {
        "none" => AssistantChoice.None,
        "reviewer" => request.Reviewer == "self" ? AssistantChoice.Planner : AssistantChoice.Session,
        _ => AssistantChoice.Session,
    };
}

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>The three patterns the setup page offers (D-S4). The key is stored in runs.json.</summary>
public enum PatternKind
{
    PlanAndCode,
    PlanAndManyCoders,
    Solo,
}

/// <summary>One pattern as data: its kind, a stable key, the title shown on the setup page, and what it does.</summary>
public sealed record PatternSpec(PatternKind Kind, string Key, string Title, string Description)
{
    public static IReadOnlyList<PatternSpec> Shipped { get; } =
    [
        new(PatternKind.PlanAndCode, "plan-code", "plan > code > review",
            "One planner and one coder. The planner reviews every coder report (D-S10)."),
        new(PatternKind.PlanAndManyCoders, "plan-many", "plan > many coders > review",
            "One planner and several coders. The planner reviews every report (D-S10)."),
        new(PatternKind.Solo, "solo", "solo",
            "One session plans and implements. No peer, and no self-planning is promised."),
    ];

    public static PatternSpec? Find(string key) =>
        Shipped.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
}

/// <summary>Names of the sessions in a run (D-S13). The default pair from before runs.json keeps its plain names.</summary>
public static class RunNames
{
    public static string Planner(string runId) => $"{SessionSpec.RunPlannerPrefix}-{runId}";

    /// <summary>One coder is named with the run id alone. Several coders are numbered from 1.</summary>
    public static string Coder(string runId, int index, int count) =>
        count == 1 ? $"{SessionSpec.RunCoderPrefix}-{runId}" : $"{SessionSpec.RunCoderPrefix}-{runId}-{index}";

    /// <summary>
    /// A new run id: four lowercase hex digits. It never equals a part of a name already in use, so a new run
    /// cannot clash with a running session's name (F33).
    /// </summary>
    public static string NewRunId(IEnumerable<string> takenNames)
    {
        var taken = takenNames.SelectMany(n => n.Split('-')).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var id = Guid.NewGuid().ToString("N")[..4];
            if (!taken.Contains(id)) return id;
        }
    }
}

/// <summary>
/// Builds the session specs of a run from a pattern. The one-coder briefs with the default names are the same text
/// as the existing <see cref="SessionSpec"/> defaults, so the default pair keeps its wording (tested).
/// </summary>
public static class RunPlan
{
    public const string PlannerModel = "opus";
    public const string PlannerEffort = "high";
    public const string CoderModel = "haiku";
    public const string CoderEffort = "medium";

    public const string SoloResumePrompt =
        "You were restarted and are continuing where you left off. Do not ask questions: make the sensible decision. " +
        "Carry on with the current batch and its tests. If nothing is pending, reply with one line saying so.";

    /// <summary>The run that exists before runs.json: the unsuffixed default pair (D-S13).</summary>
    public static IReadOnlyList<SessionSpec> Legacy(string workDir, string task) =>
        [SessionSpec.DefaultPlanner(workDir, task), SessionSpec.DefaultCoder(workDir)];

    /// <summary>
    /// The sessions of one run. Every session gets the same permission mode (F22, D-S14). Plan > code takes exactly one
    /// coder, plan > many coders takes at least two, and solo takes none (coders is ignored).
    /// </summary>
    public static IReadOnlyList<SessionSpec> Build(
        PatternSpec pattern,
        string workDir,
        string task,
        string runId,
        int coders = 1,
        string permissionMode = "auto",
        string reviewer = "self")
    {
        switch (pattern.Kind)
        {
            case PatternKind.PlanAndCode when coders != 1:
                throw new ArgumentOutOfRangeException(nameof(coders), "plan > code has exactly one coder.");
            case PatternKind.PlanAndManyCoders when coders < 2:
                throw new ArgumentOutOfRangeException(nameof(coders), "plan > many coders needs at least two coders.");
        }

        var planner = RunNames.Planner(runId);

        if (pattern.Kind == PatternKind.Solo)
        {
            return
            [
                new SessionSpec(SessionRole.Planner, planner, workDir, PlannerModel, PlannerEffort,
                    SoloBrief(planner, task), permissionMode, SoloResumePrompt),
            ];
        }

        var coderNames = Enumerable.Range(1, coders).Select(i => RunNames.Coder(runId, i, coders)).ToList();

        var specs = new List<SessionSpec>
        {
            new(SessionRole.Planner, planner, workDir, PlannerModel, PlannerEffort,
                PlannerBrief(planner, coderNames, task, reviewer), permissionMode, PlannerResumePrompt(coderNames)),
        };
        specs.AddRange(coderNames.Select(coder => new SessionSpec(
            SessionRole.Coder, coder, workDir, CoderModel, CoderEffort,
            CoderBrief(coder, planner), permissionMode, CoderResumePrompt(planner))));
        return specs;
    }

    /// <summary>The planner's brief. It names its coders, and it does not invite questions.</summary>
    public static string PlannerBrief(string planner, IReadOnlyList<string> coders, string task, string reviewer = "self")
    {
        var peers = coders.Count == 1
            ? $"Your peer is {coders[0]}, the coder."
            : $"Your peers are {string.Join(", ", coders)}, the coders.";
        var send = coders.Count == 1
            ? $"Send each batch to {coders[0]} with SendMessage"
            : "Send each batch to one coder by name with SendMessage";
        var reply = coders.Count == 1
            ? $"When {coders[0]} reports back"
            : "When a coder reports back";
        var askers = coders.Count == 1 ? coders[0] : "a coder";

        return $"You are {planner}, the planner. {peers} " +
               $"Task: {task} " +
               $"Plan it in small batches. {send}, with the full batch text in the message. " +
               "Do not ask questions: make the sensible decision and say what you decided in the message. " +
               $"{reply}, review the report and send the next batch or a fix. " +
               $"If {askers} sends you a question, answer it in one short message with SendMessage, and say what you decided." +
               ReviewClause(reviewer);
    }

    /// <summary>
    /// The sentence that sends each review to a subagent on the chosen model (D-S36). "self" adds nothing, so the baseline
    /// keeps the wording of the default pair. The criteria are part of each batch, so the reviewer has something to check against.
    /// </summary>
    public static string ReviewClause(string reviewer) => reviewer == "self"
        ? string.Empty
        : $" Put each batch's acceptance criteria in its message. For each report, review it through a subagent with model {reviewer}: " +
          "give it the batch text, the acceptance criteria, the report and the working folder, and ask for a short verdict: accept, " +
          "or a numbered list of findings with a file and line each. You decide, then tell the coder.";

    /// <summary>
    /// The coder's brief. Coders report to the planner only, so the brief names the planner and no other coder
    /// (decision D-S16).
    /// </summary>
    public static string CoderBrief(string coder, string planner) =>
        $"You are {coder}, the coder. Your peer is {planner}, the planner. " +
        $"Wait for batches from {planner}; they arrive as messages. Do each batch fully, with tests. " +
        $"When a batch is done, send a report to {planner} with SendMessage: what changed, the test results, and anything you decided. " +
        $"If you are stuck, or a choice changes the design, send {planner} one short question with SendMessage and carry on with the parts you can do. " +
        "Do not stop to wait for the answer. Otherwise make the sensible decision and say what you decided.";

    /// <summary>Solo's brief: one session, no peer, and no message to send.</summary>
    public static string SoloBrief(string name, string task) =>
        $"You are {name}, the only session in this run. There is no peer, so send no messages. " +
        $"Task: {task} " +
        "Plan it in small batches, do each batch fully with tests, and say what you decided after each batch. " +
        "Do not ask questions: make the sensible decision and say what you decided.";

    public static string PlannerResumePrompt(IReadOnlyList<string> coders) =>
        "You were restarted and are continuing where you left off. Do not ask questions: make the sensible decision. " +
        $"Tell {string.Join(", ", coders)} what you decided, and ask the coder with the current batch to resend it, with SendMessage. " +
        "If nothing is pending, reply with one line saying so.";

    public static string CoderResumePrompt(string planner) =>
        "You were restarted and are continuing where you left off. Do not ask questions: make the sensible decision. " +
        $"Tell {planner} what you decided, and ask it to resend the current batch, with SendMessage. " +
        "If nothing is pending, reply with one line saying so.";
}

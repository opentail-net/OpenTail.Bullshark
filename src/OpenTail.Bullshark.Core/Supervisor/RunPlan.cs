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

/// <summary>
/// How a run's assistant works (D-S41). None: the coders have no assistant. Planner: the reviewer is self, so the planner answers
/// the coders' questions in its own session, and no session is added. Session: a separate assistant session answers them.
/// </summary>
public enum AssistantChoice
{
    None,
    Planner,
    Session,
}

/// <summary>Names of the sessions in a run (D-S13). The default pair from before runs.json keeps its plain names.</summary>
public static class RunNames
{
    public static string Planner(string runId) => $"{SessionSpec.RunPlannerPrefix}-{runId}";

    /// <summary>The run's assistant, when it has one (D-S41).</summary>
    public static string Assistant(string runId) => $"bullshark-assistant-{runId}";

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

    /// <summary>The assistant's model and effort when the owner has not chosen them. The setup page always chooses.</summary>
    public const string AssistantModel = "sonnet";
    public const string AssistantEffort = "medium";

    public const string SoloResumePrompt =
        "You were restarted and are continuing where you left off. Do not ask questions: make the sensible decision. " +
        "Carry on with the current batch and its tests. If nothing is pending, reply with one line saying so.";

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
        string reviewer = "self",
        AssistantChoice assistant = AssistantChoice.None)
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
                    SoloBrief(task), permissionMode, SoloResumePrompt, runId),
            ];
        }

        var coderNames = Enumerable.Range(1, coders).Select(i => RunNames.Coder(runId, i, coders)).ToList();
        var helper = assistant switch
        {
            AssistantChoice.Session => RunNames.Assistant(runId),
            AssistantChoice.Planner => planner,
            _ => null,
        };

        var specs = new List<SessionSpec>
        {
            new(SessionRole.Planner, planner, workDir, PlannerModel, PlannerEffort,
                PlannerBrief(planner, coderNames, task, reviewer), permissionMode, PlannerResumePrompt(coderNames), runId),
        };
        specs.AddRange(coderNames.Select(coder => new SessionSpec(
            SessionRole.Coder, coder, workDir, CoderModel, CoderEffort,
            CoderBrief(coder, planner, helper), permissionMode, CoderResumePrompt(planner), runId)));
        // helper is set exactly when the assistant is a session, so the name is read here as a non-null value.
        if (assistant == AssistantChoice.Session && helper is { } assistantName)
            specs.Add(new SessionSpec(SessionRole.Assistant, assistantName, workDir, AssistantModel, AssistantEffort,
                AssistantBrief(assistantName, coderNames, planner), permissionMode, AssistantResumePrompt, runId));
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
    public static string CoderBrief(string coder, string planner, string? assistant = null) =>
        $"You are {coder}, the coder. Your peer is {planner}, the planner. " +
        $"Wait for batches from {planner}; they arrive as messages. Do each batch fully, with tests. " +
        $"When a batch is done, send a report to {planner} with SendMessage: what changed, the test results, and anything you decided. " +
        $"If you are stuck, or a choice changes the design, send {planner} one short question with SendMessage and carry on with the parts you can do. " +
        "Do not stop to wait for the answer. Otherwise make the sensible decision and say what you decided." +
        AssistantClause(assistant, planner);

    /// <summary>
    /// The sentence a coder's brief adds for its assistant (D-S41). When the assistant setting is "reviewer" and the reviewer is
    /// the planner, the sentence names the planner as the reviewer. It starts with a space, and it is empty when there is no assistant.
    /// The coder's brief and the {assistantclause} tag of a saved brief both use it, so they read the same.
    /// </summary>
    public static string AssistantClause(string? assistant, string planner) =>
        assistant is null ? string.Empty
        : assistant == planner
            ? $" Your reviewer is {planner}: when a question needs an answer, send it one short message with SendMessage; it answers in a few lines. Ask rather than guess."
            : $" Your assistant is {assistant}: when a question needs an answer, send it one short message with SendMessage; it answers in a few lines. Ask rather than guess.";

    /// <summary>
    /// The assistant's brief (D-S41). It answers the coders' short questions in a few lines, and does no work of its own. It
    /// costs only when a coder asks it something.
    /// </summary>
    public static string AssistantBrief(string name, IReadOnlyList<string> coders, string planner)
    {
        var askers = coders.Count == 1 ? coders[0] : "the coders";
        return $"You are {name}, the assistant to {askers}. Your peer is {planner}, the planner. " +
               $"A coder sends you a short question with SendMessage: answer it in a few lines with SendMessage to that coder. " +
               "Do not write code, take batches, or start work of your own. Do not ask questions back unless you cannot answer without one.";
    }

    public const string AssistantResumePrompt =
        "You were restarted. Do not ask questions. If a coder has a question waiting, answer it with SendMessage; " +
        "otherwise reply with one line saying you are waiting.";

    /// <summary>
    /// The run that keeps a session resumed in full alive: one solo session with the same name, folder and model. If it goes, the
    /// supervisor resumes it with the solo restart prompt, not the pair's.
    /// </summary>
    public static RunRecord ResumedRun(string name, string folder, string model, string effort, DateTimeOffset now) =>
        new(name, "resume", folder, "auto", now, [new SessionSpec(SessionRole.Planner, name, folder, model, effort,
            "Continue the session you were resumed into, where it left off.", "auto", SoloResumePrompt)]);

    /// <summary>The alert of a resumed session whose id was not read back in time. The attention list shows it.</summary>
    public const string PendingIdAlert = "the session started, but its id was not read back in time; the next pass adopts it by its name";

    /// <summary>
    /// The state record of a resumed session. With its id, it is the session's record. Without it (the session started, but its id
    /// was not read back in time), the record has no id and its alert says so: the next pass adopts the session by its name, as it
    /// does for a session whose record was lost (<see cref="SupervisorPolicy"/>), so it is not started a second time.
    /// </summary>
    public static SessionRecord ResumedRecord(string name, string? sessionId, DateTimeOffset now) =>
        string.IsNullOrEmpty(sessionId)
            ? new SessionRecord(SessionRole.Planner, string.Empty, name, now, 0, [], null, PendingIdAlert)
            : new SessionRecord(SessionRole.Planner, sessionId, name, now, 0, [], null, null);

    /// <summary>Solo's brief: the task and the working rules. It does not name the session (the CLI has the name), and a solo session has no peer.</summary>
    public static string SoloBrief(string task) =>
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

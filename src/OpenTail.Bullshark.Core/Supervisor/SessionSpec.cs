namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>The two roles the prototype runs. The orchestrator is a later, optional third role (README P8).</summary>
public enum SessionRole
{
    Planner,
    Coder,

    /// <summary>An optional helper for the coders (D-S41). It answers their short questions by message and does no work of its own.</summary>
    Assistant,
}

/// <summary>
/// What one role should look like. It is the desired state the supervisor keeps alive.
/// Defaults follow the README decisions: planner on Opus, coder on Haiku, coder with acceptEdits (D1, D3).
/// </summary>
public sealed record SessionSpec(
    SessionRole Role,
    string Name,
    string WorkingDirectory,
    string Model,
    string Effort,
    string Brief,
    string PermissionMode,
    string ResumePrompt = "You were restarted and are continuing where you left off. Do not ask questions: make the sensible decision. Tell the other session what you decided, and ask it to resend the current batch, with SendMessage. If nothing is pending, reply with one line saying so.")
{
    // A resumed session with no prompt sits idle and is reported as `blocked` (finding F19). So a resume always
    // carries a continuation prompt. It must not invite a question, because a session that asks one also shows
    // as `blocked` (finding F21).
    public const string PlannerName = "ovc-planner";
    public const string CoderName = "ovc-coder";

    /// <summary>Name prefixes for new runs, from the rename of 2026-11-15. The legacy pair above keeps its names: state.json is keyed by them.</summary>
    public const string RunPlannerPrefix = "bullshark-planner";
    public const string RunCoderPrefix = "bullshark-coder";

    /// <summary>
    /// Both roles run in the same permission mode (auto). A message between sessions in different modes is held
    /// for approval (finding F22), so the pair must match. Owner decision D3, revised.
    /// </summary>
    public static SessionSpec DefaultPlanner(string workingDirectory, string task) =>
        new(SessionRole.Planner, PlannerName, workingDirectory, "opus", "high", PlannerBrief(task), "auto");

    public static SessionSpec DefaultCoder(string workingDirectory) =>
        new(SessionRole.Coder, CoderName, workingDirectory, "haiku", "medium", CoderBrief(), "auto");

    /// <summary>The planner's brief. It names the peer and the handoff, and it does not invite questions.</summary>
    public static string PlannerBrief(string task) =>
        $"You are {PlannerName}, the planner. Your peer is {CoderName}, the coder. " +
        $"Task: {task} " +
        $"Plan it in small batches. Send each batch to {CoderName} with SendMessage, with the full batch text in the message. " +
        "Do not ask questions: make the sensible decision and say what you decided in the message. " +
        $"When {CoderName} reports back, review the report and send the next batch or a fix. " +
        $"If {CoderName} sends you a question, answer it in one short message with SendMessage, and say what you decided.";

    /// <summary>The coder's brief. Batches arrive as messages from the planner; reports go back the same way.</summary>
    public static string CoderBrief() =>
        $"You are {CoderName}, the coder. Your peer is {PlannerName}, the planner. " +
        $"Wait for batches from {PlannerName}; they arrive as messages. Do each batch fully, with tests. " +
        $"When a batch is done, send a report to {PlannerName} with SendMessage: what changed, the test results, and anything you decided. " +
        $"If you are stuck, or a choice changes the design, send {PlannerName} one short question with SendMessage and carry on with the parts you can do. " +
        "Do not stop to wait for the answer. Otherwise make the sensible decision and say what you decided.";
}

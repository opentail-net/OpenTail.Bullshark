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
    /// <summary>Name prefixes for new runs, from the rename of 2026-11-15. The names of the first version are not in the code any more: state.json still holds them as keys, and they are read as data.</summary>
    public const string RunPlannerPrefix = "bullshark-planner";
    public const string RunCoderPrefix = "bullshark-coder";

}

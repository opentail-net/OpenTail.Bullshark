using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

public enum AlertKind
{
    /// <summary>
    /// The session is `blocked`: waiting for input. That is a permission answer, an answer to a question, or an idle
    /// session with no prompt (findings F19, F21). The supervisor never answers any of them on the owner's behalf.
    /// </summary>
    Blocked,

    /// <summary>The session keeps dying and being restarted. Restarts stop until the owner looks.</summary>
    RestartLoop,

    /// <summary>The account login expires soon (read from the session's terminal output).</summary>
    LoginExpiring,

    /// <summary>A start or a resume failed. The message says why. A failed start is not retried until the owner asks.</summary>
    StartFailed,

    /// <summary>
    /// The session is listed as `stopped`. It is not brought back: the owner starts the run again. Raised once per entry into
    /// the state (via <see cref="SessionRecord.LastState"/>).
    /// </summary>
    Stopped,

    /// <summary>
    /// The session is listed as `failed`. It is not brought back: the owner looks at its tab, then starts the run again. Raised once
    /// per entry into the state.
    /// </summary>
    Failed,
}

/// <summary>One thing the supervisor decided to do on this check.</summary>
public abstract record SupervisorAction(SessionRole Role)
{
    /// <summary>Start a session for the first time, with its brief. The session id is read back after the launch.</summary>
    public sealed record Start(SessionRole Role, SessionSpec Spec) : SupervisorAction(Role);

    /// <summary>A session of this name is already listed: its record is written from the listing, and nothing is started.</summary>
    public sealed record Adopt(SessionRole Role, SessionSpec Spec, string SessionId, string State) : SupervisorAction(Role);

    /// <summary>The session is gone from `agents --json`: resume it under the same session id.</summary>
    public sealed record Resume(SessionRole Role, SessionSpec Spec, string SessionId) : SupervisorAction(Role);

    /// <summary>Tell the owner something. The supervisor never answers a prompt on its own.</summary>
    public sealed record Alert(SessionRole Role, AlertKind Kind, string Detail) : SupervisorAction(Role);
}

/// <summary>Limits the supervisor applies, from the README decisions.</summary>
public sealed record SupervisorRules(int MaxRestartsPerHour = 3)
{
    public static SupervisorRules Default { get; } = new();
}

/// <summary>
/// The supervisor's decisions as a pure function: no processes, no files, no clock of its own.
/// Given the desired session, what is remembered, and what `claude agents --json` shows, it returns the actions.
/// </summary>
public static class SupervisorPolicy
{
    /// <summary>
    /// The states that mean a session has ended. The agent listing now carries completed sessions, so these are listed too.
    /// `done` is also the state of an idle session that is still running, and the listing does not tell the two apart, so a
    /// `done` session is never adopted as the role's session (a finished one would then never be replaced).
    /// </summary>
    private static bool Is(string? state, string expected) => string.Equals(state, expected, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<SupervisorAction> Decide(
        SessionSpec spec,
        SessionRecord? record,
        IReadOnlyList<AgentInfo> agents,
        DateTimeOffset now,
        SupervisorRules rules)
    {
        // Adoption first: a session of this name is already running, because the record was lost or the start was not read back.
        // Starting it again would be a second paid session. An idle `done` session is adopted too: the listing cannot tell idle
        // from completed, and new names avoid every listed name, completed ones included, so it is this run's own. A finished
        // session is then left alone, as `done` always is. Stopped and failed sessions are not adopted: the role starts again.
        if (record is null || string.IsNullOrEmpty(record.SessionId))
        {
            var listed = agents.FirstOrDefault(a =>
                string.Equals(a.Name, spec.Name, StringComparison.Ordinal) && !Is(a.EffectiveState, "stopped") && !Is(a.EffectiveState, "failed"));
            if (listed?.SessionId is { Length: > 0 } adoptId)
                return [new SupervisorAction.Adopt(spec.Role, spec, adoptId, listed.EffectiveState)];
        }

        // First start: nothing is remembered for this role.
        if (record is null)
            return [new SupervisorAction.Start(spec.Role, spec)];

        // A start that failed is kept with no session id. It is not retried on every pass; the owner asks for a retry.
        if (string.IsNullOrEmpty(record.SessionId))
            return [];

        var agent = agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));

        // Gone: the daemon lost it or it was removed. Resume under the same id, unless it keeps happening.
        if (agent is null)
        {
            var recentRestarts = record.RestartTimes.Count(t => now - t < TimeSpan.FromHours(1));
            if (recentRestarts >= rules.MaxRestartsPerHour)
            {
                // Alerted once: while the restart-loop alert stands, nothing more is done on each pass. When the restarts age out
                // below the limit, the resume above runs instead, and the runner clears the alert with it.
                if (record.LastAlert?.StartsWith(AttentionList.RestartLoopPrefix, StringComparison.Ordinal) == true)
                    return [];
                return [new SupervisorAction.Alert(spec.Role, AlertKind.RestartLoop,
                    $"{recentRestarts} restarts in the last hour; not restarting until someone looks.")];
            }
            return [new SupervisorAction.Resume(spec.Role, spec, record.SessionId)];
        }

        var state = agent.EffectiveState;

        // Waiting on a permission prompt does nothing until someone answers. Alert once per entry into the state.
        if (string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(record.LastState, "blocked", StringComparison.OrdinalIgnoreCase)
                ? []
                : [new SupervisorAction.Alert(spec.Role, AlertKind.Blocked,
                    "Waiting for input: a permission answer, an answer to a question, or an idle session. Open its tab to see which.")];
        }

        // Stopped or failed: the session is listed, so it is not "gone", and it is never brought back by the supervisor. The owner
        // is told once per entry into the state, as for a blocked session.
        if (Is(state, "stopped"))
            return Is(record.LastState, "stopped")
                ? []
                : [new SupervisorAction.Alert(spec.Role, AlertKind.Stopped,
                    "the session was stopped, so it is not restarted. Start the run again to continue.")];

        if (Is(state, "failed"))
            return Is(record.LastState, "failed")
                ? []
                : [new SupervisorAction.Alert(spec.Role, AlertKind.Failed,
                    "the session failed, so it is not restarted. Look at its tab, then start the run again.")];

        // working, done (idle) and busy (interactive) are all fine. Unknown states are left alone.
        return [];
    }
}

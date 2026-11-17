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

    /// <summary>The run is near its limit (80 %). Raised once per entry into the level (additions item 4).</summary>
    LimitNear,

    /// <summary>
    /// The run reached its limit. Nothing new is started or resumed for it; a running session is not stopped. Raised once (additions item 4).
    /// </summary>
    LimitReached,
}

/// <summary>One thing the supervisor decided to do on this check.</summary>
public abstract record SupervisorAction(SessionRole Role)
{
    /// <summary>Start a session for the first time, with its brief. The session id is read back after the launch.</summary>
    public sealed record Start(SessionRole Role, SessionSpec Spec) : SupervisorAction(Role);

    /// <summary>
    /// A session of this name is already listed: its record is written from the listing, and nothing is started. <paramref name="StartedAt"/> is the
    /// session's own start from the listing, so the run's clock counts from when it really started, not from this adoption (round 8 item 4). Null
    /// when the listing gives none: the record then starts now.
    /// </summary>
    public sealed record Adopt(SessionRole Role, SessionSpec Spec, string SessionId, string State, DateTimeOffset? StartedAt = null)
        : SupervisorAction(Role);

    /// <summary>The session is gone from `agents --json`: resume it under the same session id.</summary>
    public sealed record Resume(SessionRole Role, SessionSpec Spec, string SessionId) : SupervisorAction(Role);

    /// <summary>Tell the owner something. The supervisor never answers a prompt on its own.</summary>
    public sealed record Alert(SessionRole Role, AlertKind Kind, string Detail) : SupervisorAction(Role);

    /// <summary>
    /// The run's limit level went down, because the owner raised the limit. The record follows (<see cref="SessionRecord.LimitLevel"/>),
    /// and nobody is told: it is not counted as an action of the pass.
    /// </summary>
    public sealed record LimitLevelSet(SessionRole Role, string? Level) : SupervisorAction(Role);
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
    /// <summary>A case-insensitive state comparison. The listing carries completed sessions too (`--all`, F61).</summary>
    private static bool Is(string? state, string expected) => string.Equals(state, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The actions for one session, with its run's limit state when the run has one (additions item 4). A run at its limit gets no new
    /// starts or resumes. The limit notices are added here, once per level: the level the owner was last told is kept in
    /// <see cref="SessionRecord.LimitLevel"/>, so a later alert does not make the notice repeat (round 5 item 8). Only the session the
    /// budget says to notify gets a notice, and a level that went down (the limit was raised) is recorded without one.
    /// </summary>
    public static IReadOnlyList<SupervisorAction> Decide(
        SessionSpec spec,
        SessionRecord? record,
        IReadOnlyList<AgentInfo> agents,
        DateTimeOffset now,
        SupervisorRules rules,
        RunBudget? budget = null)
    {
        var reached = budget is { Reached: true };
        var actions = DecideSession(spec, record, agents, now, rules, reached).ToList();
        if (record is null) return actions;

        // The run has no limit now (the owner removed it, or its run is gone from runs.json): the level the owner was told is cleared, so a
        // limit set later is announced again (round 6 item 5).
        if (budget is null)
        {
            if (record.LimitLevel is not null) actions.Add(new SupervisorAction.LimitLevelSet(spec.Role, null));
            return actions;
        }

        var wanted = RunBudget.RankOf(budget.Level);
        // The told level is the highest one of the run's records, so a session that leads now is not told a level the run was told already.
        var told = RunBudget.RankOf(budget.ToldLevel ?? record.LimitLevel);
        if (budget.Notify && !string.IsNullOrEmpty(record.SessionId) && wanted > told)
        {
            actions.Add(reached
                ? new SupervisorAction.Alert(spec.Role, AlertKind.LimitReached,
                    "the run reached its limit. Nothing new is started or resumed; a running session is not stopped. Stop it, or raise the limit.")
                : new SupervisorAction.Alert(spec.Role, AlertKind.LimitNear,
                    $"the run has used {budget.Fraction:P0} of its limit."));
        }

        // A level above the one the run is at now (the owner raised the limit) goes down quietly, on any session that holds it (round 5 item 8).
        if (RunBudget.RankOf(record.LimitLevel) > wanted)
            actions.Add(new SupervisorAction.LimitLevelSet(spec.Role, budget.Level));
        return actions;
    }

    /// <summary>True for the listed states `stopped` and `failed`, in any letter case: the states the supervisor never brings back.</summary>
    public static bool IsStoppedOrFailed(string? state) => Is(state, "stopped") || Is(state, "failed");

    /// <summary>
    /// True when the owner stopped the session or it failed. The last listed state says so: the state is kept while the listing does not
    /// show the session (round 5 item 1). The last alert is read only for a record with no listed state (one from before the state was
    /// kept), since a stale stop alert on a session that was seen running again would otherwise hold it back (round 6 item 2).
    /// </summary>
    private static bool WasStoppedOrFailed(SessionRecord record)
    {
        if (record.LastState is not null) return IsStoppedOrFailed(record.LastState);
        return record.LastAlert?.StartsWith(AttentionList.StoppedAlertPrefix, StringComparison.Ordinal) == true
            || record.LastAlert?.StartsWith(AttentionList.FailedAlertPrefix, StringComparison.Ordinal) == true;
    }

    private static IReadOnlyList<SupervisorAction> DecideSession(
        SessionSpec spec,
        SessionRecord? record,
        IReadOnlyList<AgentInfo> agents,
        DateTimeOffset now,
        SupervisorRules rules,
        bool reached)
    {
        // Adoption first: a session of this name is already running, because the record was lost or the start was not read back.
        // Starting it again would be a second paid session. An idle `done` session is adopted too: the listing cannot tell idle
        // from completed, and new names avoid every listed name, completed ones included, so it is this run's own. A finished
        // session is then left alone, as `done` always is. Stopped and failed sessions are not adopted: the role starts again.
        // A stale row is not adopted either: the daemon is not running, so the row is not alive (round 8 item 1).
        if (record is null || string.IsNullOrEmpty(record.SessionId))
        {
            var listed = agents.FirstOrDefault(a =>
                string.Equals(a.Name, spec.Name, StringComparison.Ordinal) && !Is(a.EffectiveState, "stopped") && !Is(a.EffectiveState, "failed")
                && !Is(a.EffectiveState, DaemonStatus.StaleState));
            if (listed?.SessionId is { Length: > 0 } adoptId)
                return [new SupervisorAction.Adopt(spec.Role, spec, adoptId, listed.EffectiveState, listed.StartedAt)];
        }

        // First start: nothing is remembered for this role.
        if (record is null)
            return reached ? [] : [new SupervisorAction.Start(spec.Role, spec)];

        // A start that failed is kept with no session id. It is not retried on every pass; the owner asks for a retry.
        if (string.IsNullOrEmpty(record.SessionId))
            return [];

        var agent = agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));

        // Gone: the daemon lost it or it was removed. Resume under the same id, unless it keeps happening.
        if (agent is null) return DecideLost(spec, record, now, rules, reached);

        var state = agent.EffectiveState;

        // Stale (round 8 item 1): the daemon is not running, so a listed working or blocked session is not alive. One that was waiting for
        // input gets one alert and is never resumed here, since its prompt is lost. One that was working is lost, and is resumed under the
        // rules for a lost session. Stale rows come only from DaemonStatus.IdleListing, which keeps the listed state in ListedState.
        if (string.Equals(state, DaemonStatus.StaleState, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(agent.ListedState, "blocked", StringComparison.OrdinalIgnoreCase))
                return AttentionList.IsStaleBlockedAlert(record.LastAlert)
                    ? []
                    : [new SupervisorAction.Alert(spec.Role, AlertKind.Blocked, AttentionList.StaleBlockedDetail)];
            return DecideLost(spec, record, now, rules, reached);
        }

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

    /// <summary>
    /// A session that is lost: not in the listing, or stale and working (round 8 item 1). It is resumed under the same id, under the rules for a
    /// lost session: a session the owner stopped or that failed is not brought back, a session that was waiting for input when the daemon stopped
    /// is not (its prompt is lost; the owner opens its tab), nothing is resumed at the run's limit, and a restart loop stops until someone looks.
    /// </summary>
    private static IReadOnlyList<SupervisorAction> DecideLost(
        SessionSpec spec,
        SessionRecord record,
        DateTimeOffset now,
        SupervisorRules rules,
        bool reached)
    {
        // A session the owner stopped, or that failed, is not brought back even when the listing no longer shows it (`claude rm`,
        // or a listing that dropped it). Its last listed state, or its last alert, says so (round 5 item 1).
        if (WasStoppedOrFailed(record)) return [];

        // The stale-blocked hold lasts an hour from its alert, and no longer (round 9 item 4): a session that is still not listed then goes
        // through the rules below, as any lost session does, so its role is not held forever.
        if (StaleBlockedHeld(record, now)) return [];

        // At the run's limit a lost session is not brought back (additions item 4).
        if (reached) return [];
        var recentRestarts = record.RestartTimes.Count(t => now - t < TimeSpan.FromHours(1));
        if (recentRestarts >= rules.MaxRestartsPerHour)
        {
            // Alerted once: while the restart-loop alert stands, nothing more is done on each pass. When the restarts age out
            // below the limit, the resume runs instead, and the runner clears the alert with it.
            if (record.LastAlert?.StartsWith(AttentionList.RestartLoopPrefix, StringComparison.Ordinal) == true)
                return [];
            return [new SupervisorAction.Alert(spec.Role, AlertKind.RestartLoop,
                $"{recentRestarts} restarts in the last hour; not restarting until someone looks.")];
        }
        return [new SupervisorAction.Resume(spec.Role, spec, record.SessionId)];
    }

    /// <summary>How long a lost session that was waiting for input is held back from a resume, counted from its stale-blocked alert (round 9 item 4).</summary>
    public static readonly TimeSpan StaleBlockedHold = TimeSpan.FromHours(1);

    /// <summary>
    /// True while the stale-blocked alert stands and was raised less than <see cref="StaleBlockedHold"/> ago. A record with no alert time (one
    /// from before the field) is not held: it has no hour to hold to. A time ahead of now (the clock went back) is not held either, so a
    /// clock jump cannot hold a role for longer than the hour.
    /// </summary>
    private static bool StaleBlockedHeld(SessionRecord record, DateTimeOffset now)
    {
        if (!AttentionList.IsStaleBlockedAlert(record.LastAlert) || record.StaleBlockedAt is not { } raised) return false;
        var age = now - raised;
        return age >= TimeSpan.Zero && age < StaleBlockedHold;
    }
}

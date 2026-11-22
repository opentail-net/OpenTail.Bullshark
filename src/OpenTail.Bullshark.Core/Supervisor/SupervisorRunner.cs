using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One supervision pass: read `claude agents --json`, ask the policy what to do for each session, carry it out
/// through the public CLI, and save what was remembered. Runs headless. The window is only a view.
/// Records are keyed by session name, so a run can hold several coders (D-S13). Records that are not in this pass's
/// specs (sessions of other runs) are kept as they are.
/// A failed start or resume is recorded and reported, and it does not stop the other sessions of the pass. A failed
/// start leaves a record with no session id; the policy does not retry it until the owner asks (see <see cref="StateStore"/>).
/// The pass saves only what it changed for its own sessions, under the file lock, so a change the owner made meanwhile is kept.
/// </summary>
public sealed class SupervisorRunner
{
    private readonly ClaudeCli _cli;
    private readonly StateStore _store;
    private readonly TimeProvider _clock;
    private readonly SupervisorRules _rules;
    private readonly TimeSpan _resolveTimeout;
    private readonly Action<string> _log;

    /// <summary>
    /// How far each session's run is through its limits, by session name (additions item 4). The window sets it after a pass, so a
    /// limit applies from the next pass. Empty, the default, means no run has a limit, which is how the runner worked before.
    /// </summary>
    public IReadOnlyDictionary<string, RunBudget> Budgets { get; set; } = new Dictionary<string, RunBudget>(StringComparer.Ordinal);

    /// <summary>
    /// Until when each session's run is held by a usage-limit stop, by session name (review item 1, 2026-11-21). Set by the window after a pass.
    /// Empty, the default, holds nothing.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LimitHolds { get; set; } = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

    /// <summary>
    /// Where the timeline's events go (additions item 5). Null, the default, records nothing. A write that fails is logged, and the
    /// pass goes on: the log must never stop the supervision.
    /// </summary>
    public IEventSink? Events { get; set; }

    /// <summary>
    /// The background daemon as the window read it before the pass (round 7 item 1, revised by the planner). A daemon that is not running is
    /// the normal idle state, so it never stops a pass: starts and resumes go ahead, and `claude --bg` brings the daemon up. While it is not
    /// running, a listed session that claims working or blocked is decided on as stale (<see cref="DaemonStatus.IdleListing"/>). A stale working
    /// session is lost and is resumed under the rules for a lost one; a stale blocked one gets one alert and is not resumed (round 8 item 1).
    /// Null, the default, changes nothing.
    /// </summary>
    public DaemonStatus? Daemon { get; set; }

    /// <summary>
    /// The hold while `claude respawn --all` runs and for <see cref="RespawnHold.Window"/> after it (round 8 item 3). While it is held, a pass makes
    /// no start or resume: the action is dropped, and the record is not changed by it. It still reads the listing and shows what it finds.
    /// Null, the default, holds nothing.
    /// </summary>
    public RespawnHold? Hold { get; set; }

    /// <summary>
    /// Asked before each start and each resume (R4): the reason the launch is refused, or null when it may go on. A refused action is dropped the way a
    /// held one is: no record is changed for it, and the next pass asks again. Null, the default, asks nothing.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? LaunchRefusal { get; set; }

    public SupervisorRunner(
        ClaudeCli cli,
        StateStore store,
        TimeProvider clock,
        SupervisorRules? rules = null,
        TimeSpan? resolveTimeout = null,
        Action<string>? log = null)
    {
        _cli = cli;
        _store = store;
        _clock = clock;
        _rules = rules ?? SupervisorRules.Default;
        _resolveTimeout = resolveTimeout ?? TimeSpan.FromSeconds(30);
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// Runs one pass for every desired session and returns what was done, including the failures. It lists the agents with the
    /// completed ones, so a session that finished or was stopped is seen as such and is not taken for a lost one.
    /// </summary>
    public async Task<IReadOnlyList<SupervisorAction>> CheckOnceAsync(
        IReadOnlyList<SessionSpec> specs,
        CancellationToken cancellationToken = default)
    {
        var agents = await _cli.ListAgentsAsync(includeCompleted: true, cancellationToken: cancellationToken);
        return await CheckOnceAsync(specs, agents, cancellationToken);
    }

    /// <summary>
    /// The same pass with a listing that the caller already has. Each listing is a new `claude` process, so a caller that
    /// lists for its own purposes (the window does) passes that listing here instead of paying for a second one.
    /// A session started during the pass still reads its id back with a listing of its own.
    /// </summary>
    public async Task<IReadOnlyList<SupervisorAction>> CheckOnceAsync(
        IReadOnlyList<SessionSpec> specs,
        IReadOnlyList<AgentInfo> agents,
        CancellationToken cancellationToken = default)
    {
        // While the daemon is not running, a listed session that claims working or blocked is stale: it is decided on, and recorded, as stale.
        agents = Daemon?.IdleListing(agents) ?? agents;

        // Read first: an unreadable state file stops the pass before anything is started or written.
        var before = _store.Load().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var records = new Dictionary<string, SessionRecord>(before, StringComparer.Ordinal);
        var performed = new List<SupervisorAction>();
        var processed = new List<string>();
        var now = _clock.GetUtcNow();

        // Saved in a finally: a session started earlier in this pass keeps its record even if a later step throws.
        try
        {
            foreach (var spec in specs)
            {
                processed.Add(spec.Name);
                records.TryGetValue(spec.Name, out var record);
                // The usage-limit hold of this session's run, if a stop was in force at the last read (review item 1, 2026-11-21).
                DateTimeOffset? limitHoldUntil = LimitHolds.TryGetValue(spec.Name, out var hold) ? hold : null;
                var actions = SupervisorPolicy.Decide(spec, record, agents, now, _rules,
                    Budgets.TryGetValue(spec.Name, out var budget) ? budget : null, limitHoldUntil);

                foreach (var action in actions)
                {
                    // A respawn is restarting the workers: no start or resume is carried out until the hold ends. The session is decided on again
                    // by the next pass that is not held (round 8 item 3). The hold is read for each action, just before it is carried out, so a
                    // respawn that began during this pass holds the rest of it too (round 9 item 2).
                    if (Hold?.IsHeld == true && HeldByRespawn(action)) continue;
                    // A start or a resume is refused while the installed claude may not launch (R4). It is dropped as a held one is.
                    if (LaunchRefusal is not null && action is (SupervisorAction.Start or SupervisorAction.Resume)
                        && await LaunchRefusal(cancellationToken) is not null) continue;

                    try
                    {
                        await Carry(action, spec, records, now, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && action is SupervisorAction.Start or SupervisorAction.Resume)
                    {
                        // Any failure to start or resume is a failed start for this session only: the pass goes on (F48).
                        var failure = Failed(action, spec, records, now, ex.Message);
                        performed.Add(failure);
                        Emit(spec, failure, now);
                        continue;
                    }

                    // A limit level that went down is recorded (Carry) and is not an action the owner hears about (round 5 item 8).
                    if (action is SupervisorAction.LimitLevelSet) continue;

                    // Outside the try above: a session that started is never recorded as failed because the event log could not be
                    // written (round 5 item 2). Write itself logs the failure and the pass goes on.
                    performed.Add(action);
                    Emit(spec, action, now);
                }

                // Remember the state we saw, so the next pass can tell a change of state from a repeat. A session the listing no
                // longer shows keeps `stopped` or `failed`: the owner's stop must outlive the listing (round 5 item 1). Any other
                // state is dropped when the session is not listed, as before. A stale row is recorded as the state it was listed
                // with, not as "stale": a blocked, stale, blocked sequence is not a new entry into blocked (round 8 item 2).
                if (records.TryGetValue(spec.Name, out var current))
                {
                    var seen = agents.FirstOrDefault(a => string.Equals(a.SessionId, current.SessionId, StringComparison.OrdinalIgnoreCase));
                    var observed = seen?.ObservedState;
                    var kept = observed is null && SupervisorPolicy.IsStoppedOrFailed(current.LastState) ? current.LastState : observed;
                    if (!string.Equals(current.LastState, kept, StringComparison.Ordinal))
                        EmitState(spec, current.LastState, observed ?? "not listed", now);

                    // A session the owner brought back (listed in a state other than stopped or failed) no longer owes its stop or failure
                    // alert. The alert is cleared here, so a later loss is not read as a stop (round 6 item 2).
                    var revived = observed is not null && !SupervisorPolicy.IsStoppedOrFailed(observed);
                    // A name-taken alert is about a lost session: once the session is listed again (lifecycle batch L1), it no longer stands.
                    // The stale-blocked alert stands while the session is stale or not listed. A listing that shows it live again clears it, so the
                    // next time it goes stale it is alerted again (round 8 item 1). Only a daemon known to be running clears it (round 9 item 5): on an
                    // unknown status the listing is used as it is, so a row that claims blocked clears the alert and the next not-running read
                    // alerts the same session again.
                    var listedLive = seen is not null && !string.Equals(seen.EffectiveState, DaemonStatus.StaleState, StringComparison.OrdinalIgnoreCase);
                    var daemonRunning = Daemon?.State == DaemonState.Running;
                    var clear = (revived && (IsStopOrFailureAlert(current.LastAlert) || IsNameTakenAlert(current.LastAlert)))
                        || (listedLive && daemonRunning && AttentionList.IsStaleBlockedAlert(current.LastAlert));
                    records[spec.Name] = current with { LastState = kept, LastAlert = clear ? null : current.LastAlert };
                }
            }
        }
        finally
        {
            Merge(before, records, processed);
        }

        return performed;
    }

    /// <summary>
    /// What a respawn holds back: a start, a resume, and the stale-blocked alert. A listing taken while the daemon restarts can show a session
    /// as not running for a moment, so that alert waits for the hold to end too (round 9 item 4). Its record is then left as it was.
    /// </summary>
    private static bool HeldByRespawn(SupervisorAction action) =>
        action is (SupervisorAction.Start or SupervisorAction.Resume)
        || (action is SupervisorAction.Alert { Kind: AlertKind.Blocked } alert && alert.Detail == AttentionList.StaleBlockedDetail);

    /// <summary>True for a last alert that says the session was stopped or failed, the two alerts the owner must act on by starting the run again.</summary>
    private static bool IsStopOrFailureAlert(string? alert) =>
        alert?.StartsWith(AttentionList.StoppedAlertPrefix, StringComparison.Ordinal) == true
        || alert?.StartsWith(AttentionList.FailedAlertPrefix, StringComparison.Ordinal) == true;

    /// <summary>True for a last alert that says another live session holds the name of this lost session (lifecycle batch L1).</summary>
    private static bool IsNameTakenAlert(string? alert) => alert?.StartsWith(AttentionList.NameTakenPrefix, StringComparison.Ordinal) == true;

    /// <summary>Writes one performed action to the event sink, if there is one. The event names the session's run (round 5 item 10).</summary>
    private void Emit(SessionSpec spec, SupervisorAction action, DateTimeOffset now)
    {
        var (kind, detail) = action switch
        {
            SupervisorAction.Start => ("start", string.Empty),
            SupervisorAction.Adopt adopt => ("adopt", adopt.SessionId),
            SupervisorAction.Resume resume => ("resume", resume.SessionId),
            SupervisorAction.Alert alert => ($"alert:{alert.Kind}", alert.Detail),
            _ => ("other", string.Empty),
        };
        Write(new SupervisorEvent(now, spec.Name, kind, detail, spec.RunId));
    }

    private void EmitState(SessionSpec spec, string? from, string to, DateTimeOffset now) =>
        Write(new SupervisorEvent(now, spec.Name, "state", $"{from ?? "-"} -> {to}", spec.RunId));

    /// <summary>
    /// Writes one event. Every failure the stores report is logged here, and the pass goes on: the lock of the event log can time out
    /// (a <see cref="TimeoutException"/>), which the filter used to let out of the pass (round 5 item 2).
    /// </summary>
    private void Write(SupervisorEvent evt)
    {
        if (Events is null) return;
        try
        {
            Events.Write(evt);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            _log($"events: not written: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes only what this pass changed for its own sessions, under the file lock. A session the owner forgot during the pass
    /// stays forgotten: it was in the file when the pass began, and it is gone now.
    /// </summary>
    private void Merge(
        IReadOnlyDictionary<string, SessionRecord> before,
        IReadOnlyDictionary<string, SessionRecord> after,
        IReadOnlyList<string> names)
    {
        var changed = names.Distinct(StringComparer.Ordinal)
            .Where(name => after.TryGetValue(name, out var now)
                           && (!before.TryGetValue(name, out var was) || !now.Equals(was)))
            .ToList();
        if (changed.Count == 0) return;

        _store.Update(current =>
        {
            foreach (var name in changed)
            {
                if (before.ContainsKey(name) && !current.ContainsKey(name)) continue;
                current[name] = after[name];
            }
        });
    }

    private async Task Carry(
        SupervisorAction action,
        SessionSpec spec,
        Dictionary<string, SessionRecord> records,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case SupervisorAction.Start:
                var shortId = await _cli.StartBackgroundAsync(new BackgroundLaunch(
                    Name: spec.Name,
                    WorkingDirectory: spec.WorkingDirectory,
                    Model: spec.Model,
                    Effort: spec.Effort,
                    Prompt: spec.Brief,
                    PermissionMode: spec.PermissionMode), cancellationToken);
                // --bg assigns the session id itself, so read the real one back.
                var sessionId = await _cli.ResolveSessionIdAsync(shortId, _resolveTimeout, cancellationToken);
                records[spec.Name] = new SessionRecord(spec.Role, sessionId, spec.Name, now, 0, [], null, null);
                _log($"{spec.Name}: started {shortId} (session {sessionId})");
                break;

            case SupervisorAction.Adopt adopt:
                // The run's clock counts from the session's own start when the listing has it (round 8 item 4). A record that is kept
                // keeps its StartedAt: Resume copies the record, and a start or an adoption only replaces a record with no session id.
                records[spec.Name] = new SessionRecord(spec.Role, adopt.SessionId, spec.Name, adopt.StartedAt ?? now, 0, [], adopt.State, null);
                _log($"{spec.Name}: adopted the listed session {adopt.SessionId}");
                break;

            case SupervisorAction.Resume resume:
                await _cli.StartBackgroundAsync(new BackgroundLaunch(
                    Name: spec.Name,
                    WorkingDirectory: spec.WorkingDirectory,
                    Model: spec.Model,
                    Effort: spec.Effort,
                    Prompt: spec.ResumePrompt,
                    PermissionMode: spec.PermissionMode,
                    ResumeSessionId: resume.SessionId), cancellationToken);
                var previous = records[spec.Name];
                records[spec.Name] = previous with
                {
                    Restarts = previous.Restarts + 1,
                    RestartTimes = previous.RestartTimes.Append(now).ToList(),
                    // The session is back: a restart-loop or failed-resume alert from before is no longer current. Kept, it would
                    // put the session under "Needs you" the next time it went missing (AttentionList reads it).
                    LastAlert = null,
                };
                _log($"{spec.Name}: resumed {resume.SessionId}");
                break;

            case SupervisorAction.Alert alert:
                // A limit notice records its level, which is what keeps it from being repeated (round 5 item 8). It does not replace the last
                // alert: a restart-loop or a stopped alert must stay visible when the run reaches its limit too (round 6 item 4).
                records[spec.Name] = alert.Kind switch
                {
                    AlertKind.LimitNear => records[spec.Name] with { LimitLevel = RunBudget.NearLevel },
                    AlertKind.LimitReached => records[spec.Name] with { LimitLevel = RunBudget.ReachedLevel },
                    // The stale-blocked alert keeps the time it was raised: the hold on a lost session lasts an hour from it (round 9 item 4).
                    _ => records[spec.Name] with
                    {
                        LastAlert = $"{alert.Kind}: {alert.Detail}",
                        StaleBlockedAt = alert.Detail == AttentionList.StaleBlockedDetail ? now : records[spec.Name].StaleBlockedAt,
                    },
                };
                _log($"{spec.Name}: ALERT {alert.Kind} - {alert.Detail}");
                break;

            case SupervisorAction.LimitLevelSet levelSet:
                records[spec.Name] = records[spec.Name] with { LimitLevel = levelSet.Level };
                break;
        }
    }

    /// <summary>
    /// Records a failed start or resume and returns the alert for it. A failed start keeps a record with no session id,
    /// so the next pass does not start it again. A failed resume counts as a restart, so repeated failures reach the
    /// restart-loop alert instead of retrying on every pass.
    /// </summary>
    private SupervisorAction.Alert Failed(
        SupervisorAction action,
        SessionSpec spec,
        Dictionary<string, SessionRecord> records,
        DateTimeOffset now,
        string message)
    {
        var detail = (action is SupervisorAction.Start ? "start failed: " : AttentionList.ResumeFailedPrefix) + message;
        _log($"{spec.Name}: ALERT {AlertKind.StartFailed} - {detail}");

        if (action is SupervisorAction.Start)
        {
            records[spec.Name] = new SessionRecord(spec.Role, string.Empty, spec.Name, now, 0, [], null, detail);
        }
        else if (records.TryGetValue(spec.Name, out var previous))
        {
            records[spec.Name] = previous with
            {
                Restarts = previous.Restarts + 1,
                RestartTimes = previous.RestartTimes.Append(now).ToList(),
                LastAlert = detail,
            };
        }
        return new SupervisorAction.Alert(spec.Role, AlertKind.StartFailed, detail);
    }
}

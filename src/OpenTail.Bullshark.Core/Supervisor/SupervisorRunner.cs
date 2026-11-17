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

    /// <summary>Runs one pass for every desired session and returns what was done, including the failures.</summary>
    public async Task<IReadOnlyList<SupervisorAction>> CheckOnceAsync(
        IReadOnlyList<SessionSpec> specs,
        CancellationToken cancellationToken = default)
    {
        var agents = await _cli.ListAgentsAsync(cancellationToken);
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
                var actions = SupervisorPolicy.Decide(spec, record, agents, now, _rules);

                foreach (var action in actions)
                {
                    try
                    {
                        await Carry(action, spec, records, now, cancellationToken);
                        performed.Add(action);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && action is SupervisorAction.Start or SupervisorAction.Resume)
                    {
                        // Any failure to start or resume is a failed start for this session only: the pass goes on (F48).
                        performed.Add(Failed(action, spec, records, now, ex.Message));
                    }
                }

                // Remember the state we saw, so the next pass can tell a change of state from a repeat.
                if (records.TryGetValue(spec.Name, out var current))
                {
                    var seen = agents.FirstOrDefault(a => string.Equals(a.SessionId, current.SessionId, StringComparison.OrdinalIgnoreCase));
                    records[spec.Name] = current with { LastState = seen?.EffectiveState };
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
                records[spec.Name] = new SessionRecord(spec.Role, adopt.SessionId, spec.Name, now, 0, [], adopt.State, null);
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
                records[spec.Name] = records[spec.Name] with { LastAlert = $"{alert.Kind}: {alert.Detail}" };
                _log($"{spec.Name}: ALERT {alert.Kind} - {alert.Detail}");
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

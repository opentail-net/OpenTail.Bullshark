using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One supervision pass: read `claude agents --json`, ask the policy what to do for each session, carry it out
/// through the public CLI, and save what was remembered. Runs headless. The window is only a view.
/// Records are keyed by session name, so a run can hold several coders (D-S13). Records that are not in this pass's
/// specs (sessions of other runs) are kept as they are.
/// A failed start or resume is recorded and reported, and it does not stop the other sessions of the pass. A failed
/// start leaves a record with no session id; the policy does not retry it until the owner asks (see <see cref="StateStore"/>).
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
        var records = _store.Load().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var performed = new List<SupervisorAction>();
        var now = _clock.GetUtcNow();

        // Saved in a finally: a session started earlier in this pass keeps its record even if a later step throws.
        try
        {
            foreach (var spec in specs)
            {
                records.TryGetValue(spec.Name, out var record);
                var actions = SupervisorPolicy.Decide(spec, record, agents, now, _rules);

                foreach (var action in actions)
                {
                    try
                    {
                        await Carry(action, spec, records, now, cancellationToken);
                        performed.Add(action);
                    }
                    catch (ClaudeCliException ex) when (action is SupervisorAction.Start or SupervisorAction.Resume)
                    {
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
            _store.Save(records);
        }

        return performed;
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
        var detail = $"{(action is SupervisorAction.Start ? "start" : "resume")} failed: {message}";
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

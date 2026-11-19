using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One pass of the headless supervisor: restart what is gone, alert on what is blocked, and warn before the
/// account login expires (finding F8). It runs without any window; the window is only a view on the same state.
/// </summary>
public sealed class SupervisorLoop
{
    /// <summary>The shortest time between two login alerts (round 12 item A5).</summary>
    public static readonly TimeSpan LoginAlertInterval = TimeSpan.FromHours(1);

    private readonly SupervisorRunner _runner;
    private readonly ClaudeCli _cli;
    private readonly StateStore _store;
    private readonly Action<string> _log;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _lastLoginAlert;

    /// <param name="clock">The time the login alert is limited by. The default is the system clock.</param>
    public SupervisorLoop(SupervisorRunner runner, ClaudeCli cli, StateStore store, Action<string> log, TimeProvider? clock = null)
    {
        _runner = runner;
        _cli = cli;
        _store = store;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs one pass and returns the number of actions taken (starts, resumes and alerts). It lists the agents itself, with the
    /// completed ones (see <see cref="SupervisorRunner.CheckOnceAsync(IReadOnlyList{SessionSpec}, CancellationToken)"/>).
    /// </summary>
    public async Task<int> RunPassAsync(IReadOnlyList<SessionSpec> specs, CancellationToken cancellationToken = default)
    {
        var agents = await _cli.ListAgentsAsync(includeCompleted: true, cancellationToken: cancellationToken);
        return await RunPassAsync(specs, agents, cancellationToken);
    }

    /// <summary>
    /// Runs one pass with a listing the caller already has, so the pass makes no listing call of its own. The window lists the
    /// agents once for its refresh and passes that listing here.
    /// </summary>
    public async Task<int> RunPassAsync(
        IReadOnlyList<SessionSpec> specs,
        IReadOnlyList<AgentInfo> agents,
        CancellationToken cancellationToken = default)
    {
        var actions = await _runner.CheckOnceAsync(specs, agents, cancellationToken);

        // What this pass read of the login (night batch N2): whether any live session could be read, and the warning it showed, if any.
        var read = false;
        TimeSpan? warning = null;
        foreach (var record in _store.Load().Values)
        {
            // A failed start has no session to read yet.
            if (string.IsNullOrEmpty(record.SessionId)) continue;

            // Only a live session is read (round 12 item A5). The login belongs to the account, so one live session tells us, and a
            // finished, stopped or failed one is not launched for on every pass.
            if (!IsLive(agents, record.SessionId)) continue;

            string output;
            try
            {
                output = await _cli.LogsAsync(ClaudeCli.ShortIdOf(record.SessionId), cancellationToken);
            }
            catch (ClaudeCliException ex)
            {
                // A session that has just gone cannot be read this pass. The runner handles the restart.
                _log($"{record.Name}: output not readable this pass ({ex.Message})");
                continue;
            }

            read = true;
            if (LoginExpiry.Find(output) is { } remaining)
            {
                warning = remaining;
                if (LoginExpiry.IsExpiringSoon(remaining))
                {
                    AlertLogin(record.Name, remaining);
                    // The first session that shows the expiry says it for the account, so the others are not read this pass.
                    break;
                }
            }
        }
        LastLogin = new LoginReading(read, warning);

        return actions.Count;
    }

    /// <summary>
    /// The login as the last pass that ran read it (night batch N2). Not readable until a pass has read a live session. Written by the pass,
    /// so the window reads it only when the pass has finished.
    /// </summary>
    public LoginReading LastLogin { get; private set; } = new(Readable: false, Remaining: null);

    /// <summary>
    /// True when the listing shows the session and it is not finished: not done, stopped or failed (the states the supervisor's own rules
    /// use). A session the listing does not show is not live.
    /// </summary>
    private static bool IsLive(IReadOnlyList<AgentInfo> agents, string sessionId)
    {
        var listed = agents.FirstOrDefault(a => string.Equals(a.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (listed is null) return false;
        var state = listed.EffectiveState;
        return !SupervisorPolicy.IsStoppedOrFailed(state) && !string.Equals(state, "done", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes the login alert, at most once per <see cref="LoginAlertInterval"/>. A clock that went back starts the count again.</summary>
    private void AlertLogin(string name, TimeSpan remaining)
    {
        var now = _clock.GetUtcNow();
        if (_lastLoginAlert is { } last && now >= last && now - last < LoginAlertInterval) return;
        _lastLoginAlert = now;
        _log($"{name}: ALERT {AlertKind.LoginExpiring} - the login expires in {remaining.TotalHours:0} hours. Run /login.");
    }
}

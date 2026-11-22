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

    /// <summary>The shortest time between two reads of the login, while it is not expiring soon (reliability batch R4).</summary>
    public static readonly TimeSpan LoginReadInterval = TimeSpan.FromHours(1);

    private readonly SupervisorRunner _runner;
    private readonly ClaudeCli _cli;
    private readonly StateStore _store;
    private readonly Action<string> _log;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _lastLoginAlert;
    private DateTimeOffset? _lastLoginRead;
    private bool _expiringSoon;
    private LoginReading _lastReading = new(Readable: false, Remaining: null);

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

        // The login belongs to the account, so one live session tells us for all of them (reliability batch R4). It is read at most once an
        // hour, and on every pass while the last reading showed the login expiring soon. A failed read counts as a read: it is not retried
        // on the next pass. The reading the window shows is the last one made, so the passes between two reads show the same thing.
        var now = _clock.GetUtcNow();
        var session = _store.Load().Values.FirstOrDefault(record =>
            !string.IsNullOrEmpty(record.SessionId) && IsLive(agents, record.SessionId));
        if (session is null)
        {
            // Nothing live to read this pass: nothing is read, and the window shows that.
            LastLogin = new LoginReading(Readable: false, Remaining: null);
            return actions.Count;
        }

        if (LoginReadDue(now))
        {
            _lastLoginRead = now;
            string output;
            try
            {
                output = await _cli.LogsAsync(ClaudeCli.ShortIdOf(session.SessionId), cancellationToken);
            }
            catch (ClaudeCliException ex)
            {
                // A session that has just gone cannot be read. The runner handles the restart; the next read is an hour away.
                _log($"{session.Role}: output not readable this pass ({ex.GetType().Name})");
                _expiringSoon = false;
                _lastReading = new LoginReading(Readable: false, Remaining: null);
                LastLogin = _lastReading;
                return actions.Count;
            }

            var warning = LoginExpiry.Find(output);
            _expiringSoon = warning is { } remaining && LoginExpiry.IsExpiringSoon(remaining);
            _lastReading = new LoginReading(Readable: true, Remaining: warning);
            // The first session that shows the expiry says it for the account (the alert is limited to once an hour, see AlertLogin).
            if (_expiringSoon) AlertLogin(warning!.Value);
        }
        LastLogin = _lastReading;

        return actions.Count;
    }

    /// <summary>
    /// The login as the last read of it made (night batch N2). Not readable until a pass has read a live session. Written by the pass,
    /// so the window reads it only when the pass has finished.
    /// </summary>
    public LoginReading LastLogin { get; private set; } = new(Readable: false, Remaining: null);

    /// <summary>
    /// Whether the login is read on this pass: no read yet, the last one is an hour old (or the clock went back), or the last reading showed the
    /// login expiring soon and it has not cleared (reliability batch R4).
    /// </summary>
    private bool LoginReadDue(DateTimeOffset now) =>
        _expiringSoon
        || _lastLoginRead is not { } last
        || now < last
        || now - last >= LoginReadInterval;

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
    private void AlertLogin(TimeSpan remaining)
    {
        var now = _clock.GetUtcNow();
        if (_lastLoginAlert is { } last && now >= last && now - last < LoginAlertInterval) return;
        _lastLoginAlert = now;
        _log($"ALERT {AlertKind.LoginExpiring} - the login expires in {remaining.TotalHours:0} hours. Run /login.");
    }
}

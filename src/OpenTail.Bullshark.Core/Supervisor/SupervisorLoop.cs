using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// One pass of the headless supervisor: restart what is gone, alert on what is blocked, and warn before the
/// account login expires (finding F8). It runs without any window; the window is only a view on the same state.
/// </summary>
public sealed class SupervisorLoop
{
    private readonly SupervisorRunner _runner;
    private readonly ClaudeCli _cli;
    private readonly StateStore _store;
    private readonly Action<string> _log;

    public SupervisorLoop(SupervisorRunner runner, ClaudeCli cli, StateStore store, Action<string> log)
    {
        _runner = runner;
        _cli = cli;
        _store = store;
        _log = log;
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

        foreach (var record in _store.Load().Values)
        {
            // A failed start has no session to read yet.
            if (string.IsNullOrEmpty(record.SessionId)) continue;

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

            if (LoginExpiry.Find(output) is { } remaining && LoginExpiry.IsExpiringSoon(remaining))
                _log($"{record.Name}: ALERT {AlertKind.LoginExpiring} - the login expires in {remaining.TotalHours:0} hours. Run /login.");
        }

        return actions.Count;
    }
}

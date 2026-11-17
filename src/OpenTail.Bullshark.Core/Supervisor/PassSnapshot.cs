using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// What a pass decides before it starts anything: which specs may run now, and which repository roots wait for trust.
/// <see cref="Problem"/> is set while Claude's trust record cannot be read; then nothing runs.
/// </summary>
public sealed record TrustPlan(
    IReadOnlyList<SessionSpec> Runnable,
    IReadOnlyList<string> UntrustedRoots,
    int Waiting,
    string? Problem);

/// <summary>
/// The rules a supervisor pass applies around the runner, as pure functions with no files, processes or window. The window
/// reads what they need on a pool thread and draws what they return. The rules live here so that a test can reach them.
/// </summary>
public static class PassSnapshot
{
    /// <summary>
    /// Splits the specs by trust. While Claude's trust record cannot be read, trust is unknown: nothing runs, and no root is
    /// judged untrusted. That way the window does not tell the owner to accept trust that may already be recorded.
    /// </summary>
    public static TrustPlan Trust(IReadOnlyList<SessionSpec> specs, ClaudeTrust trust)
    {
        if (trust.Problem is not null) return new TrustPlan([], [], 0, trust.Problem);

        var (runnable, roots) = TrustGate.Filter(specs, trust);
        var needed = roots.Select(FolderKey.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waiting = specs.Count(s => needed.Contains(FolderKey.From(s.WorkingDirectory)));
        return new TrustPlan(runnable, roots, waiting, null);
    }

    /// <summary>
    /// The starts that failed: a record with no session id. The owner can ask for them to be tried again, and the supervisor
    /// does not try them on its own.
    /// </summary>
    public static int FailedStarts(IEnumerable<SessionRecord> records) =>
        records.Count(r => string.IsNullOrEmpty(r.SessionId));

    /// <summary>
    /// The parts of the status line after a pass, in the order the owner reads them. The first part is the time and the number
    /// of actions; the last says that closing the window keeps the sessions running.
    /// </summary>
    public static IReadOnlyList<string> StatusParts(
        DateTime now,
        int actions,
        int waiting,
        int failed,
        string? storeProblem,
        string? trustProblem,
        string? daemonProblem = null,
        string? versionNotice = null)
    {
        var parts = new List<string> { $"{now:HH:mm:ss}  {actions} action(s) this pass" };
        if (daemonProblem is not null) parts.Add(daemonProblem);
        if (versionNotice is not null) parts.Add(versionNotice);
        if (waiting > 0) parts.Add($"{waiting} session(s) wait for trust (see the Trust step)");
        if (failed > 0) parts.Add($"{failed} start(s) failed: see the rail, then Retry failed starts");
        if (storeProblem is not null) parts.Add($"state file cannot be read, nothing is changed: {storeProblem}");
        if (trustProblem is not null) parts.Add(trustProblem);
        parts.Add("closing the window detaches the terminals; the sessions keep running");
        return parts;
    }
}

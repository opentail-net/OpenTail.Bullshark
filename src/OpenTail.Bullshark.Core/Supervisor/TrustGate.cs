using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The trust rule for runs (D-S2, F24): a session in an untrusted repository cannot start, so its run waits for trust.
/// Other runs are not held back by it. OpenTail.Bullshark only reads trust; the owner accepts it in Claude.
/// </summary>
public static class TrustGate
{
    /// <summary>
    /// Splits the specs into those that can run now and the repository roots that are waiting for trust. A folder that is
    /// not in a repository needs no trust and runs.
    /// </summary>
    /// <summary>Reads Claude's file once for the whole call. A pass that also needs the problem uses the <see cref="ClaudeTrust"/> overload.</summary>
    public static (IReadOnlyList<SessionSpec> Runnable, IReadOnlyList<string> UntrustedRoots) Filter(
        IEnumerable<SessionSpec> specs,
        string claudeJsonPath) =>
        Filter(specs, ClaudeTrust.Read(claudeJsonPath));

    /// <summary>
    /// The same split from a file that has already been read. Every folder is judged on that one read, so a write by Claude
    /// between two folders cannot make one folder look untrusted and the next trusted.
    /// </summary>
    public static (IReadOnlyList<SessionSpec> Runnable, IReadOnlyList<string> UntrustedRoots) Filter(
        IEnumerable<SessionSpec> specs,
        ClaudeTrust trust)
    {
        var runnable = new List<SessionSpec>();
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in specs.GroupBy(s => FolderKey.From(s.WorkingDirectory), StringComparer.OrdinalIgnoreCase))
        {
            var folder = group.First().WorkingDirectory;
            var check = TrustCheck.Check(folder, trust);
            if (check.State == TrustState.NotTrusted)
            {
                if (seen.Add(FolderKey.From(check.RepositoryRoot!))) roots.Add(check.RepositoryRoot!);
            }
            else
            {
                runnable.AddRange(group);
            }
        }
        return (runnable, roots);
    }
}

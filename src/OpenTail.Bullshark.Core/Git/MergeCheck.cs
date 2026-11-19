namespace OpenTail.Bullshark.Core.Git;

/// <summary>What the merge check found: the committed work merges cleanly, conflicts (with the files), or could not be checked.</summary>
public enum MergeCheckKind
{
    Clean,
    Conflicts,
    NotARepository,
    NoDefaultBranch,
    Problem,
}

/// <summary>
/// The result of a merge check. <see cref="Target"/> is the branch the work was merged into (empty when none was found).
/// <see cref="Files"/> lists the conflicting paths, exactly as git names them, only for <see cref="MergeCheckKind.Conflicts"/>.
/// </summary>
public sealed record MergeCheckReport(MergeCheckKind Kind, string Folder, string Target, IReadOnlyList<string> Files, string Message);

/// <summary>
/// Asks git whether a coder's committed work would merge cleanly into the default branch (gems item 2, the agtx idea). It is run when
/// a coder enters `done`. It checks committed work only: uncommitted changes are not in HEAD, and the message says so.
/// <para>
/// `git merge-tree --write-tree` builds the merged tree as objects in the object database. It moves no branch, and it does not touch
/// the index or the working tree. The objects it leaves are unreferenced and git removes them in a later garbage collection.
/// </para>
/// </summary>
public static class MergeCheck
{
    /// <summary>The same limit as the Changes tab: each command has 10 seconds and 2 MB of output.</summary>
    public static readonly TimeSpan CommandTimeout = GitChanges.CommandTimeout;

    public const int MaxOutputBytes = GitChanges.MaxOutputBytes;

    /// <summary>The note every result carries, so the owner knows what the check did not look at.</summary>
    public const string CommittedOnlyNote = "Checks committed work only: uncommitted changes are not in HEAD, so they are not checked.";

    /// <summary>Runs the check for one folder. Read-only for refs, the index and the working tree; nothing is pushed or fetched.</summary>
    public static async Task<MergeCheckReport> RunAsync(string folder, IGitRunner git, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
            return Problem(folder, $"The folder does not exist: {folder}");

        var remote = await git.RunAsync(["symbolic-ref", "refs/remotes/origin/HEAD"], folder, CommandTimeout, MaxOutputBytes, cancellationToken);
        if (IsNotARepository(remote))
            return new MergeCheckReport(MergeCheckKind.NotARepository, folder, string.Empty, [],
                "This folder is not in a git repository, so there is no merge to check.");
        if (remote.TimedOut) return Problem(folder, Describe("symbolic-ref", remote));

        string? target = null;
        if (remote.Succeeded && remote.StdOut.Trim() is { Length: > 0 } remoteHead)
        {
            target = remoteHead;
        }
        else
        {
            // No origin/HEAD (a repository with no remote, or one that was never set up): the local main, then master.
            foreach (var candidate in new[] { "main", "master" })
            {
                var found = await git.RunAsync(["rev-parse", "--verify", "--quiet", "refs/heads/" + candidate], folder, CommandTimeout, MaxOutputBytes, cancellationToken);
                if (found.TimedOut) return Problem(folder, Describe("rev-parse", found));
                if (found.Succeeded)
                {
                    target = candidate;
                    break;
                }
            }
        }
        if (target is null)
            return new MergeCheckReport(MergeCheckKind.NoDefaultBranch, folder, string.Empty, [],
                "No default branch is known (no origin/HEAD, no main, no master), so there is no merge to check. " + CommittedOnlyNote);

        // -z: the names come out exactly, with no quoting of spaces or non-ASCII characters.
        var merge = await git.RunAsync(["merge-tree", "--write-tree", "--name-only", "-z", target, "HEAD"], folder, CommandTimeout, MaxOutputBytes, cancellationToken);
        if (merge.TimedOut) return Problem(folder, Describe("merge-tree", merge));

        if (merge.Succeeded)
            return new MergeCheckReport(MergeCheckKind.Clean, folder, target, [],
                $"The committed work merges cleanly into {target}. {CommittedOnlyNote}");

        // Exit 1 with a tree id and a file list is a conflict. Anything else (a missing ref, an unborn HEAD) is a problem.
        if (merge.ExitCode == 1 && ConflictFiles(merge.StdOut) is { } files)
        {
            var count = files.Count == 1 ? "1 file" : $"{files.Count} files";
            return new MergeCheckReport(MergeCheckKind.Conflicts, folder, target, files,
                $"The committed work conflicts with {target} in {count}. {CommittedOnlyNote}");
        }
        return Problem(folder, Describe("merge-tree", merge));
    }

    /// <summary>
    /// True when a coder's check should be made again on a later pass while it stays `done` (round 8 item 6). A problem is not a verdict: the
    /// folder may come back, or git may stop timing out. A clean, conflicting or not-a-repository result is kept until the coder leaves `done`.
    /// </summary>
    public static bool ShouldRecheck(MergeCheckReport report) => report.Kind == MergeCheckKind.Problem;

    /// <summary>A branch name for a person to read: the refs prefix is dropped, so "refs/remotes/origin/main" shows as "origin/main".</summary>
    public static string DisplayName(string target) =>
        target.StartsWith("refs/remotes/", StringComparison.Ordinal) ? target["refs/remotes/".Length..]
        : target.StartsWith("refs/heads/", StringComparison.Ordinal) ? target["refs/heads/".Length..]
        : target;

    /// <summary>
    /// The conflicting paths from `merge-tree --write-tree --name-only -z`. The output is the tree id, then one NUL-ended path per
    /// conflicting file, then an empty field, then the messages (which are ignored). Null when the list is not there or is cut short.
    /// </summary>
    public static IReadOnlyList<string>? ConflictFiles(string output)
    {
        var fields = output.Split('\0');
        if (fields.Length < 2 || !IsObjectId(fields[0])) return null;

        var files = new List<string>();
        for (var i = 1; i < fields.Length; i++)
        {
            if (fields[i].Length == 0) return files;
            files.Add(fields[i]);
        }
        // No empty field: the list was cut at the limit, so it is not trusted.
        return null;
    }

    /// <summary>
    /// The commit HEAD of a folder points to, or null when it cannot be read (no commit yet, or not a repository). Used by the schedule of
    /// rechecks (<see cref="MergeRecheck"/>): a problem is rechecked after HEAD moves, and not more often than every 30 minutes while it stays put.
    /// </summary>
    public static async Task<string?> HeadOfAsync(string folder, IGitRunner git, CancellationToken cancellationToken = default)
    {
        var head = await git.RunAsync(["rev-parse", "--verify", "--quiet", "HEAD"], folder, CommandTimeout, MaxOutputBytes, cancellationToken);
        var text = head.StdOut.Trim();
        return head.Succeeded && IsObjectId(text) ? text : null;
    }

    /// <summary>A tree or commit id: 40 hex digits (SHA-1) or 64 (SHA-256).</summary>
    private static bool IsObjectId(string text) =>
        text.Length is 40 or 64 && text.All(Uri.IsHexDigit);

    private static bool IsNotARepository(GitOutput output) =>
        !output.Succeeded && !output.TimedOut && output.StdErr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);

    private static string Describe(string command, GitOutput output)
    {
        if (output.TimedOut) return $"The merge check did not finish: git {command} took longer than {CommandTimeout.TotalSeconds:0} seconds.";
        var firstLine = output.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return $"The merge check failed: git {command} exited {output.ExitCode}: {firstLine ?? "no message"}";
    }

    private static MergeCheckReport Problem(string folder, string message) =>
        new(MergeCheckKind.Problem, folder, string.Empty, [], message);
}

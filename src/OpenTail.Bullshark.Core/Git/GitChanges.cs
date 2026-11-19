using System.Globalization;
using System.Text;

namespace OpenTail.Bullshark.Core.Git;

/// <summary>What the Changes tab says about a folder: a list of changes, a plain note (not a repository), or a problem.</summary>
public enum GitChangesKind
{
    Changes,
    NotARepository,
    Problem,
}

/// <summary>
/// One changed path. <see cref="Code"/> is the two-letter status from `git status --porcelain=v1`. <see cref="Added"/> and
/// <see cref="Deleted"/> are the line counts of `git diff`; both are null for an untracked file or a file with no counts.
/// <see cref="Binary"/> is set when git counts no lines for a binary file.
/// </summary>
public sealed record GitFileChange(string Path, string? OldPath, string Code, int? Added, int? Deleted, bool Binary)
{
    /// <summary>The status in plain words, for the list.</summary>
    public string Label
    {
        get
        {
            if (Code is "UU" or "AA" or "DD" or "AU" or "UA" or "DU" or "UD") return "conflict";
            if (Code == "??") return "new, not in the diff";
            // A rename staged in the index is a rename, even when the worktree has changed it since. A file added to the index is new against HEAD
            // (the diff HEAD shows it whole), even when the worktree has changed it since (AM); AD is still deleted from the folder (review R9).
            var keepsIndex = Code[0] is 'R' or 'C' || (Code[0] == 'A' && Code[1] != 'D');
            var letter = keepsIndex ? Code[0] : Code[1] != ' ' ? Code[1] : Code[0];
            return letter switch
            {
                'A' => "added",
                'D' => "deleted",
                'R' => "renamed",
                'C' => "copied",
                'T' => "type changed",
                _ => "modified",
            };
        }
    }
}

/// <summary>The Changes tab's data for one folder. The text is in full, up to the limits in <see cref="GitChanges"/>.</summary>
public sealed record GitChangesReport(
    GitChangesKind Kind,
    string Folder,
    string Message,
    IReadOnlyList<GitFileChange> Files,
    bool FilesTruncated,
    string Diff,
    bool DiffTruncated);

/// <summary>
/// Reads what a session's folder has changed, through git, read-only (the Changes tab, item 2 of the additions plan). `status` for the list,
/// `diff --numstat` for the counts and `diff` for the text, against HEAD (staged and unstaged together); `rev-parse` tells an unborn HEAD apart. A folder that is not a repository is a plain
/// message, not an error. The output of each command is capped at 2 MB and each command runs for at most 10 seconds.
/// </summary>
public static class GitChanges
{
    /// <summary>The most output kept from one command.</summary>
    public const int MaxOutputBytes = 2 * 1024 * 1024;

    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The line added to the end of a diff that was cut at the limit.</summary>
    public const string DiffTruncatedNote = "[truncated: the diff is longer than 2 MB, so the rest is not shown]";

    /// <summary>
    /// The folder for a session: the working folder from its listing, else the folder of its spec, each only when it exists. Null when neither
    /// does. It never throws (round 8 item 6): a folder that is gone must not be read by the Changes tab or the merge check.
    /// </summary>
    public static string? FolderOf(string? listedFolder, string? specFolder) =>
        ExistingFolder(listedFolder) ?? ExistingFolder(specFolder);

    /// <summary>
    /// The folder a session's changes are read from (round 7 item 4, round 8 item 6): the folder its transcript names last, which is the one it edits
    /// now and can be a worktree under `.claude/worktrees`, in its full normalised form, when that folder exists; else the listing's folder; else the
    /// spec's. A folder that is gone is skipped, and so is one the path normaliser rejects: the same folder spelled two ways is the same folder.
    /// </summary>
    public static string? FolderOf(string? transcriptFolder, string? listedFolder, string? specFolder) =>
        NormalisedExistingFolder(transcriptFolder) ?? FolderOf(listedFolder, specFolder);

    private static string? ExistingFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;

    /// <summary>The folder in its full form when the normaliser accepts it and it exists; null otherwise, and never a throw.</summary>
    private static string? NormalisedExistingFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            var full = Folders.FolderKey.Normalise(folder);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception)
        {
            // An invalid path (the normaliser throws on it) is not a folder: the candidate is skipped, and the listing's folder is tried.
            return null;
        }
    }

    /// <summary>Reads the changes of a folder. Nothing is written, and no git command can start a prompt or take the index lock.</summary>
    public static async Task<GitChangesReport> ReadAsync(string folder, IGitRunner git, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
            return Problem(folder, $"The folder does not exist: {folder}");

        var status = await git.RunAsync(["status", "--porcelain=v1", "-z"], folder, CommandTimeout, MaxOutputBytes, cancellationToken);
        if (IsNotARepository(status))
            return new GitChangesReport(GitChangesKind.NotARepository, folder,
                "This folder is not in a git repository, so there are no changes to show.", [], false, string.Empty, false);
        if (!Usable(status)) return Problem(folder, Describe("status", status));

        // The staged and the unstaged changes are read together (F2): `diff HEAD` compares the working tree with HEAD, so a file staged in the
        // index shows its hunks and counts as well as its unstaged ones. With no commit yet there is no HEAD: then the unstaged part (`diff`)
        // and the staged part (`diff --cached`, against the empty tree) are read separately and put together.
        var head = await git.RunAsync(["rev-parse", "--verify", "--quiet", "HEAD"], folder, CommandTimeout, MaxOutputBytes, cancellationToken);
        if (head.TimedOut) return Problem(folder, Describe("rev-parse", head));
        var numstatCommands = head.Succeeded
            ? new[] { (Label: "diff --numstat", Args: NumstatHead) }
            : new[] { (Label: "diff --numstat", Args: NumstatWorktree), (Label: "diff --cached --numstat", Args: NumstatCached) };
        var diffCommands = head.Succeeded
            ? new[] { (Label: "diff", Args: DiffHead) }
            : new[] { (Label: "diff", Args: DiffWorktree), (Label: "diff --cached", Args: DiffCached) };

        var numstats = new List<GitOutput>();
        foreach (var (label, args) in numstatCommands)
        {
            var numstat = await git.RunAsync(args, folder, CommandTimeout, MaxOutputBytes, cancellationToken);
            if (!Usable(numstat)) return Problem(folder, Describe(label, numstat));
            numstats.Add(numstat);
        }

        var diffs = new List<GitOutput>();
        foreach (var (label, args) in diffCommands)
        {
            var diff = await git.RunAsync(args, folder, CommandTimeout, MaxOutputBytes, cancellationToken);
            if (!Usable(diff)) return Problem(folder, Describe(label, diff));
            diffs.Add(diff);
        }

        var counts = CountsOf(numstats);
        var files = ParseStatus(status.OutputTruncated ? CutAtLastNul(status.StdOut) : status.StdOut)
            .Select(file => counts.TryGetValue(file.Path, out var count)
                ? file with { Added = count.Added, Deleted = count.Deleted, Binary = count.Binary }
                : file)
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToList();

        // The parts are joined in order. A part cut at the limit ends the text, so the total is never more than one part past 2 MB.
        var text = new StringBuilder();
        var diffTruncated = false;
        foreach (var diff in diffs)
        {
            text.Append(diff.StdOut);
            if (!diff.OutputTruncated) continue;
            diffTruncated = true;
            break;
        }
        var diffText = diffTruncated ? text.ToString().TrimEnd('\n') + "\n" + DiffTruncatedNote : text.ToString();

        var message = status.OutputTruncated
            ? "The file list is longer than 2 MB, so the rest is not shown."
            : files.Count == 0 ? "No changes: git sees nothing new or modified here." : string.Empty;
        return new GitChangesReport(GitChangesKind.Changes, folder, message, files, status.OutputTruncated, diffText, diffTruncated);
    }

    // The arguments of the diff commands. --no-textconv and --no-ext-diff keep a repository's own diff drivers from running (F3).
    private static readonly string[] NumstatHead = ["diff", "--numstat", "-z", "--no-textconv", "HEAD"];
    private static readonly string[] DiffHead = ["diff", "--no-color", "--no-ext-diff", "--no-textconv", "HEAD"];
    private static readonly string[] NumstatWorktree = ["diff", "--numstat", "-z", "--no-textconv"];
    private static readonly string[] DiffWorktree = ["diff", "--no-color", "--no-ext-diff", "--no-textconv"];
    private static readonly string[] NumstatCached = ["diff", "--cached", "--numstat", "-z", "--no-textconv"];
    private static readonly string[] DiffCached = ["diff", "--cached", "--no-color", "--no-ext-diff", "--no-textconv"];

    /// <summary>
    /// The counts of one or more `diff --numstat -z` outputs, keyed by path. A path in two outputs (staged and unstaged, with no HEAD) gets the
    /// sum of its counts; a binary count in either makes the sum binary with no counts.
    /// </summary>
    private static IReadOnlyDictionary<string, NumstatCount> CountsOf(IEnumerable<GitOutput> outputs)
    {
        var counts = new Dictionary<string, NumstatCount>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            foreach (var (path, count) in ParseNumstat(output.OutputTruncated ? CutAtLastNul(output.StdOut) : output.StdOut))
            {
                counts[path] = counts.TryGetValue(path, out var sum)
                    ? new NumstatCount(sum.Added + count.Added, sum.Deleted + count.Deleted, sum.Binary || count.Binary)
                    : count;
            }
        }
        return counts;
    }

    /// <summary>
    /// The entries of `git status --porcelain=v1 -z`: "XY path" followed by NUL. A rename or copy has the old path in the next field,
    /// so both are read. Counts are not set here.
    /// </summary>
    public static IReadOnlyList<GitFileChange> ParseStatus(string output)
    {
        var fields = output.Split('\0');
        var files = new List<GitFileChange>();
        for (var i = 0; i < fields.Length; i++)
        {
            var entry = fields[i];
            if (entry.Length < 4) continue;
            var code = entry[..2];
            string? oldPath = null;
            if (code[0] is 'R' or 'C' || code[1] is 'R' or 'C')
            {
                oldPath = i + 1 < fields.Length ? fields[++i] : null;
            }
            files.Add(new GitFileChange(entry[3..], oldPath, code, null, null, false));
        }
        return files;
    }

    /// <summary>
    /// The counts of `git diff --numstat -z`, keyed by path. A line is "added TAB deleted TAB path" followed by NUL. A rename is
    /// "added TAB deleted TAB" followed by NUL, then the old path and the new path, each followed by NUL; the count goes under the new
    /// path. A binary file has "-" for both counts.
    /// </summary>
    public static IReadOnlyDictionary<string, NumstatCount> ParseNumstat(string output)
    {
        var fields = output.Split('\0');
        var counts = new Dictionary<string, NumstatCount>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length; i++)
        {
            var line = fields[i];
            var first = line.IndexOf('\t');
            if (first < 0) continue;
            var second = line.IndexOf('\t', first + 1);
            if (second < 0) continue;

            var added = line[..first];
            var deleted = line[(first + 1)..second];
            var path = line[(second + 1)..];
            var count = new NumstatCount(ParseCount(added), ParseCount(deleted), Binary: added == "-" && deleted == "-");
            if (path.Length == 0)
            {
                if (i + 2 >= fields.Length) break;
                counts[fields[i + 2]] = count;
                i += 2;
            }
            else
            {
                counts[path] = count;
            }
        }
        return counts;
    }

    /// <summary>The counts of one changed path, from `git diff --numstat`.</summary>
    public sealed record NumstatCount(int? Added, int? Deleted, bool Binary);

    private static int? ParseCount(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>Output cut at the limit may end inside a field. The text after the last NUL is dropped.</summary>
    private static string CutAtLastNul(string output)
    {
        var last = output.LastIndexOf('\0');
        return last < 0 ? string.Empty : output[..(last + 1)];
    }

    private static bool IsNotARepository(GitOutput output) =>
        !output.Succeeded && !output.TimedOut && output.StdErr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);

    /// <summary>A command's output can be used when it succeeded, or when it was cut at the limit (its exit code is then the stop).</summary>
    private static bool Usable(GitOutput output) => output.Succeeded || output.OutputTruncated;

    private static string Describe(string command, GitOutput output)
    {
        if (output.TimedOut) return $"git {command} did not finish within {CommandTimeout.TotalSeconds:0} seconds.";
        var firstLine = output.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return $"git {command} failed (exit {output.ExitCode}): {firstLine ?? "no message"}";
    }

    private static GitChangesReport Problem(string folder, string message) =>
        new(GitChangesKind.Problem, folder, message, [], false, string.Empty, false);
}

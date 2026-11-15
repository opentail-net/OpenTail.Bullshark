using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.Core.Folders;

/// <summary>
/// The name Claude gives a project folder under <c>~/.claude/projects</c>: the working directory with every character
/// outside A-Z, a-z and 0-9 replaced by <c>-</c> (F44). It groups a run's folder with the history groups. It does not
/// find a session's transcript: that is found by session id (<c>TranscriptReader.FindTranscript</c>).
/// </summary>
public static class FolderKey
{
    private static readonly Regex NotAlphanumeric = new("[^A-Za-z0-9]", RegexOptions.Compiled);

    public static string From(string folder) => NotAlphanumeric.Replace(Normalise(folder), "-");

    /// <summary>The full path without a trailing separator. A drive root keeps its separator, so C:\ stays C:\ (F44).</summary>
    public static string Normalise(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));

    /// <summary>Two folders are the same group when their keys match. Drive letters and typed paths vary in case.</summary>
    public static bool Same(string a, string b) =>
        string.Equals(From(a), From(b), StringComparison.OrdinalIgnoreCase);
}

public enum FolderStatus
{
    /// <summary>The path is not valid, or the folder does not exist.</summary>
    Missing,

    /// <summary>The folder is in a git repository whose root Claude has recorded as trusted.</summary>
    Trusted,

    /// <summary>The folder is in a git repository whose root is not recorded as trusted.</summary>
    NotTrusted,

    /// <summary>The folder is not in a git repository, so no trust is needed.</summary>
    NotARepository,
}

public sealed record FolderCheckResult(string FullPath, string Key, FolderStatus Status, string? RepositoryRoot, string Message);

/// <summary>
/// Checks a folder the owner typed or chose (D-S2). Read only: it writes no file, not even when the folder is missing.
/// </summary>
public static class FolderCheck
{
    public static FolderCheckResult Check(string folder, string claudeJsonPath)
    {
        string full;
        try
        {
            full = FolderKey.Normalise(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return new FolderCheckResult(folder ?? string.Empty, string.Empty, FolderStatus.Missing, null,
                "This is not a valid folder path.");
        }

        var key = FolderKey.From(full);
        if (!Directory.Exists(full))
            return new FolderCheckResult(full, key, FolderStatus.Missing, null, "This folder does not exist.");

        var trust = TrustCheck.Check(full, claudeJsonPath);
        var status = trust.State switch
        {
            TrustState.Trusted => FolderStatus.Trusted,
            TrustState.NotTrusted => FolderStatus.NotTrusted,
            _ => FolderStatus.NotARepository,
        };
        return new FolderCheckResult(full, key, status, trust.RepositoryRoot, trust.Explanation);
    }
}

/// <summary>
/// The folders the setup page offers first (D-S2): the trusted repositories, most recently used first, then the other
/// trusted ones. Untrusted folders are not offered; they can still be typed, and then the Trust tab opens.
/// </summary>
public static class FolderSuggestions
{
    /// <param name="runs">The runs from <c>RunStore.Load()</c>. Never <c>LoadOrMigrate</c>: this read must not write.</param>
    public static IReadOnlyList<string> Build(IEnumerable<RunRecord> runs, string claudeJsonPath)
    {
        // Position of each folder in the run history, newest run first. The first use counts.
        var recency = runs
            .OrderByDescending(r => r.CreatedAt)
            .Select((run, index) => (Key: FolderKey.From(run.WorkingDirectory), Index: index))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Index), StringComparer.OrdinalIgnoreCase);

        return TrustCheck.TrustedRepositories(claudeJsonPath)
            .Select(Safe)
            .OfType<string>()
            .DistinctBy(FolderKey.From, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => recency.ContainsKey(FolderKey.From(p)) ? 0 : 1)
            .ThenBy(p => recency.TryGetValue(FolderKey.From(p), out var index) ? index : int.MaxValue)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Safe(string path)
    {
        try
        {
            return FolderKey.Normalise(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}

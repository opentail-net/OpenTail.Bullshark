namespace OpenTail.Bullshark.Core.Folders;

/// <summary>What a folder group is called on screen (P9): its name, and the full working folder for a tooltip (null when none is known).</summary>
public sealed record FolderLabel(string Name, string? FullPath);

/// <summary>
/// The name a person sees for a Claude project folder (P9). Claude names the folder under <c>~/.claude/projects</c> from the working folder with
/// every character outside A-Z, a-z and 0-9 changed to <c>-</c>, so the name is lossy: <c>C--Git</c> is <c>C:\Git</c>, and a real dash is the
/// same as a separator. The transcripts name their working folder (the last cwd of the transcript). The name shown is the leaf of the most common
/// of those folders that encode to the group's own key: a session that moved on to another folder does not name the group, because its folder
/// is not the one the group was made for. The key stays the group's identity (<see cref="FolderKey"/>); this only chooses the words.
/// </summary>
public static class FolderDisplay
{
    /// <summary>
    /// The label of the group whose key is <paramref name="projectFolderKey"/>, from the working folders its transcripts name. A folder counts only
    /// when it encodes to that key (without case): null, blank, relative and other-key folders are not counted. The one most often named wins, and
    /// a tie goes to the first in ordinal order; the spelling most often written is shown. The name is the folder's leaf, or the whole path for a
    /// drive root or a share root. With no folder that counts, the name is the key and there is no tooltip path.
    /// </summary>
    public static FolderLabel Of(string projectFolderKey, IEnumerable<string?> cwds)
    {
        var folders = cwds
            .Select(Full)
            .OfType<string>()
            .Where(folder => string.Equals(FolderKey.From(folder), projectFolderKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (folders.Count == 0) return new FolderLabel(projectFolderKey, null);

        // The most common folder, without case. Within it, the spelling most often written (then ordinal) is the one shown.
        var chosen = folders
            .GroupBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .First();
        var full = chosen
            .GroupBy(folder => folder, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First().Key;

        var leaf = Path.GetFileName(full);
        return new FolderLabel(leaf.Length == 0 ? full : leaf, full);
    }

    /// <summary>
    /// The labels of several groups at once, by key (case-insensitive), from the sessions of all of them: each key takes <see cref="Of"/> over the
    /// working folders of the sessions that carry that key. Two keys that get the same name are told apart: each gets its parent folder in
    /// brackets, <c>repo (Git)</c>, and if those still match, the key, <c>repo (C--Git-repo)</c>. Only names change; the keys are the groups.
    /// </summary>
    public static IReadOnlyDictionary<string, FolderLabel> LabelsFor(IEnumerable<string> keys, IEnumerable<(string Key, string? Cwd)> sessions)
    {
        var cwds = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, cwd) in sessions)
        {
            if (!cwds.TryGetValue(key, out var list)) cwds[key] = list = [];
            list.Add(cwd);
        }

        var labels = new Dictionary<string, FolderLabel>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key) || labels.ContainsKey(key)) continue;
            labels[key] = Of(key, cwds.TryGetValue(key, out var known) ? known : []);
        }

        // A name shared by two keys gets the parent folder; a name still shared gets the key. Both are read from the base names.
        var baseNames = labels.ToDictionary(pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase);
        var shown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in labels.GroupBy(pair => baseNames[pair.Key], StringComparer.OrdinalIgnoreCase).ToList())
        {
            var shared = group.Count() > 1;
            foreach (var pair in group)
            {
                var parent = pair.Value.FullPath is null ? null : Path.GetFileName(Path.GetDirectoryName(pair.Value.FullPath));
                shown[pair.Key] = shared && !string.IsNullOrEmpty(parent) ? $"{group.Key} ({parent})" : group.Key;
            }
        }
        var finalNames = shown.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g.Select(p => p.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in finalNames) shown[key] = $"{baseNames[key]} ({key})";

        foreach (var key in labels.Keys.ToList()) labels[key] = labels[key] with { Name = shown[key] };
        return labels;
    }

    /// <summary>The folder as written, without a trailing separator (a drive root keeps its own), or null when it is not an absolute path.</summary>
    private static string? Full(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            var trimmed = folder.Trim();
            if (!Path.IsPathFullyQualified(trimmed)) return null;
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

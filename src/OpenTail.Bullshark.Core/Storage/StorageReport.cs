namespace OpenTail.Bullshark.Core.Storage;

/// <summary>
/// One project folder under the projects root: how many transcripts it holds (subagent files included), their total size, and the
/// oldest and newest write times. <see cref="Name"/> is the folder's own name, which Claude encodes from the working folder.
/// </summary>
public sealed record ProjectStorage(string Name, string Folder, int Transcripts, long Bytes, DateTimeOffset? Oldest, DateTimeOffset? Newest);

/// <summary>A transcript file, with the session id it is named for, its size and when it was last written.</summary>
public sealed record TranscriptFileInfo(string SessionId, string Path, long Bytes, DateTimeOffset Written);

/// <summary>
/// How much the transcripts take up, by project folder (gems item 6, the ccs idea). Read-only: only the names, sizes and times of the
/// files are looked at, and no transcript is opened.
/// </summary>
public static class StorageReport
{
    /// <summary>One line per project folder, largest first. A missing root is an empty list, and a folder that cannot be listed is left out.</summary>
    public static IReadOnlyList<ProjectStorage> Read(string projectsRoot)
    {
        if (!Directory.Exists(projectsRoot)) return [];

        var projects = new List<ProjectStorage>();
        foreach (var folder in Directory.EnumerateDirectories(projectsRoot))
        {
            IReadOnlyList<FileInfo> files;
            try
            {
                files = new DirectoryInfo(folder).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var newest = files.Count == 0 ? (DateTimeOffset?)null : files.Max(f => f.LastWriteTimeUtc);
            var oldest = files.Count == 0 ? (DateTimeOffset?)null : files.Min(f => f.LastWriteTimeUtc);
            projects.Add(new ProjectStorage(
                Path.GetFileName(folder),
                folder,
                files.Count,
                files.Sum(f => f.Length),
                oldest,
                newest));
        }

        return projects
            .OrderByDescending(p => p.Bytes)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The session transcripts directly in one project folder (not the subagent files), newest first.</summary>
    public static IReadOnlyList<TranscriptFileInfo> FilesOf(string projectFolder)
    {
        if (!Directory.Exists(projectFolder)) return [];
        return new DirectoryInfo(projectFolder)
            .EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(f => new TranscriptFileInfo(Path.GetFileNameWithoutExtension(f.Name), f.FullName, f.Length, f.LastWriteTimeUtc))
            .OrderByDescending(f => f.Written)
            .ToList();
    }
}

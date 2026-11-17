namespace OpenTail.Bullshark.Core.Storage;

/// <summary>What happened to one file: moved, or refused with the reason.</summary>
public sealed record ArchiveOutcome(string Path, bool Moved, string Message);

/// <summary>
/// The archive of old transcripts (gems item 6). A transcript is moved, never deleted, to the same relative place under the archive
/// root, so it can be moved back to where it was. History and search read only the projects folder, so an archived session leaves
/// them by being moved. Every move is on the caller's list; nothing is chosen here.
/// </summary>
public static class TranscriptArchive
{
    /// <summary>The archive under the owner's Claude folder. It is not under the projects folder, so Claude does not read it.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "bullshark-archive");

    /// <summary>
    /// A transcript written this recently may belong to a session that is still running, so it is not archived (round 5 item 4).
    /// </summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Moves each file into <paramref name="archiveRoot"/>, under the same relative path it has below <paramref name="projectsRoot"/>.
    /// A file is refused, and stays where it is, when: it is not under the projects folder; it is gone; the session list has not been
    /// read yet (<paramref name="protectedSessionIds"/> is null, so every file waits); its session is one of
    /// <paramref name="protectedSessionIds"/> (listed by `claude agents --all`, or recorded in the state file); it was written within
    /// <see cref="RecentWindow"/> of <paramref name="now"/> (the clock, when null); or its target already exists (nothing is
    /// overwritten). Empty folders left behind are not removed.
    /// </summary>
    public static IReadOnlyList<ArchiveOutcome> Archive(
        IEnumerable<string> paths,
        string projectsRoot,
        string archiveRoot,
        IReadOnlySet<string>? protectedSessionIds,
        DateTimeOffset? now = null)
    {
        var outcomes = new List<ArchiveOutcome>();
        var clock = now ?? DateTimeOffset.UtcNow;
        foreach (var path in paths)
        {
            var relative = RelativeInside(projectsRoot, path);
            if (relative is null)
            {
                outcomes.Add(new ArchiveOutcome(path, false, "Not in the projects folder, so it is not archived."));
                continue;
            }
            if (!File.Exists(path))
            {
                outcomes.Add(new ArchiveOutcome(path, false, "The file is gone."));
                continue;
            }
            if (protectedSessionIds is null)
            {
                outcomes.Add(new ArchiveOutcome(path, false,
                    "claude's session list has not been read yet, so nothing is archived. Try again after the next check."));
                continue;
            }
            if (protectedSessionIds.Contains(Path.GetFileNameWithoutExtension(path)))
            {
                outcomes.Add(new ArchiveOutcome(path, false, "This session is listed by claude or recorded by Bullshark, so it is not archived."));
                continue;
            }
            if (clock - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) < RecentWindow)
            {
                outcomes.Add(new ArchiveOutcome(path, false,
                    "Written in the last 24 hours, so it may be a session that is still running. It is not archived."));
                continue;
            }

            var target = Path.Combine(archiveRoot, relative);
            if (File.Exists(target))
            {
                outcomes.Add(new ArchiveOutcome(path, false, "A file of that name is already in the archive, so nothing was overwritten."));
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(path, target);
                outcomes.Add(new ArchiveOutcome(path, true, $"Archived to {target}."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another program holds the file, or the disk refused it: the file stays where it was.
                outcomes.Add(new ArchiveOutcome(path, false, $"Not moved: {ex.Message}"));
            }
        }
        return outcomes;
    }

    /// <summary>
    /// Moves archived files back to the same relative place under <paramref name="projectsRoot"/>. A file is refused when it is not
    /// under the archive root, or when a file of that name is already back in place.
    /// </summary>
    public static IReadOnlyList<ArchiveOutcome> Restore(IEnumerable<string> archivePaths, string projectsRoot, string archiveRoot)
    {
        var outcomes = new List<ArchiveOutcome>();
        foreach (var path in archivePaths)
        {
            var relative = RelativeInside(archiveRoot, path);
            if (relative is null || !File.Exists(path))
            {
                outcomes.Add(new ArchiveOutcome(path, false, "Not an archived transcript, so it is not restored."));
                continue;
            }

            var target = Path.Combine(projectsRoot, relative);
            if (File.Exists(target))
            {
                outcomes.Add(new ArchiveOutcome(path, false, "A transcript of that name is already back in place, so nothing was overwritten."));
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(path, target);
                outcomes.Add(new ArchiveOutcome(path, true, $"Restored to {target}."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new ArchiveOutcome(path, false, $"Not moved: {ex.Message}"));
            }
        }
        return outcomes;
    }

    /// <summary>The archived session transcripts, one folder per project, newest first.</summary>
    public static IReadOnlyList<TranscriptFileInfo> List(string archiveRoot)
    {
        if (!Directory.Exists(archiveRoot)) return [];
        return Directory.EnumerateDirectories(archiveRoot)
            .SelectMany(project => StorageReport.FilesOf(project))
            .OrderByDescending(f => f.Written)
            .ToList();
    }

    /// <summary>
    /// The path of <paramref name="path"/> relative to <paramref name="root"/>, or null when it is not inside the root (or is the root itself).
    /// Both are compared as full paths, so ".." cannot climb out of the root.
    /// </summary>
    private static string? RelativeInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        // Windows paths are compared without case, as the file system does; elsewhere exactly.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot, comparison)) return null;
        var relative = fullPath[fullRoot.Length..];
        return relative.Length == 0 ? null : relative;
    }
}

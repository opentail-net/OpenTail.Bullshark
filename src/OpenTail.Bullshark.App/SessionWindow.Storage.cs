using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Storage;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Storage page (gems item 6): opened from the status bar. Every read and every move runs on a pool thread. Archiving asks first
/// and lists the files. A session that claude lists, or that the state file records, is never archived: the archive refuses it.
/// </summary>
public partial class SessionWindow
{
    private readonly StorageView _storage = new() { Visibility = Visibility.Hidden };

    /// <summary>
    /// True once a pass has read claude's session list (with the completed ones), and the last pass did not fail. Until then the
    /// archive refuses every file: a session that claude lists must never be moved (round 5 item 4). Written on the UI thread only.
    /// </summary>
    private bool _listingKnown;

    /// <summary>
    /// Archiving needs both protections: the session list has been read, and the state file that records the sessions can be read.
    /// The Archive button and the archive itself follow this.
    /// </summary>
    private bool ArchiveAllowed => _listingKnown && _storeProblem is null;

    private void InitStorage()
    {
        // The Archive button starts disabled, with its note, until the first listing has been read: the button is enabled by default, and the
        // first pass only sets it at its end (round 6 item 9). The click guard in ArchiveAsync stays as well.
        _storage.SetArchiveAvailable(ArchiveAllowed);
        _storage.RefreshRequested += () => _ = RefreshStorageAsync();
        _storage.ProjectChosen += folder => _ = ShowProjectAsync(folder);
        _storage.ArchiveRequested += paths => _ = ArchiveAsync(paths);
        _storage.RestoreRequested += paths => _ = RestoreAsync(paths);
        // The empty state's button opens Claude's folder: the parent of the projects folder (firstrun batch B8).
        _storage.ClaudeFolderRequested += () => OpenFolder(Path.GetDirectoryName(Path.GetFullPath(_options.ProjectsRoot))!);
    }

    /// <summary>The project folder whose transcripts the middle list shows, so the list can be read again after a move (round 5 item 11).</summary>
    private string? _storageFolder;

    /// <summary>
    /// Reads the project sizes, the archive and the retention setting, then draws them. A folder that cannot be read is said in the note, not
    /// dropped. The chosen project is drawn again after the setting is set, so its marks follow the setting that is read now (round 8 item 11).
    /// This runs on Refresh, and after each archive and restore.
    /// </summary>
    private async Task RefreshStorageAsync()
    {
        try
        {
            // The setting is read from the settings file beside the projects folder (round 7 item 5, round 8 item 11). It is read only, and never written.
            var (projects, archived, retention) = await Task.Run(() =>
                (StorageReport.Read(_options.ProjectsRoot), TranscriptArchive.List(_options.ArchiveRoot),
                 TranscriptRetention.ReadDays(TranscriptRetention.SettingsBeside(_options.ProjectsRoot))));
            _storage.SetRetention(retention.Days ?? TranscriptRetention.DefaultDays, retention.Days is not null, retention.Problem);
            _storageProjects = projects;
            _storageArchived = archived;
            _storage.ShowProjects(projects, archived, ProjectNames(projects),
                UserPathText.Of(Path.GetFullPath(_options.ProjectsRoot), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            await RefreshChosenProjectAsync();
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"storage not read: {ex.GetType().Name}");
            _storage.SetResult($"The storage cannot be read: {ex.Message}");
        }
    }

    private async Task ShowProjectAsync(string folder)
    {
        _storageFolder = folder;
        try
        {
            var files = await Task.Run(() => StorageReport.FilesOf(folder));
            _storage.ShowTranscripts(StorageNameOf(folder), files);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"storage project not read: {ex.GetType().Name}");
            _storage.SetResult($"The transcripts of {StorageNameOf(folder)} cannot be read: {ex.Message}");
        }
    }

    /// <summary>
    /// The last listing the Storage page drew, so its names can be drawn again when the history is read (P9). The listing can be read before the
    /// history, and the names need the history's working folders; without this the page keeps Claude's names until the next Refresh.
    /// </summary>
    private IReadOnlyList<ProjectStorage>? _storageProjects;
    private IReadOnlyList<TranscriptFileInfo>? _storageArchived;

    /// <summary>Draws the last listing again with the names the history gives now. Nothing is read from disk for the list itself.</summary>
    private void RedrawStorageNames()
    {
        if (_storageProjects is not { } projects || _storageArchived is not { } archived) return;
        _storage.ShowProjects(projects, archived, ProjectNames(projects));
        _ = RefreshChosenProjectAsync();
    }

    /// <summary>The name each project folder is shown by on the Storage page, by its folder (P9).</summary>
    private Dictionary<string, string> ProjectNames(IEnumerable<ProjectStorage> projects) =>
        projects.ToDictionary(project => project.Folder, project => StorageNameOf(project.Folder), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name a project folder has on the Storage page (P9): the leaf of the working folder its transcripts name, read from the history the rail
    /// has read. Before the history is read, or when no transcript of the folder names one, it is Claude's own name. The folder is still found by its key.
    /// </summary>
    private string StorageNameOf(string folder)
    {
        var key = Path.GetFileName(folder);
        var history = _history ?? new List<SessionDigest>();
        var cwds = history.Where(digest => string.Equals(digest.ProjectFolder, key, StringComparison.OrdinalIgnoreCase)).Select(digest => digest.Cwd);
        return FolderDisplay.Of(key, cwds).Name;
    }

    /// <summary>
    /// The middle list shows the chosen project again. After an archive or a restore it is read again too, so a transcript that moved
    /// leaves the list, and a checkbox does not point at a file that is gone (round 5 item 11).
    /// </summary>
    private async Task RefreshChosenProjectAsync()
    {
        if (_storageFolder is { } folder) await ShowProjectAsync(folder);
    }

    /// <summary>
    /// Moves the checked transcripts to the archive, after the owner confirms. The protected sessions are the ones claude lists (all of
    /// them, completed too) and the ones the state file records; the archive refuses those.
    /// </summary>
    private async Task ArchiveAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            _storage.SetResult("Nothing is checked.");
            return;
        }

        // The protected sessions come from the listing and the state file. Without either, the archive waits (round 5 item 4).
        var (records, recordsProblem) = LoadRecords(_store);
        if (!_listingKnown || recordsProblem is not null || _storeProblem is not null)
        {
            _storage.SetResult("Nothing is archived yet: claude's session list and the state file must both be read first.");
            return;
        }

        var answer = MessageBox.Show(this,
            $"Move {paths.Count} transcript(s) to the archive? They leave History and search. Restore brings them back.\n\n" +
            string.Join("\n", paths.Select(p => Path.GetFileName(p))),
            "Archive transcripts", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var protectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in _allAgents)
            if (agent.SessionId is { Length: > 0 } listed) protectedIds.Add(listed);
        foreach (var record in records.Values)
            if (record.SessionId.Length > 0) protectedIds.Add(record.SessionId);

        var outcomes = await Task.Run(() => TranscriptArchive.Archive(
            paths, _options.ProjectsRoot, _options.ArchiveRoot, protectedIds));
        _storage.SetResult(Summary(outcomes));
        if (outcomes.Any(o => o.Moved)) _ = LoadHistoryAsync();
        // The refresh draws the chosen project again too, so a transcript that moved leaves the list and the marks follow (round 8 item 11).
        await RefreshStorageAsync();
    }

    /// <summary>Moves the checked archived transcripts back to where they were. Restoring is reversible, so it is not asked about.</summary>
    private async Task RestoreAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            _storage.SetResult("Nothing is checked.");
            return;
        }

        var outcomes = await Task.Run(() => TranscriptArchive.Restore(paths, _options.ProjectsRoot, _options.ArchiveRoot));
        _storage.SetResult(Summary(outcomes));
        if (outcomes.Any(o => o.Moved)) _ = LoadHistoryAsync();
        // The refresh draws the chosen project again too, so a transcript that moved leaves the list and the marks follow (round 8 item 11).
        await RefreshStorageAsync();
    }

    private static string Summary(IReadOnlyList<ArchiveOutcome> outcomes)
    {
        var moved = outcomes.Count(o => o.Moved);
        var refused = outcomes.Where(o => !o.Moved).Select(o => $"{Path.GetFileName(o.Path)}: {o.Message}").ToList();
        var line = $"{moved} moved.";
        return refused.Count == 0 ? line : line + " Not moved: " + string.Join("  ", refused);
    }
}

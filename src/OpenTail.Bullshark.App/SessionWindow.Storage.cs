using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Storage;

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
        Pane.Children.Add(_storage);
        _storage.RefreshRequested += () => _ = RefreshStorageAsync();
        _storage.ProjectChosen += folder => _ = ShowProjectAsync(folder);
        _storage.ArchiveRequested += paths => _ = ArchiveAsync(paths);
        _storage.RestoreRequested += paths => _ = RestoreAsync(paths);
    }

    private void StorageButton_Click(object sender, RoutedEventArgs e)
    {
        SetActive(TabFor(TabKind.Storage, "storage", () => "Storage"));
        ShowOnly(_storage);
        _ = RefreshStorageAsync();
    }

    /// <summary>Reads the project sizes and the archive, then draws them.</summary>
    private async Task RefreshStorageAsync()
    {
        var (projects, archived) = await Task.Run(() =>
            (StorageReport.Read(_options.ProjectsRoot), TranscriptArchive.List(_options.ArchiveRoot)));
        _storage.ShowProjects(projects, archived);
    }

    private async Task ShowProjectAsync(string folder)
    {
        var files = await Task.Run(() => StorageReport.FilesOf(folder));
        _storage.ShowTranscripts(Path.GetFileName(folder), files);
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

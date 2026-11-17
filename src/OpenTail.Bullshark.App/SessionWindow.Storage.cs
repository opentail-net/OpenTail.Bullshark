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
            (StorageReport.Read(TranscriptReader.DefaultProjectsRoot), TranscriptArchive.List(TranscriptArchive.DefaultRoot)));
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

        var answer = MessageBox.Show(this,
            $"Move {paths.Count} transcript(s) to the archive? They leave History and search. Restore brings them back.\n\n" +
            string.Join("\n", paths.Select(p => Path.GetFileName(p))),
            "Archive transcripts", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var protectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in _allAgents)
            if (agent.SessionId is { Length: > 0 } listed) protectedIds.Add(listed);
        foreach (var record in ReadRecords().Values)
            if (record.SessionId.Length > 0) protectedIds.Add(record.SessionId);

        var outcomes = await Task.Run(() => TranscriptArchive.Archive(
            paths, TranscriptReader.DefaultProjectsRoot, TranscriptArchive.DefaultRoot, protectedIds));
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

        var outcomes = await Task.Run(() => TranscriptArchive.Restore(paths, TranscriptReader.DefaultProjectsRoot, TranscriptArchive.DefaultRoot));
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

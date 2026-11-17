using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Changes tab of a live session (item 2 of the additions plan). Opened from the session's rail menu. The folder is the
/// session's working folder from the listing, else its spec's. Everything is read on a pool thread; nothing is written.
/// </summary>
public partial class SessionWindow
{
    private readonly ChangesView _changes = new() { Visibility = Visibility.Hidden };

    /// <summary>The session the Changes tab shows. A read that finishes for another session is dropped.</summary>
    private string? _changesSession;

    private void InitChanges()
    {
        Pane.Children.Add(_changes);
        _changes.RefreshRequested += () =>
        {
            if (_changesSession is { } sessionId) _ = ShowChangesAsync(sessionId);
        };
    }

    /// <summary>Opens (or shows) the Changes tab of a session and reads its changes.</summary>
    private void OpenChanges(string sessionId, string title)
    {
        var tab = TabFor(TabKind.Changes, sessionId, () => "Changes: " + title);
        SetActive(tab);
        _ = ShowChangesAsync(sessionId);
    }

    private async Task ShowChangesAsync(string sessionId)
    {
        _changesSession = sessionId;
        ShowOnly(_changes);

        var record = ReadRecords().Values.FirstOrDefault(r => string.Equals(r.SessionId, sessionId, StringComparison.Ordinal));
        var listed = _agents.FirstOrDefault(a => string.Equals(a.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))?.Cwd;
        var spec = record is null ? null : Specs.FirstOrDefault(s => string.Equals(s.Name, record.Name, StringComparison.Ordinal))?.WorkingDirectory;
        // The transcript is read first, on a pool thread. Its latest folder is the one the session edits now, which can be a worktree,
        // so it is preferred to the listing's folder (round 7 item 4). Its files are shown as before.
        var facts = await Task.Run(() => TranscriptWrites.Facts(_options.ProjectsRoot, sessionId));
        if (_changesSession != sessionId) return;
        var folder = GitChanges.FolderOf(facts.Folder, listed, spec);
        if (folder is null)
        {
            _changes.ShowMessage("Changes", "No folder is known for this session yet. It appears once the session is listed.");
            return;
        }

        _changes.ShowLoading(folder);
        try
        {
            var changes = await Task.Run(() => GitChanges.ReadAsync(folder, new ProcessGitRunner()));
            if (_changesSession != sessionId) return;
            _changes.Show(changes, facts.Files, facts.Note, SharedLinesFor(record));
            _changes.ResolvePrompt = ResolvePromptFor(record);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            // A folder that cannot be read used to leave the tab on "Reading the changes…" with Refresh off. It is said instead (round 5 item 11).
            AppLog.Write($"changes not read: {ex.Message}");
            if (_changesSession == sessionId) _changes.ShowMessage("Changes", $"The changes cannot be read: {ex.Message}");
        }
    }
}

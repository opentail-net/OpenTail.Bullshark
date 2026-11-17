using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Git;

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
        var folder = GitChanges.FolderOf(listed, spec);
        if (folder is null)
        {
            _changes.ShowMessage("Changes", "No folder is known for this session yet. It appears once the session is listed.");
            return;
        }

        _changes.ShowLoading(folder);
        var (report, written, writtenNote) = await Task.Run(async () =>
        {
            var changes = await GitChanges.ReadAsync(folder, new ProcessGitRunner());
            var (files, note) = WrittenFiles(sessionId);
            return (changes, files, note);
        });
        if (_changesSession != sessionId) return;
        _changes.Show(report, written, writtenNote, SharedLinesFor(record));
    }

    /// <summary>
    /// The files the session's transcript says it wrote (Write, Edit, MultiEdit, NotebookEdit). Reads the whole transcript once,
    /// so it runs on a pool thread. A missing transcript is a note, not an error.
    /// </summary>
    private static (IReadOnlyList<string>? Files, string? Note) WrittenFiles(string sessionId)
    {
        try
        {
            var path = TranscriptReader.FindTranscript(TranscriptReader.DefaultProjectsRoot, sessionId);
            if (path is null) return (null, "No transcript for this session yet.");
            return (TranscriptReader.ReadFile(path).FilesWritten ?? [], null);
        }
        catch (IOException ex)
        {
            AppLog.Write($"changes: transcript not read: {ex.Message}");
            return (null, $"The transcript cannot be read: {ex.Message}");
        }
    }
}

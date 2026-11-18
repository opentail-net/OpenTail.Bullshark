using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Changes tab of a live session (item 2 of the additions plan). Opened from the session's rail menu. The folder is the
/// session's working folder from the listing, else its spec's. Everything is read on a pool thread. The only write is the viewed ticks
/// (features batch G1), which go to ui.json and never to the folder or to git.
/// </summary>
public partial class SessionWindow
{
    private readonly ChangesView _changes = new() { Visibility = Visibility.Hidden };

    /// <summary>The session the Changes tab shows. A read that finishes for another session is dropped.</summary>
    private string? _changesSession;

    /// <summary>
    /// The viewed ticks of every session, as ui.json holds them (features batch G1). Read with the Changes tab, replaced on each save. UI thread only.
    /// </summary>
    private IReadOnlyDictionary<string, ViewedSession> _viewedMarks = new Dictionary<string, ViewedSession>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The stamp each changed file of the tab on screen had when it was read (null when it could not be read). UI thread only.</summary>
    private IReadOnlyDictionary<string, string?> _changesStamps = new Dictionary<string, string?>(StringComparer.Ordinal);

    private void InitChanges()
    {
        Pane.Children.Add(_changes);
        _changes.RefreshRequested += () =>
        {
            if (_changesSession is { } sessionId) _ = ShowChangesAsync(sessionId);
        };
        _changes.ViewedChanged += OnViewedChanged;
        _changes.ViewedAllChanged += OnViewedAllChanged;
    }

    /// <summary>Opens (or shows) the Changes tab of a session and reads its changes.</summary>
    private void OpenChanges(string sessionId, string title)
    {
        var tab = TabFor(TabKind.Changes, sessionId, () => "Changes: " + title);
        SetActive(tab);
        // The owner looked at the changes, so the session leaves the Ready for review lane (ready-for-review D2). The tab is active first, so the
        // rail's redraw does not move the pane away from it.
        MarkReviewed(sessionId);
        _ = ShowChangesAsync(sessionId);
    }

    private async Task ShowChangesAsync(string sessionId)
    {
        _changesSession = sessionId;
        ShowOnly(_changes);

        try
        {
            var record = ReadRecords().Values.FirstOrDefault(r => SameId(r.SessionId, sessionId));
            _changes.SessionName = record?.Name ?? string.Empty;
            var listed = _agents.FirstOrDefault(a => string.Equals(a.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))?.Cwd;
            var spec = record is null ? null : Specs.FirstOrDefault(s => string.Equals(s.Name, record.Name, StringComparison.Ordinal))?.WorkingDirectory;
            // The transcript is read first, on a pool thread. Its latest folder is the one the session edits now, which can be a worktree,
            // so it is preferred to the listing's folder (round 7 item 4). Its files are shown as before. The writes come from the cache, off the
            // UI thread (round 12 B3); the path is copied here, as the window's dictionary is read on the UI thread only.
            var known = _transcriptPaths.TryGetValue(sessionId, out var knownPath) ? knownPath : null;
            var projectsRoot = _options.ProjectsRoot;
            var facts = await Task.Run(() => TranscriptFactsOf(sessionId, known, projectsRoot));
            if (!SameId(_changesSession, sessionId)) return;
            var folder = GitChanges.FolderOf(facts.Folder, listed, spec);
            if (folder is null)
            {
                _changes.ShowMessage("Changes", "No folder is known for this session yet. It appears once the session is listed.");
                return;
            }

            _changes.ShowLoading(folder);
            var changes = await Task.Run(() => GitChanges.ReadAsync(folder, new ProcessGitRunner()));
            if (!SameId(_changesSession, sessionId)) return;

            // The stamp of each changed file and the viewed ticks are read on a pool thread too (features batch G1). The state file's path is
            // copied here: the window's options are read on the UI thread.
            var statePath = _options.StatePath;
            var (stamps, marks) = await Task.Run(() => ViewedStateOf(statePath, folder, changes));
            if (!SameId(_changesSession, sessionId)) return;
            _changesStamps = stamps;
            _viewedMarks = ViewedFiles.Prune(marks, DateTimeOffset.UtcNow);
            // A complete list drops the ticks of the files that are no longer changed. A list cut at its limit keeps them all.
            if (changes.Kind == GitChangesKind.Changes && !changes.FilesTruncated)
                _viewedMarks = ViewedFiles.DropUnchanged(_viewedMarks, sessionId, changes.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal));

            var viewed = new ViewedTicks(stamps, ViewedFiles.ViewedAmong(_viewedMarks, sessionId, stamps));
            _changes.Show(changes, facts.Files, facts.Note, SharedLinesFor(record), viewed);
            _changes.ResolvePrompt = ResolvePromptFor(record);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A read that fails is said in the tab, not left on its old view (round 12 B2). A folder that cannot be read is said so too
            // (round 5 item 11).
            AppLog.Write($"changes not read: {ex.GetType().Name}: {ex.Message}");
            if (SameId(_changesSession, sessionId)) _changes.ShowMessage("Changes", $"The changes cannot be read: {ex.Message}");
        }
    }

    /// <summary>
    /// The stamp of each changed file now (only for a list of changes), and the viewed ticks as the state file holds them. Runs on a pool
    /// thread: it reads the files' metadata and the state file, and nothing is written.
    /// </summary>
    private static (IReadOnlyDictionary<string, string?> Stamps, IReadOnlyDictionary<string, ViewedSession> Marks) ViewedStateOf(
        string statePath, string folder, GitChangesReport report)
    {
        var stamps = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (report.Kind == GitChangesKind.Changes)
            foreach (var file in report.Files) stamps[file.Path] = ViewedFiles.CurrentStamp(folder, file.Path);
        var marks = ViewedFiles.Parse(UiFile.Read(statePath, ViewedFiles.Key));
        return (stamps, marks);
    }

    /// <summary>A file's "viewed" box was clicked. Its tick is kept with the file's stamp now, so it clears when the file changes (features batch G1).</summary>
    private void OnViewedChanged(string path, bool viewed)
    {
        if (_changesSession is not { } sessionId) return;
        if (!_changesStamps.TryGetValue(path, out var stamp) || stamp is null) return;
        var now = DateTimeOffset.UtcNow;
        var marks = viewed
            ? ViewedFiles.Mark(_viewedMarks, sessionId, path, stamp, now)
            : ViewedFiles.Unmark(_viewedMarks, sessionId, path);
        SaveViewed(sessionId, marks, now);
    }

    /// <summary>"Mark all viewed" ticks every changed file of the tab with its stamp now; "Clear" removes the session's ticks (features batch G1).</summary>
    private void OnViewedAllChanged(bool viewed)
    {
        if (_changesSession is not { } sessionId) return;
        var now = DateTimeOffset.UtcNow;
        var marks = viewed
            ? ViewedFiles.MarkAll(_viewedMarks, sessionId, _changesStamps, now)
            : ViewedFiles.Clear(_viewedMarks, sessionId);
        SaveViewed(sessionId, marks, now);
    }

    /// <summary>
    /// Keeps the ticks in memory, writes them to ui.json and draws the tab's ticks again. A write that fails is said on the status line: the ticks
    /// still show until the window closes, and a later read of the tab shows what the file holds.
    /// </summary>
    private void SaveViewed(string sessionId, IReadOnlyDictionary<string, ViewedSession> marks, DateTimeOffset now)
    {
        _viewedMarks = ViewedFiles.Prune(marks, now);
        if (!UiFile.Set(_options.StatePath, ViewedFiles.Key, ViewedFiles.ToJson(_viewedMarks)))
            SetStatus("The viewed ticks are not saved: ui.json cannot be written. They show until the window closes.");
        _changes.SetViewed(ViewedFiles.ViewedAmong(_viewedMarks, sessionId, _changesStamps));
    }
}

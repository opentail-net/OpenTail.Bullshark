using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The merge check of each coder (gems item 2). A coder that enters `done` is checked once for that entry: the check runs on a pool
/// thread, and its result comes back on the UI thread and shows on the next pass. A conflict is a "Needs you" notice, and the Changes
/// tab copies a resolve prompt. Nothing is sent to the session.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The merge check result of each coder, by session id. Written on the UI thread only.</summary>
    private readonly Dictionary<string, MergeCheckReport> _merges = new(StringComparer.Ordinal);

    /// <summary>
    /// The coders whose check has started for their current entry into `done`. A coder that leaves `done` is taken out, so its next
    /// entry is checked again and the old result goes.
    /// </summary>
    private readonly HashSet<string> _mergeStarted = new(StringComparer.Ordinal);

    /// <summary>Starts a check for each coder that has just entered `done`, and forgets the result of one that has left it.</summary>
    private void UpdateMergeChecks(IReadOnlyDictionary<string, SessionRecord> records)
    {
        var stillDone = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records.Values.Where(r => r.Role == SessionRole.Coder && r.SessionId.Length > 0))
        {
            var state = _allAgents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase))?.EffectiveState;
            if (!string.Equals(state, "done", StringComparison.OrdinalIgnoreCase)) continue;

            stillDone.Add(record.SessionId);
            if (_mergeStarted.Add(record.SessionId)) _ = CheckMergeAsync(record);
        }

        foreach (var id in _mergeStarted.Where(id => !stillDone.Contains(id)).ToList())
        {
            _mergeStarted.Remove(id);
            _merges.Remove(id);
        }
    }

    /// <summary>
    /// The check for one coder, on a pool thread. A coder with no known folder is not checked yet, and is tried again on the next pass.
    /// A result that arrives after the coder has left `done` is dropped.
    /// </summary>
    private async Task CheckMergeAsync(SessionRecord record)
    {
        try
        {
            var listed = _allAgents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase))?.Cwd;
            var spec = Specs.FirstOrDefault(s => string.Equals(s.Name, record.Name, StringComparison.Ordinal))?.WorkingDirectory;
            // The folder the coder edits now, from its transcript, is checked ahead of the listing's (round 7 item 4).
            var transcriptFolder = await Task.Run(() => TranscriptWrites.Facts(_options.ProjectsRoot, record.SessionId).Folder);
            var folder = GitChanges.FolderOf(transcriptFolder, listed, spec);
            if (folder is null)
            {
                _mergeStarted.Remove(record.SessionId);
                return;
            }

            var report = await Task.Run(() => MergeCheck.RunAsync(folder, new ProcessGitRunner()));
            if (!_mergeStarted.Contains(record.SessionId)) return;
            _merges[record.SessionId] = report;
            // A problem is not a verdict: the coder leaves the started set, so the next pass checks it again while it stays done (round 8 item 6).
            if (MergeCheck.ShouldRecheck(report)) _mergeStarted.Remove(record.SessionId);
            AppLog.Write($"merge check {record.Name}: {report.Kind} {report.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The reads are inside the try (round 12 B2): a failure here was lost, since the task is not awaited. It is logged, and the
            // coder leaves the started set, so the next pass checks it again.
            _mergeStarted.Remove(record.SessionId);
            AppLog.Write($"merge check {record.Name} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The coders whose latest check found conflicts, for the attention list.</summary>
    private IReadOnlyList<SessionConflict> ConflictsOf(IEnumerable<SessionRecord> records) =>
        records
            .Where(r => _merges.TryGetValue(r.SessionId, out var report) && report.Kind == MergeCheckKind.Conflicts)
            .Select(r => new SessionConflict(r.Name, _merges[r.SessionId].Files, _merges[r.SessionId].Target))
            .ToList();

    /// <summary>The resolve prompt for a session whose check found conflicts, or null. It is copied, never sent.</summary>
    private string? ResolvePromptFor(SessionRecord? record)
    {
        if (record is null || !_merges.TryGetValue(record.SessionId, out var report) || report.Kind != MergeCheckKind.Conflicts) return null;
        var target = MergeCheck.DisplayName(report.Target);
        var files = string.Join(Environment.NewLine, report.Files.Select(file => "- " + file));
        return
            $"Your committed work conflicts with {target} in these files:{Environment.NewLine}{files}{Environment.NewLine}{Environment.NewLine}" +
            $"Merge {target} into this branch, resolve each conflict so that both sides' intent is kept, run the tests, and commit the result. " +
            "Do not push. Change only the files in the conflict.";
    }

    /// <summary>True when the session's latest check found conflicts, for the rail mark.</summary>
    private bool HasConflict(string sessionId) =>
        _merges.TryGetValue(sessionId, out var report) && report.Kind == MergeCheckKind.Conflicts;
}

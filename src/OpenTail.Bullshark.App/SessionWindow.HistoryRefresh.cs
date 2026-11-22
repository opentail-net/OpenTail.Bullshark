using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The periodic refresh of the session list (R9). Every <c>index.refreshSeconds</c>, checked on the pass timer, the history is brought up to date
/// from the memo of the last read: only the folders whose stamp changed, and the live transcripts, are read again. The rail is drawn again only
/// when the list it shows changed (<see cref="SessionListSignature"/>). The full read (<c>LoadHistoryAsync</c>) is unchanged, and it is what the
/// window does on open, on 🔄, after a Storage move and on return from Away. UI thread only, apart from the reads, which run on the thread pool.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The memo of the last full or scoped read, which the next scoped refresh reuses. Null until the first full read has finished.</summary>
    private TranscriptIndexMemo? _historyMemo;

    /// <summary>
    /// True while a scoped refresh runs. It is not the loading flag: the rail's signature includes that flag, so a refresh that may not redraw
    /// must not set it.
    /// </summary>
    private bool _historyRefreshing;

    /// <summary>A full read was asked for while a scoped refresh ran. It starts once the refresh has finished (one follow-up, as concept note B says).</summary>
    private bool _historyFullPending;

    /// <summary>When the last read finished, full or scoped (a failed read counts). The periodic refresh waits <c>index.refreshSeconds</c> from it.</summary>
    private DateTimeOffset _historyReadAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Starts a scoped refresh when it is due: <c>index.refreshSeconds</c> is above zero, the history has been read once, no read is running, and
    /// the last read finished that many seconds ago or more. It is called on each pass tick (every 15 s), so a refresh runs within one tick of
    /// being due. It does nothing once the window is closing.
    /// </summary>
    private void MaybeRefreshHistory()
    {
        var seconds = _behaviour.Index.RefreshSeconds;
        if (seconds <= 0 || _windowClosing || _history is null || _historyMemo is null) return;
        if (_historyLoading || _historyRefreshing) return;
        if (DateTimeOffset.UtcNow < _historyReadAt.AddSeconds(seconds)) return;
        _ = RefreshHistoryAsync();
    }

    /// <summary>
    /// The scoped read, off the UI thread. The new list replaces the old one at once, so a later redraw shows it; the rail and the Storage names
    /// are drawn again only when the list changed. A failure keeps the last good list and logs its exception type only. A full read that was
    /// asked for meanwhile starts after this one.
    /// </summary>
    private async Task RefreshHistoryAsync()
    {
        var memo = _historyMemo;
        if (memo is null) return;
        _historyRefreshing = true;
        try
        {
            // The list before this refresh is not changed by anything (a new list replaces it), so it can be read on the pool thread too.
            var previous = _history ?? [];
            var refresh = await Task.Run(() => ActiveClaude.RefreshIndex(memo));
            var sessions = refresh.Index.Sessions;
            // Only the transcripts that were read again have their tails read again. The others keep the facts they had (see FactsAfterRefresh).
            var reread = sessions.Where(session => refresh.ReadFiles.Contains(session.FilePath)).ToList();
            var fresh = await Task.Run(() => ReadHistoryFacts(reread));
            var facts = FactsAfterRefresh(sessions, refresh.ReadFiles, fresh);
            var changed = await Task.Run(() => SessionListSignature.Of(previous) != SessionListSignature.Of(sessions));
            _history = sessions.ToList();
            _historyFacts = facts;
            _historyMemo = refresh.Memo;
            AppLog.Write($"history refresh: {refresh.FoldersReused} folders reused, {refresh.FoldersRead} folders read " +
                         $"({refresh.ReadFiles.Count} files), changed {(changed ? "yes" : "no")}");
            if (changed)
            {
                _historyVersion++;
                RefreshRail();
                RedrawStorageNames();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"history refresh failed: {ex.GetType().Name}; the last list is kept");
        }
        finally
        {
            _historyRefreshing = false;
            _historyReadAt = DateTimeOffset.UtcNow;
            if (_historyFullPending)
            {
                _historyFullPending = false;
                _ = LoadHistoryAsync();
            }
        }
    }

    /// <summary>
    /// The cache-label facts after a scoped refresh: the fresh ones for the transcripts that were read again, and the previous ones for the rest.
    /// A session with no fresh facts gets none, as a full read gives it. UI thread only (it reads the window's facts).
    /// </summary>
    private Dictionary<string, TailFacts> FactsAfterRefresh(
        IReadOnlyList<SessionDigest> sessions,
        IReadOnlySet<string> readFiles,
        Dictionary<string, TailFacts> fresh)
    {
        var facts = new Dictionary<string, TailFacts>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (readFiles.Contains(session.FilePath))
            {
                if (fresh.TryGetValue(session.SessionId, out var tail)) facts.TryAdd(session.SessionId, tail);
            }
            else if (_historyFacts.TryGetValue(session.SessionId, out var kept))
            {
                facts.TryAdd(session.SessionId, kept);
            }
        }
        return facts;
    }
}

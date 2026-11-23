using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The message texts of one provider's sessions, kept for the message-text search (PP-D4, 2026-11-23). A session's texts are kept while its activity time
/// is the same as when they were read; when the activity moves on, they are read again. Only the window's UI thread uses it, so it needs no lock. A
/// read that fails keeps an empty list for that activity time, so it is not retried until the session changes; the log names the exception type only.
/// </summary>
internal sealed class MessageTextCache
{
    private readonly Dictionary<string, (DateTimeOffset? Stamp, IReadOnlyList<string> Texts)> _entries = new(StringComparer.Ordinal);

    /// <summary>The texts that are cached and current, by session id. A session with no current texts is left out.</summary>
    public Dictionary<string, IReadOnlyList<string>> Known(IEnumerable<SessionSummary> sessions)
    {
        var known = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (Current(session) is { } texts) known[session.Key.SessionId] = texts;
        }
        return known;
    }

    /// <summary>Reads the texts of every session that is not current, and returns the texts of all of them, by session id.</summary>
    public async Task<Dictionary<string, IReadOnlyList<string>>> LoadAllAsync(
        IEnumerable<SessionSummary> sessions, IMessageTextSource source, CancellationToken ct)
    {
        var all = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            all[session.Key.SessionId] = await LoadAsync(session, source, ct);
        }
        return all;
    }

    /// <summary>The cached texts of a session when they are current (same activity time), else null.</summary>
    private IReadOnlyList<string>? Current(SessionSummary session) =>
        _entries.TryGetValue(session.Key.ToString(), out var entry) && entry.Stamp == session.LastActivity ? entry.Texts : null;

    private async Task<IReadOnlyList<string>> LoadAsync(SessionSummary session, IMessageTextSource source, CancellationToken ct)
    {
        if (Current(session) is { } cached) return cached;
        IReadOnlyList<string> texts;
        try
        {
            texts = await source.MessageTextsAsync(session.Key.SessionId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            AppLog.Write($"message search: a session could not be read ({e.GetType().Name})");
            texts = [];
        }
        _entries[session.Key.ToString()] = (session.LastActivity, texts);
        return texts;
    }
}

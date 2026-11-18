namespace OpenTail.Bullshark.Core.Cache;

/// <summary>
/// Sees a session's cache go cold while the window is open (tags plan, Part C). It remembers the last known state of each session; each
/// <see cref="Update"/> returns the sessions whose state moved from Warm or Cooling to Cold since the previous update. The first state of a
/// session only records it, so a session that was already cold when the watch first looked is not returned. An Unknown state tells nothing:
/// it is ignored and the remembered state stays. A session that is not in an update is forgotten, so it is recorded afresh if it comes back.
/// Pure: the caller passes the states, and keeps one watch for the window's life.
/// </summary>
public sealed class CacheWatch
{
    private readonly Dictionary<string, CacheState> _last = new(StringComparer.Ordinal);

    /// <summary>The sessions that went from Warm or Cooling to Cold, in the order given. Every session in <paramref name="states"/> is kept.</summary>
    public IReadOnlyList<string> Update(IEnumerable<(string SessionId, CacheState State)> states)
    {
        var wentCold = new List<string>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (sessionId, state) in states)
        {
            present.Add(sessionId);
            if (state == CacheState.Unknown) continue;
            if (state == CacheState.Cold && _last.TryGetValue(sessionId, out var before) && before is (CacheState.Warm or CacheState.Cooling))
                wentCold.Add(sessionId);
            _last[sessionId] = state;
        }
        foreach (var gone in _last.Keys.Where(id => !present.Contains(id)).ToList()) _last.Remove(gone);
        return wentCold;
    }
}

namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>
/// The memory of failed lookups (behaviour batch, R10). A session id whose transcript was not found is not searched for again while the miss is
/// within the window (<c>lookups.missMemorySeconds</c>), so a pass does not search the folders for the same missing file each time. Only a miss is
/// kept: a found file is never remembered, and <see cref="Forget"/> drops a miss once the file is found. A window of zero or less remembers nothing.
/// The time and the window are given by the caller, so nothing here reads a clock. Safe to use from several threads: the lookups run on pool threads,
/// and the window clears the memory on the UI thread.
/// </summary>
public sealed class LookupMemory
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _misses = new(StringComparer.Ordinal);

    /// <summary>Remembers a miss for <paramref name="key"/> found at <paramref name="now"/>. Nothing is kept when <paramref name="window"/> is zero or less.</summary>
    public void Remember(string key, DateTimeOffset now, TimeSpan window)
    {
        if (window <= TimeSpan.Zero) return;
        lock (_gate) _misses[key] = now;
    }

    /// <summary>
    /// True when a miss for <paramref name="key"/> was remembered less than <paramref name="window"/> before <paramref name="now"/>. A miss that is out of its
    /// window is dropped and reads as no miss. A miss that is later than <paramref name="now"/> (the clock went back) is dropped too, so it cannot hide a file
    /// for longer than the window. A window of zero or less is never remembered.
    /// </summary>
    public bool IsRemembered(string key, DateTimeOffset now, TimeSpan window)
    {
        lock (_gate)
        {
            if (window <= TimeSpan.Zero)
            {
                _misses.Remove(key);
                return false;
            }
            if (!_misses.TryGetValue(key, out var missedAt)) return false;
            var age = now - missedAt;
            if (age >= TimeSpan.Zero && age < window) return true;
            _misses.Remove(key);
            return false;
        }
    }

    /// <summary>Drops the miss for <paramref name="key"/>, if there is one. Called when the lookup finds the file.</summary>
    public void Forget(string key)
    {
        lock (_gate) _misses.Remove(key);
    }

    /// <summary>Drops every miss. Called when the session the pane shows changes, so no miss is carried to what is shown next.</summary>
    public void Clear()
    {
        lock (_gate) _misses.Clear();
    }
}

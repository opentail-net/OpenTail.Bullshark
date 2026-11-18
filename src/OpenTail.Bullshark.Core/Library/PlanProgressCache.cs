namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// The plan counts kept in memory, by plan path, so a rail that is drawn again on every pass does not read the files again. An entry is valid
/// while the file keeps its last write time and length; a file that changed is read again. A missing or unreadable file gives null, and that
/// is kept too, until the file changes. Used on the UI thread only.
/// </summary>
public sealed class PlanProgressCache
{
    private sealed record Entry(DateTime WrittenUtc, long Length, PlanProgressCount? Count);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The counts of the plan at <paramref name="path"/>, or null when the file is missing or cannot be read.</summary>
    public PlanProgressCount? Get(string path)
    {
        var stamp = Stamp(path);
        if (stamp is null)
        {
            _entries.Remove(path);
            return null;
        }

        var (written, length) = stamp.Value;
        if (_entries.TryGetValue(path, out var known) && known.WrittenUtc == written && known.Length == length) return known.Count;

        var count = PlanProgress.ReadFile(path);
        _entries[path] = new Entry(written, length, count);
        return count;
    }

    /// <summary>The last write time and the length of a file, or null when it is missing or cannot be looked at.</summary>
    private static (DateTime WrittenUtc, long Length)? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            return (info.LastWriteTimeUtc, info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

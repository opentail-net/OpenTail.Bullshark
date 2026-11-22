namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The memo of the Codex rollouts read for the usage report (R8 part 2): the record each rollout gave the last time it was read, so that a
/// finished rollout is not read again on each refresh. One memo per window, owned by the caller and never static; the Limits page and the Away
/// digest share the one the window gives them. It holds records and stamps only: no line text is kept.
/// <para>
/// <see cref="CodexUsageReader.ReadFolder"/> reuses an entry only when the file's length, its last write time and the <c>reasoningAsOutput</c>
/// setting are the ones the record was read with, and the file's last write time is more than <see cref="LiveWindow"/> before the read. So a
/// rollout still being written is always read again, and a changed setting is never answered from the old figures. A file that fails to read
/// has no entry. The memo does no file I/O, and a lock guards it, so the two readers may share it from different threads.
/// </para>
/// </summary>
public sealed class CodexUsageMemo
{
    /// <summary>A rollout last written less than this long before a read is live: it is read again, whatever its stamp.</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>The number of rollouts the memo holds a record for (zero for a file with no valid total, too).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// The record kept for <paramref name="path"/> when its stamp and setting match and it is not live (its write time is more than
    /// <see cref="LiveWindow"/> before <paramref name="nowUtc"/>). The record may be null: a file with no valid total.
    /// </summary>
    internal bool TryGet(string path, long length, long writtenTicks, bool reasoningAsOutput, DateTime nowUtc, out CodexSessionUsage? usage)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var entry)
                && entry.Length == length
                && entry.WrittenTicks == writtenTicks
                && entry.ReasoningAsOutput == reasoningAsOutput
                && nowUtc - new DateTime(writtenTicks, DateTimeKind.Utc) > LiveWindow)
            {
                usage = entry.Usage;
                return true;
            }
        }
        usage = null;
        return false;
    }

    /// <summary>Keeps the record of a file read now, with the stamp and setting it was read with.</summary>
    internal void Put(string path, long length, long writtenTicks, bool reasoningAsOutput, CodexSessionUsage? usage)
    {
        lock (_gate)
        {
            _entries[path] = new Entry(length, writtenTicks, reasoningAsOutput, usage);
        }
    }

    /// <summary>Removes the entry of a file that failed to read, so that the next read of it is a fresh one.</summary>
    internal void Forget(string path)
    {
        lock (_gate)
        {
            _entries.Remove(path);
        }
    }

    /// <summary>Removes the entries of the files that are not in <paramref name="paths"/>: the rollouts that are gone.</summary>
    internal void Prune(IEnumerable<string> paths)
    {
        var keep = new HashSet<string>(paths, StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var path in _entries.Keys.Where(path => !keep.Contains(path)).ToList())
            {
                _entries.Remove(path);
            }
        }
    }

    /// <summary>One file's stamp, the setting it was read with, and its record (null for no valid total).</summary>
    private sealed record Entry(long Length, long WrittenTicks, bool ReasoningAsOutput, CodexSessionUsage? Usage);
}

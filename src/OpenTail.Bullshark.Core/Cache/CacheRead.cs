using System.Text.Json;

namespace OpenTail.Bullshark.Core.Cache;

/// <summary>
/// Reads a cache file without its lock (reliability batch R11). A cache is read without the file lock, so a read can meet the moment a writer
/// replaces the file: the read gets the old file or the new one, but an error can come from the switch itself. A read that fails that way is
/// tried once more after <see cref="RetryDelay"/>. A second failure is the caller's: it then treats the cache as not there, as before.
/// A file that is not there is not retried, and nor is any other error.
/// </summary>
public static class CacheRead
{
    /// <summary>How long to wait before the one retry.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Runs <paramref name="read"/>. When it fails with an I/O or a JSON error (a file caught in the middle of a replace), it waits and runs it once more.
    /// <paramref name="pause"/> is the wait, and is replaced in tests; by default it is a thread sleep.
    /// </summary>
    public static T Retry<T>(Func<T> read, Action<TimeSpan>? pause = null)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsReplaceError(ex))
        {
            (pause ?? (delay => Thread.Sleep(delay)))(RetryDelay);
            return read();
        }
    }

    /// <summary>True for an I/O or JSON error that a replace can cause. A missing file is not one: the caller checks for the file first.</summary>
    public static bool IsReplaceError(Exception ex) =>
        ex is IOException or JsonException
        && ex is not (FileNotFoundException or DirectoryNotFoundException);
}

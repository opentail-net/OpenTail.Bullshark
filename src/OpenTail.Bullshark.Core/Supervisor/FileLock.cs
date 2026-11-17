using System.Security.Cryptography;
using System.Text;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>A state file that cannot be read as the records it should hold. Nothing is written when this is thrown.</summary>
public sealed class StoreUnreadableException(string path, string reason)
    : Exception($"{path} cannot be read ({reason}); nothing was changed.");

/// <summary>
/// The lock and the atomic write behind runs.json, state.json, library.json, patterns.json and ui.json. The window is the process
/// that writes them; the headless host (OpenTail.Bullshark.Supervisor) only reads them, for its reports. Every change is still read,
/// changed and written under one named lock, so a second writer (another window) is safe. A write goes to a temporary file with its
/// own name, then moves over the file: a crash never leaves half a file, and two writers never share a temporary file.
/// </summary>
public static class FileLock
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>Takes the lock for a file, waiting up to ten seconds. Dispose the result to release it.</summary>
    public static IDisposable Hold(string path) => Hold(path, Wait);

    /// <summary>
    /// Takes the lock for a file, waiting up to <paramref name="wait"/>. The key is the full path, so two spellings of one file
    /// share one lock. On Windows file names do not depend on case, so the key ignores case there: without that, a path typed
    /// in another case would take a second lock on the same file. The key names the mutex, so the window and the headless host
    /// (which ship together) always agree on it.
    /// </summary>
    public static IDisposable Hold(string path, TimeSpan wait)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) full = full.ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
        var mutex = new Mutex(false, "Local\\OpenTail.Bullshark.file." + key);
        try
        {
            if (!mutex.WaitOne(wait))
            {
                mutex.Dispose();
                throw new TimeoutException($"{path} is held by another process for more than {wait.TotalSeconds:0.#} seconds.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The last holder ended without releasing. The lock is ours now, and each file is written whole, so nothing is lost.
        }
        return new Release(mutex);
    }

    /// <summary>
    /// Writes the text to a unique temporary file beside the path, then moves it over the path. If the write or the move fails,
    /// the temporary file is deleted and the error is rethrown: a failed write must not leave a file that looks like a half save.
    /// </summary>
    public static void WriteAtomic(string path, string text)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // The clean-up is best effort: the error that stopped the save is the one the owner needs, so a failed delete is ignored.
            try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private sealed class Release(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}

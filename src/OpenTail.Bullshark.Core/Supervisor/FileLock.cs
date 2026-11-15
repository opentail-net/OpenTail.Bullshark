using System.Security.Cryptography;
using System.Text;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>A state file that cannot be read as the records it should hold. Nothing is written when this is thrown.</summary>
public sealed class StoreUnreadableException(string path, string reason)
    : Exception($"{path} cannot be read ({reason}); nothing was changed.");

/// <summary>
/// The lock and the atomic write behind runs.json and state.json. The window and the supervisor are two processes that write the
/// same files, so every change is read, changed and written under one named lock. A write goes to a temporary file with its own
/// name, then moves over the file: a crash never leaves half a file, and two writers never share a temporary file.
/// </summary>
public static class FileLock
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>Takes the lock for a file. Dispose the result to release it.</summary>
    public static IDisposable Hold(string path)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path))))[..32];
        var mutex = new Mutex(false, "Local\\OpenTail.Bullshark.file." + key);
        try
        {
            if (!mutex.WaitOne(Wait))
            {
                mutex.Dispose();
                throw new TimeoutException($"{path} is held by another process for more than {Wait.TotalSeconds:0} seconds.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The last holder ended without releasing. The lock is ours now, and each file is written whole, so nothing is lost.
        }
        return new Release(mutex);
    }

    /// <summary>Writes the text to a unique temporary file beside the path, then moves it over the path.</summary>
    public static void WriteAtomic(string path, string text)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
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

using System.Security.Cryptography;
using System.Text;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The one supervisor of a state (reliability batch R1): a named mutex that the window holds while it runs the supervisor pass. A second
/// window on the same state cannot take it, so it only watches. The name is per state path, so two states have two supervisors.
/// A mutex whose last holder ended without releasing it (<see cref="AbandonedMutexException"/>) is taken: the name is free.
/// The mutex is thread-affine, so it is taken and released on the same thread (the window's UI thread).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned = true;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>The mutex name for a state file: "Local\OpenTail.Bullshark.supervisor." and a hash of the full path, in lower case.</summary>
    public static string NameFor(string statePath)
    {
        var full = Path.GetFullPath(statePath).ToLowerInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
        return "Local\\OpenTail.Bullshark.supervisor." + key;
    }

    /// <summary>Takes the name without waiting. Null when another holder has it; the caller then only watches.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            if (mutex.WaitOne(0)) return new SingleInstance(mutex);
        }
        catch (AbandonedMutexException)
        {
            // The last holder ended without releasing: the name is ours now, and the pass reads the state before it writes.
            return new SingleInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    /// <summary>Releases the name. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (!_owned) return;
        _owned = false;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not held by this thread any more (it was released elsewhere): nothing to release.
        }
        _mutex.Dispose();
    }
}

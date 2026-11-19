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

    /// <summary>Takes the name without waiting. Null when another holder has it, or when the name cannot be used (see the other overload).</summary>
    public static SingleInstance? TryAcquire(string name) => TryAcquire(name, out _);

    /// <summary>
    /// Takes the name without waiting. Null when another holder has it: <paramref name="problem"/> is then null. Null also when the name cannot be used at
    /// all (a mutex of another user's privilege level, or one that cannot be waited on): <paramref name="problem"/> says why, and the caller only
    /// watches. It never throws, so a window that cannot take the name still opens and draws (reliability review R2).
    /// <paramref name="createMutex"/> is the seam the tests use; the window passes nothing.
    /// </summary>
    public static SingleInstance? TryAcquire(string name, out string? problem, Func<string, Mutex>? createMutex = null)
    {
        problem = null;
        Mutex? mutex = null;
        try
        {
            mutex = (createMutex ?? CreateMutex)(name);
            if (mutex.WaitOne(0)) return new SingleInstance(mutex);
        }
        catch (AbandonedMutexException) when (mutex is not null)
        {
            // The last holder ended without releasing: the name is ours now, and the pass reads the state before it writes.
            return new SingleInstance(mutex);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            mutex?.Dispose();
            problem = $"the supervisor name cannot be taken ({ex.GetType().Name})";
            return null;
        }
        mutex?.Dispose();
        return null;
    }

    private static Mutex CreateMutex(string name) => new(false, name);

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

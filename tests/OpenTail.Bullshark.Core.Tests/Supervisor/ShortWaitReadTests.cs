using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Reliability batch R7: the window's UI thread reads the state files with a short wait for the lock. A read that is not in time throws
/// <see cref="TimeoutException"/> (the window keeps its last good data); a read with the default wait still waits for the lock.
/// </summary>
public sealed class ShortWaitReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-shortwait-" + Guid.NewGuid().ToString("N")[..8]);

    public ShortWaitReadTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Holds the file's lock on another thread until the returned action is called.</summary>
    private Action HoldLock(string path)
    {
        var held = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using (FileLock.Hold(path))
            {
                held.Set();
                release.Wait();
            }
        });
        held.Wait();
        return () =>
        {
            release.Set();
            holder.Wait();
        };
    }

    [Fact]
    public void TheStateStoreReadTimesOutInItsShortWait()
    {
        var path = Path.Combine(_dir, "state.json");
        var store = new StateStore(path);
        var release = HoldLock(path);
        try
        {
            Assert.Throws<TimeoutException>(() => store.Load(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            release();
        }
    }

    [Fact]
    public void TheRunStoreReadTimesOutInItsShortWait()
    {
        var path = Path.Combine(_dir, "runs.json");
        var store = new RunStore(path);
        var release = HoldLock(path);
        try
        {
            Assert.Throws<TimeoutException>(() => store.Load(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            release();
        }
    }

    [Fact]
    public void TheLibraryReadTimesOutInItsShortWait()
    {
        var path = Path.Combine(_dir, "library.json");
        var library = new SessionLibrary(path);
        var release = HoldLock(path);
        try
        {
            Assert.Throws<TimeoutException>(() => library.Load(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            release();
        }
    }

    [Fact]
    public void AReadWithTheShortWaitSucceedsWhenTheLockIsFree()
    {
        var path = Path.Combine(_dir, "state.json");
        var store = new StateStore(path);
        store.Save(new Dictionary<string, SessionRecord>());

        var records = store.Load(TimeSpan.FromMilliseconds(100));

        Assert.Empty(records);
    }
}

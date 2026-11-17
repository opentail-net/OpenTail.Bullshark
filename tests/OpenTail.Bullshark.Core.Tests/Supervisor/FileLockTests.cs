using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The lock per file and the atomic write. A named mutex is re-entered by the thread that holds it, and a task that is waited on
/// can run inline on the waiting thread, so every attempt to take a held lock runs on a thread of its own. OpenTail.Bullshark
/// runs on Windows, where file names do not depend on case.
/// </summary>
public sealed class FileLockTests : IDisposable
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(300);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-lock-" + Guid.NewGuid().ToString("N")[..8]);

    public FileLockTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Tries to take the lock for the path on a thread of its own, and releases it at once if it gets it. Returns null when the
    /// lock was taken, or the error that stopped the attempt (a timeout, when another holder kept the lock).
    /// </summary>
    private static Exception? TryLockOnAnotherThread(string path)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                FileLock.Hold(path, ShortWait).Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();
        return error;
    }

    [Fact]
    public void TheSameSpellingWaitsWhileTheLockIsHeld()
    {
        var path = Path.Combine(_dir, "state.json");
        using var held = FileLock.Hold(path);

        Assert.IsType<TimeoutException>(TryLockOnAnotherThread(path));
    }

    [Fact]
    public void ADifferentCaseOfTheSamePathWaitsToo()
    {
        // Two spellings of one file are one lock: the second must not take the lock while the first is held.
        var path = Path.Combine(_dir, "State.json");
        var upper = path.ToUpperInvariant();
        using var held = FileLock.Hold(path);

        Assert.IsType<TimeoutException>(TryLockOnAnotherThread(upper));
    }

    [Fact]
    public void TheOtherSpellingCanTakeTheLockOnceItIsReleased()
    {
        var path = Path.Combine(_dir, "runs.json");
        var upper = path.ToUpperInvariant();
        FileLock.Hold(path).Dispose();

        Assert.Null(TryLockOnAnotherThread(upper));
    }

    [Fact]
    public void DifferentFilesDoNotShareALock()
    {
        var first = Path.Combine(_dir, "a.json");
        var second = Path.Combine(_dir, "b.json");
        using var held = FileLock.Hold(first);

        Assert.Null(TryLockOnAnotherThread(second));
    }

    [Fact]
    public void AFailedMoveLeavesNoTemporaryFile()
    {
        // A folder cannot be replaced by a file, so the move over it fails after the temporary file is written.
        var target = Path.Combine(_dir, "target");
        Directory.CreateDirectory(target);

        var error = Record.Exception(() => FileLock.WriteAtomic(target, "text"));

        Assert.True(error is IOException or UnauthorizedAccessException, $"expected a file error, got {error?.GetType().Name}");
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void AGoodWriteReplacesTheFileAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, "old");

        FileLock.WriteAtomic(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}

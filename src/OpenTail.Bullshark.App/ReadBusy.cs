namespace OpenTail.Bullshark.App;

/// <summary>
/// The window's reads of the state files on its UI thread wait briefly for the file lock (reliability batch R7). A read that cannot get the lock
/// keeps the last good data, and the window says "state is busy, showing the last read" once when that starts: the flag below is set on the first
/// busy read, and taken once by the window (<see cref="TakeNotice"/>). A read that gets the lock again ends the busy state.
/// </summary>
internal static class ReadBusy
{
    /// <summary>The status-line text, once, when a UI read finds the state busy.</summary>
    public const string Notice = "state is busy, showing the last read";

    private static int _busy;
    private static int _pending;

    /// <summary>Records whether the last UI read of the state was busy. A new busy state raises the notice.</summary>
    public static void Note(bool busy)
    {
        var was = Interlocked.Exchange(ref _busy, busy ? 1 : 0) == 1;
        if (busy && !was) Interlocked.Exchange(ref _pending, 1);
    }

    /// <summary>True once for each new busy state: the caller shows <see cref="Notice"/> on its status line.</summary>
    public static bool TakeNotice() => Interlocked.Exchange(ref _pending, 0) == 1;
}

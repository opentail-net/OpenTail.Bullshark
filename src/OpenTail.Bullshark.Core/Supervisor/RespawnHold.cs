namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The hold on starts and resumes while `claude respawn --all` restarts the background sessions (round 8 item 3). While it runs, and for
/// <see cref="Window"/> after it ends, a supervision pass makes no start or resume decision: the restarting workers are not all listed yet, so
/// a session that is missing from the listing is not "gone", and a second claude process must not be started for it. The pass still reads the
/// listing and shows it. Shared by the window and the runner, and read on a pool thread, so it is locked.
/// The window is measured on the monotonic clock (round 9 item 6): setting the wall clock back would otherwise extend the hold, and setting it
/// forward would end it early.
/// </summary>
public sealed class RespawnHold(TimeProvider? clock = null)
{
    /// <summary>How long the hold lasts after the respawn ends.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _lock = new();
    private bool _running;

    /// <summary>The monotonic timestamp at which the respawn ended, or null while none has ended (round 9 item 6).</summary>
    private long? _endedAt;

    /// <summary>True while the respawn runs, and for <see cref="Window"/> after it ends. False before the first respawn.</summary>
    public bool IsHeld
    {
        get
        {
            lock (_lock)
            {
                if (_running) return true;
                return _endedAt is { } ended && _clock.GetElapsedTime(ended) < Window;
            }
        }
    }

    /// <summary>Called when the respawn starts: the hold is on from now, whatever the time was before.</summary>
    public void Begin()
    {
        lock (_lock)
        {
            _running = true;
            _endedAt = null;
        }
    }

    /// <summary>Called when the respawn has returned (success or failure): the hold runs on for <see cref="Window"/> from now.</summary>
    public void End()
    {
        lock (_lock)
        {
            _running = false;
            _endedAt = _clock.GetTimestamp();
        }
    }

    /// <summary>
    /// The text the owner reads before `claude respawn --all` runs. It says what happens to the blocked and the stopped sessions, and that
    /// starts and resumes wait for the hold to end. Whether the CLI restarts a stopped session is not checked (`claude respawn --help` does not
    /// say), so the text does not promise it.
    /// </summary>
    public static string ConfirmationText(string cliVersion) =>
        $"Restart all background sessions so they run the installed claude {cliVersion}?\n\n" +
        "This runs `claude respawn --all`, which restarts the background sessions so that they run the installed version.\n\n" +
        "Blocked sessions lose the prompt they are waiting on: answer them again in their tabs.\n" +
        "Bullshark does not restart a stopped session. Whether `claude respawn --all` restarts one is not checked, so look at the list afterwards.\n\n" +
        $"Starts and resumes wait for {Window.TotalSeconds:0} seconds after the respawn ends.";
}

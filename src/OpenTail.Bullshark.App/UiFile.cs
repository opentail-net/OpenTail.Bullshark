using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// ui.json for the window (the rail theme, the zoom and the notify setting). The file, its lock, its atomic write and its recovery are in
/// <see cref="UiSettingsFile"/>; the lines it logs go to the app log. A file that cannot be read gives null, and a save that fails is logged.
/// <para>
/// A read on the UI thread waits at most <see cref="UiWait"/> for the lock (reliability batch R7). If the file is busy, the last value read for that
/// key is given, and <see cref="ReadBusy"/> says so once. A read on a pool thread (<see cref="ReadInBackground"/>) keeps the longer wait.
/// </para>
/// </summary>
public static class UiFile
{
    /// <summary>How long a UI-thread read waits for ui.json's lock. Writers and background reads keep the longer wait.</summary>
    public static readonly TimeSpan UiWait = TimeSpan.FromSeconds(2);

    /// <summary>The last value read for each file and key, so a busy read can give it (R7).</summary>
    private static readonly ConcurrentDictionary<string, JsonNode?> LastGood = new(StringComparer.Ordinal);

    private static long _uiThreadReads;
    private static long _backgroundReads;

    /// <summary>
    /// How many reads of ui.json the process made: on the UI thread (<see cref="Read"/>) and on a pool thread (<see cref="ReadInBackground"/>). Counted
    /// only, for the soak log (perf batch Q1); nothing else reads it.
    /// </summary>
    public static (long UiThread, long Background) ReadCounts =>
        (Interlocked.Read(ref _uiThreadReads), Interlocked.Read(ref _backgroundReads));

    public static string PathFor(string stateFile) => UiSettingsFile.PathFor(stateFile);

    /// <summary>The value of one key, read on the UI thread. Null when the file or the key is missing, or the file cannot be read.</summary>
    public static JsonNode? Read(string stateFile, string key)
    {
        Interlocked.Increment(ref _uiThreadReads);
        var path = PathFor(stateFile);
        var slot = path + "\u0001" + key;
        if (UiSettingsFile.TryRead(path, key, UiWait, out var value, AppLog.Write))
        {
            ReadBusy.Note(false);
            LastGood[slot] = value?.DeepClone();
            return value;
        }
        ReadBusy.Note(true);
        return LastGood.TryGetValue(slot, out var last) ? last?.DeepClone() : null;
    }

    /// <summary>
    /// The whole file as an object, read on the UI thread for at most <see cref="UiWait"/> (perf and testability batch, Q2). False when the lock stayed
    /// busy; the caller then keeps the values it had. Used where a window draws a setting at once, such as the rail after a change in Settings.
    /// </summary>
    public static bool TryReadAll(string stateFile, out JsonObject? root)
    {
        Interlocked.Increment(ref _uiThreadReads);
        var ok = UiSettingsFile.TryReadAll(PathFor(stateFile), UiWait, out root, AppLog.Write);
        ReadBusy.Note(!ok);
        return ok;
    }

    /// <summary>
    /// The whole file as an object, read on a pool thread: it waits for the lock as long as a writer does (perf and testability batch, Q2). False
    /// when the lock stayed busy for that long.
    /// </summary>
    public static bool TryReadAllInBackground(string stateFile, out JsonObject? root)
    {
        Interlocked.Increment(ref _backgroundReads);
        return UiSettingsFile.TryReadAll(PathFor(stateFile), UiSettingsFile.DefaultWait, out root, AppLog.Write);
    }

    /// <summary>The value of one key, read on a pool thread: it waits for the lock as long as a writer does. Null as <see cref="Read"/>.</summary>
    public static JsonNode? ReadInBackground(string stateFile, string key)
    {
        Interlocked.Increment(ref _backgroundReads);
        return UiSettingsFile.Read(PathFor(stateFile), key, AppLog.Write);
    }

    /// <summary>Sets one key and writes the file, keeping the other keys. False when the file was not written (the failure is logged).</summary>
    public static bool Set(string stateFile, string key, JsonNode? value) => UiSettingsFile.Set(PathFor(stateFile), key, value, AppLog.Write);

    /// <summary>
    /// Changes one key under one hold of the lock (reliability batch R5): <paramref name="change"/> gets the value as the file holds it now, and its
    /// result is written before the lock is let go. Use it wherever a window keeps a copy of a value that another window may also change.
    /// </summary>
    public static bool Update(string stateFile, string key, Func<JsonNode?, JsonNode?> change) =>
        UiSettingsFile.Update(PathFor(stateFile), key, change, AppLog.Write);
}

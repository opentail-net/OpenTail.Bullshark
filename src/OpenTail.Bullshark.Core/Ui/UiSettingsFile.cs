using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// ui.json, beside the state file: the rail theme and the zoom of the session text (zoom.md). Each save changes one key and keeps the
/// others. Every read and write takes the file's lock and writes atomically (<see cref="FileLock"/>), so two windows never lose each
/// other's key. A file that is not a JSON object (truncated or damaged) is moved aside to <c>ui.json.bad-yyyyMMdd-HHmmss</c>, logged, and read as
/// missing, so the next save starts a new file; without that, every later save would fail. A file that cannot be read because
/// another process holds it is left as it is: it is damaged only when it does not parse.
/// </summary>
public static class UiSettingsFile
{
    public const string FileName = "ui.json";

    /// <summary>The ui.json beside a state file.</summary>
    public static string PathFor(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, FileName);

    /// <summary>How long a read or a write waits for the file's lock, as <see cref="FileLock.Hold(string)"/> does.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    /// <summary>The value of one key, or null when the file or the key is missing, or the file cannot be read.</summary>
    public static JsonNode? Read(string path, string key, Action<string>? log = null)
    {
        log ??= _ => { };
        return TryRead(path, key, DefaultWait, out var value, log) ? value : NotRead(log);
    }

    private static JsonNode? NotRead(Action<string> log)
    {
        log("ui.json not read: the file was held by another process for too long.");
        return null;
    }

    /// <summary>
    /// Reads one key, waiting at most <paramref name="wait"/> for the file's lock (reliability batch R7). Returns false when the lock was not free
    /// in time: the file is busy, and <paramref name="value"/> is null. A file that cannot be read gives true with a null value, as <see cref="Read"/> does.
    /// </summary>
    public static bool TryRead(string path, string key, TimeSpan wait, out JsonNode? value, Action<string>? log = null)
    {
        log ??= _ => { };
        value = null;
        try
        {
            using var _ = FileLock.Hold(path, wait);
            value = ReadRoot(path, log)?[key];
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"ui.json not read: {ex.GetType().Name}");
            return true;
        }
    }

    /// <summary>
    /// Reads the whole file as an object in one hold of the lock (perf and testability batch, Q2), so a pass reads its keys together. Returns false
    /// when the lock was not free within <paramref name="wait"/>: the file is busy, and <paramref name="root"/> is null. Otherwise it returns true, with
    /// null when the file is missing, is not an object (then it is moved aside, as <see cref="Read"/> does) or cannot be read.
    /// </summary>
    public static bool TryReadAll(string path, TimeSpan wait, out JsonObject? root, Action<string>? log = null)
    {
        log ??= _ => { };
        root = null;
        try
        {
            using var _ = FileLock.Hold(path, wait);
            root = ReadRoot(path, log);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"ui.json not read: {ex.GetType().Name}");
            return true;
        }
    }

    /// <summary>
    /// Changes one key inside one hold of the file's lock (reliability batch R5). The stored value is read, changed by <paramref name="change"/> and
    /// written under the same hold, so a change another window made since this one last read the file is kept. <paramref name="change"/> gets a copy
    /// of the stored value (null when the key is missing) and returns the value to store. Returns true when the file was written; a failure is
    /// logged and gives false (nothing is thrown, and the file is not replaced).
    /// </summary>
    public static bool Update(string path, string key, Func<JsonNode?, JsonNode?> change, Action<string>? log = null, TimeSpan? wait = null)
    {
        log ??= _ => { };
        try
        {
            using var _ = FileLock.Hold(path, wait ?? DefaultWait);
            var root = ReadRoot(path, log) ?? new JsonObject();
            root[key] = change(root[key]?.DeepClone());
            FileLock.WriteAtomic(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            log($"ui.json not saved: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>Sets one key and writes the file, keeping the other keys. The change is not made when the file cannot be written.</summary>
    /// <summary>Sets one key and writes the file. Returns true when it was written; a failure is logged and gives false (nothing is thrown).</summary>
    public static bool Set(string path, string key, JsonNode? value, Action<string>? log = null)
    {
        log ??= _ => { };
        try
        {
            using var _ = FileLock.Hold(path);
            var root = ReadRoot(path, log) ?? new JsonObject();
            root[key] = value;
            FileLock.WriteAtomic(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            log($"ui.json not saved: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// The file as an object: null when there is no file, or when the file does not parse as one (it is then moved aside). A file that
    /// cannot be read at all throws, and is left where it is.
    /// </summary>
    private static JsonObject? ReadRoot(string path, Action<string> log)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new JsonException("it is not a JSON object");
        }
        catch (JsonException ex)
        {
            MoveAside(path, log, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The most damaged copies kept beside the file. The oldest go first.</summary>
    public const int MaxDamagedCopies = 5;

    /// <summary>
    /// Moves a damaged file to <c>ui.json.bad-yyyyMMdd-HHmmss</c> (F7): an earlier damaged copy is kept, not replaced, and only the
    /// newest <see cref="MaxDamagedCopies"/> are kept. Logs it once. If the move fails, the error goes to the caller, which logs it and
    /// changes nothing: the damaged file is never overwritten.
    /// </summary>
    private static void MoveAside(string path, Action<string> log, string reason)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var bad = NextDamagedName(path, stamp);
        File.Move(path, bad, overwrite: false);
        PruneDamagedCopies(path);
        log($"ui.json cannot be read ({reason}); it was moved aside, and a new one is started.");
    }

    /// <summary>
    /// The name for a new damaged copy: the base name, or with the next counter after the highest one already used in that second. A
    /// counter is never reused, even when pruning has removed a lower one, so the names keep their order (F7).
    /// </summary>
    private static string NextDamagedName(string path, string stamp)
    {
        var directory = Path.GetDirectoryName(path);
        var prefix = Path.GetFileName(path) + ".bad-";
        var used = string.IsNullOrEmpty(directory)
            ? new List<int>()
            : Directory.GetFiles(directory, prefix + stamp + "*")
                .Select(file => OrderOf(Path.GetFileName(file), prefix))
                .Where(order => order.Stamp == stamp)
                .Select(order => order.Counter)
                .ToList();
        var counter = used.Count == 0 ? 1 : used.Max() + 1;
        return counter == 1 ? path + ".bad-" + stamp : path + ".bad-" + stamp + "-" + counter;
    }

    /// <summary>Deletes the damaged copies beyond <see cref="MaxDamagedCopies"/>, oldest first by the time in the name, then by its counter.</summary>
    private static void PruneDamagedCopies(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return;
        var prefix = Path.GetFileName(path) + ".bad-";
        var copies = Directory.GetFiles(directory, prefix + "*")
            .OrderBy(file => OrderOf(Path.GetFileName(file), prefix).Stamp, StringComparer.Ordinal)
            .ThenBy(file => OrderOf(Path.GetFileName(file), prefix).Counter)
            .ToList();
        foreach (var old in copies.Take(Math.Max(0, copies.Count - MaxDamagedCopies)))
        {
            try { File.Delete(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The time part and the counter of a damaged copy's name: "yyyyMMdd-HHmmss" and an optional "-n" (1 when there is none).</summary>
    private static (string Stamp, int Counter) OrderOf(string fileName, string prefix)
    {
        var rest = fileName[prefix.Length..];
        var stamp = rest.Length >= 15 ? rest[..15] : rest;
        var counter = rest.Length > 16 && rest[15] == '-' && int.TryParse(rest[16..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        return (stamp, counter);
    }
}

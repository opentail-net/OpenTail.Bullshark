using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Health;

/// <summary>
/// The supervisor heartbeat (night batch N1): the time of the window's last supervisor pass, kept in <c>heartbeat.json</c> beside the state
/// file. The window is the only supervisor, so this file is how the owner learns, in the morning, whether the window ran in the night. It is
/// one small file, written atomically (<see cref="FileLock.WriteAtomic"/>), and read tolerantly: a missing or damaged file is "no pass
/// recorded", never an error.
/// </summary>
public static class Heartbeat
{
    /// <summary>The file name, beside the state file.</summary>
    public const string FileName = "heartbeat.json";

    /// <summary>The shortest gap between two writes. A pass runs every 15 seconds, so a write on each would be needless disk work.</summary>
    public static readonly TimeSpan MinWriteInterval = TimeSpan.FromSeconds(10);

    /// <summary>The heartbeat file beside a state file.</summary>
    public static string PathFor(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, FileName);

    /// <summary>
    /// Writes the time of a pass, and the pass number when one is given. The write is atomic. It throws the usual file errors (the caller logs
    /// them); nothing else is written.
    /// </summary>
    public static void Write(string path, DateTimeOffset now, int? passNumber = null)
    {
        var root = new JsonObject { ["at"] = now.ToString("O", CultureInfo.InvariantCulture) };
        if (passNumber is { } pass) root["pass"] = pass;
        FileLock.WriteAtomic(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// The time of the last pass in the file, or null when the file is missing, cannot be read, or does not hold a time. Never throws.
    /// </summary>
    public static DateTimeOffset? Read(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (JsonNode.Parse(text) is not JsonObject root) return null;
            if (!root.TryGetPropertyValue("at", out var value) || value is not JsonValue json) return null;
            if (!json.TryGetValue<string>(out var stamp)) return null;
            return DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>How long ago the last pass was, or null when none is recorded. A time after <paramref name="now"/> (a clock that went back) is zero.</summary>
    public static TimeSpan? Age(DateTimeOffset? last, DateTimeOffset now)
    {
        if (last is not { } at) return null;
        var age = now - at;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    /// <summary>
    /// The age as a short phrase for the owner: "12 s ago" under a minute, "3 min ago" under an hour, "2 h ago" under a day, and "3 d ago"
    /// after that. The count is rounded down.
    /// </summary>
    public static string Describe(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age < TimeSpan.FromMinutes(1)) return $"{(int)age.TotalSeconds} s ago";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} d ago";
    }

    /// <summary>
    /// True when a write is due: none was made yet, or the last one is at least <see cref="MinWriteInterval"/> old. A clock that went back
    /// is written at once.
    /// </summary>
    public static bool WriteDue(DateTimeOffset? lastWrite, DateTimeOffset now)
    {
        if (lastWrite is not { } at) return true;
        var gap = now - at;
        return gap < TimeSpan.Zero || gap >= MinWriteInterval;
    }
}

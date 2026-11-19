using System.Globalization;
using System.Text;

namespace OpenTail.Bullshark.Core.Diagnostics;

/// <summary>What a crash file is built from: when, the app version, the OS and runtime, the exception, and the app log's lines.</summary>
public sealed record CrashInput(
    DateTimeOffset At,
    string AppVersion,
    string OperatingSystem,
    string Runtime,
    Exception Exception,
    IReadOnlyList<string> LogLines,
    string UserProfile);

/// <summary>
/// The crash file written when the app hit an error it kept going past (firstrun batch B3): <c>logs\crash-yyyyMMdd-HHmmss.txt</c>. Its text is the
/// exception (type, message, stack, inner exceptions) and the last lines of app.log, every line through <see cref="Redactor"/>. Pure: the App
/// writes the text, and <see cref="KeptFiles"/> keeps the newest ones.
/// </summary>
public static class CrashReport
{
    /// <summary>How many crash files are kept: the newest.</summary>
    public const int KeepNewest = 10;

    /// <summary>How many lines of app.log go into a crash file.</summary>
    public const int LogLinesKept = 200;

    /// <summary>The file name pattern of a crash file, for <see cref="KeptFiles"/>.</summary>
    public const string Pattern = "crash-*.txt";

    /// <summary>The file name for a crash at a time: local time, so the owner can match it to the clock.</summary>
    public static string FileNameFor(DateTimeOffset at) =>
        $"crash-{at.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt";

    /// <summary>The text of a crash file. Nothing in it is unredacted: the exception and the log lines both pass through <see cref="Redactor"/>.</summary>
    public static string Build(CrashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var profile = input.UserProfile;
        var log = input.LogLines.Count > LogLinesKept ? input.LogLines.Skip(input.LogLines.Count - LogLinesKept) : input.LogLines;

        var text = new StringBuilder();
        text.AppendLine("OpenTail.Bullshark crash report");
        text.AppendLine("Redacted: paths start at ~ or at the file name, tokens and prompt lines are removed, long lines are cut.");
        text.AppendLine();
        text.AppendLine($"When: {input.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        text.AppendLine($"App: {input.AppVersion}");
        text.AppendLine($"OS: {input.OperatingSystem}");
        text.AppendLine($"Runtime: {input.Runtime}");
        text.AppendLine();
        text.AppendLine("Exception");
        text.AppendLine("---------");
        text.AppendLine(Redactor.Text(input.Exception.ToString(), profile));
        text.AppendLine();
        text.AppendLine($"Last {log.Count()} lines of app.log");
        text.AppendLine("---------");
        foreach (var line in Redactor.Lines(log, profile)) text.AppendLine(line);
        return text.ToString();
    }
}

/// <summary>
/// Keeps the newest files of a folder that match a pattern, and deletes the rest. The file names carry their time (yyyyMMdd-HHmmss), so the newest
/// is the last in name order. Only files that match are touched, and a file that cannot be deleted is left for the next time.
/// </summary>
public static class KeptFiles
{
    /// <summary>Deletes all but the newest <paramref name="keep"/> files matching <paramref name="pattern"/> in <paramref name="folder"/>.</summary>
    public static void KeepNewest(string folder, string pattern, int keep, Action<string>? log = null)
    {
        log ??= _ => { };
        try
        {
            if (!Directory.Exists(folder)) return;
            var newestFirst = Directory.EnumerateFiles(folder, pattern)
                .Select(file => Path.GetFileName(file))
                .OrderByDescending(name => name, StringComparer.Ordinal)
                .ToList();
            foreach (var name in newestFirst.Skip(keep))
            {
                try
                {
                    File.Delete(Path.Combine(folder, name));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log($"old file kept, not deleted: {name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"old files not counted: {ex.Message}");
        }
    }

    /// <summary>A name in the folder that is not taken yet: the name itself, else the name with "-2", "-3"... before its extension.</summary>
    public static string UniqueName(string folder, string name)
    {
        if (!File.Exists(Path.Combine(folder, name))) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}-{n}{extension}";
            if (!File.Exists(Path.Combine(folder, candidate))) return candidate;
        }
    }
}

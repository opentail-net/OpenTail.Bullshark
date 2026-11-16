using System.Text;

namespace OpenTail.Bullshark.Core.Resume;

/// <summary>
/// Keeps each summary the app makes as a file (review of 2026-11-15, Phase 2, step 1, decided 2026-11-16). The files sit in a
/// Summaries folder beside the state file, so a scratch run keeps its own, as the library and the runs do. The start text does
/// not name the file yet: that is step 2, after the Phase 3 run. A file that cannot be written must not stop a resume; the
/// caller logs the exception and goes on.
/// </summary>
public static class SummaryFiles
{
    /// <summary>The longest session name in a file name, in characters, so that the path stays short.</summary>
    public const int MaxNameLength = 60;

    /// <summary>The Summaries folder that goes with a state file.</summary>
    public static string FolderBeside(string statePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "Summaries");

    /// <summary>
    /// The file name, with the date first so the files sort by day: the date, the session's name, the level and the short
    /// session id, for example <c>2026-11-16 Plan for L4 complete 5bc2852c.md</c>. The name is cleaned of characters a file
    /// name cannot hold and cut to <see cref="MaxNameLength"/>.
    /// </summary>
    public static string FileNameFor(DateOnly date, string sessionName, SummaryLevel level, string sessionId)
    {
        var shortId = sessionId.Length >= 8 ? sessionId[..8] : sessionId;
        return $"{date:yyyy-MM-dd} {CleanName(sessionName)} {level.ToString().ToLowerInvariant()} {shortId}.md";
    }

    /// <summary>
    /// Writes the summary to a new file in the folder and returns its path. The folder is created when it is missing. When a
    /// file of the same name already exists (the same session summarised twice on one day at one level), the new file gets
    /// " (2)", " (3)" and so on, so no earlier summary is overwritten.
    /// </summary>
    public static string Save(
        string folder,
        string sessionName,
        SummaryLevel level,
        string sessionId,
        string summary,
        DateTimeOffset madeAt)
    {
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(FileNameFor(DateOnly.FromDateTime(madeAt.LocalDateTime), sessionName, level, sessionId));
        var text = new StringBuilder()
            .Append("# ").AppendLine(CleanTitle(sessionName))
            .AppendLine()
            .AppendLine($"Summary of session {sessionId}, level {level.ToString().ToLowerInvariant()}, made {madeAt.LocalDateTime:yyyy-MM-dd HH:mm}.")
            .AppendLine()
            .AppendLine(summary.Trim())
            .ToString();

        for (var copy = 1; copy <= 1000; copy++)
        {
            var path = Path.Combine(folder, copy == 1 ? stem + ".md" : $"{stem} ({copy}).md");
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(text);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken already: try the next copy. Any other IO failure is thrown to the caller.
            }
        }
        throw new IOException($"no free file name for the summary of {sessionId} in {folder}");
    }

    /// <summary>A session name made safe for a file name: no characters Windows refuses, single spaces, cut to the limit.</summary>
    public static string CleanName(string sessionName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = sessionName.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c);
        var collapsed = string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxNameLength) collapsed = collapsed[..MaxNameLength];
        // A file name may not end in a dot or a space on Windows.
        collapsed = collapsed.TrimEnd('.', ' ');
        return collapsed.Length == 0 ? "session" : collapsed;
    }

    /// <summary>The title line of a summary file: the session's name as it reads, with control characters removed.</summary>
    private static string CleanTitle(string sessionName)
    {
        var title = new string(sessionName.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return title.Length == 0 ? "Session" : title;
    }
}

using System.Globalization;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The wording of the Copilot rail that is not a row (G2.2, G2.5): the empty state and why it is empty, the note that the history may be incomplete,
/// and the refusals of a resume (G-D12). Pure, so the wording is tested without a window. No session text is in any of these lines: they carry
/// counts, the history folder and the owner's folder path only.
/// </summary>
internal static class CopilotRailText
{
    /// <summary>The heading of the empty state, when the history holds no listed session.</summary>
    public const string EmptyHeading = "No Copilot sessions found";

    /// <summary>
    /// The lines under the empty heading: where the app looked, why a folder is not listed, and what to do. <paramref name="historyRoot"/> is the
    /// session folder the rail reads (COPILOT_HOME or the user's <c>.copilot</c>, then <c>session-state</c>). The counts are those of the last read.
    /// </summary>
    public static string EmptyReason(string historyRoot, int emptySessions, int damagedFolders, int skippedLines = 0)
    {
        var lines = new List<string>
        {
            $"Copilot sessions are read from {historyRoot}.",
            "A session shows once its folder holds an events file. Start one with New Copilot session….",
        };
        if (emptySessions > 0)
            lines.Add($"{Count(emptySessions, "session folder holds", "session folders hold")} a workspace file but no events, so not listed.");
        if (damagedFolders > 0)
            lines.Add($"{Count(damagedFolders, "session folder", "session folders")} could not be read, so not listed.");
        if (skippedLines > 0)
            lines.Add(skippedLines == 1
                ? "1 line of the session files was skipped (not a record, or too long)."
                : $"{skippedLines.ToString("N0", CultureInfo.InvariantCulture)} lines of the session files were skipped (not records, or too long).");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The status line of a read that skipped something (risk R-C1): "Copilot history may be incomplete: ...". Null when nothing was skipped, so a clean
    /// read says nothing.
    /// </summary>
    public static string? IncompleteNote(int damagedFolders, int skippedLines)
    {
        if (damagedFolders == 0 && skippedLines == 0) return null;
        var parts = new List<string>();
        if (damagedFolders > 0) parts.Add(Count(damagedFolders, "folder", "folders") + " could not be read");
        if (skippedLines > 0) parts.Add(Count(skippedLines, "line", "lines") + " skipped");
        return "Copilot history may be incomplete: " + string.Join("; ", parts) + ".";
    }

    /// <summary>The refusal of a resume whose id is not in the app's list of Copilot sessions (G-D12). No silent fallback to another session.</summary>
    public const string NotListed = "Not resumed: this Copilot session is not in the list. Reload the Copilot history, then try again.";

    /// <summary>The refusal of a resume whose session folder no longer exists: Copilot resumes a session in its own folder (G-D12).</summary>
    public static string FolderMissing(string? folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? "Not resumed: the session has no folder on record, so Copilot cannot open it there."
            : $"Not resumed: the session's folder is missing: {folder}";

    /// <summary>The refusal of a resume whose id the command line builder refuses.</summary>
    public const string InvalidId = "Not resumed: the session id is not a valid Copilot id.";

    private static string Count(int value, string singular, string plural) =>
        value == 1 ? $"1 {singular}" : $"{value.ToString("N0", CultureInfo.InvariantCulture)} {plural}";
}

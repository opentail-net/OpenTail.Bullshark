using System.Globalization;
using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The wording of the opencode rail that is not a row (OC2, copied from <c>CopilotRailText</c>, OC-D16): the empty state and why it is empty, the note
/// that the history may be incomplete, the refusals of a resume, and the search lines. The text that says the database cannot be read is not here: it is
/// <see cref="OpenCodeHistorySource.TextOf"/>, the fixed phrases of <see cref="OpenCodeHistoryStatus"/> (OC-D7). Pure, so the wording is tested without a
/// window. No session text is in any of these lines: they carry counts, the database path and the owner's folder path only.
/// </summary>
internal static class OpenCodeRailText
{
    /// <summary>The heading of the empty state, when the database holds no listed session.</summary>
    public const string EmptyHeading = "No opencode sessions found";

    /// <summary>
    /// The lines under the empty heading: where the app looked, what makes a session listed, and how many rows were left out. <paramref name="databasePath"/>
    /// is the database file the rail reads.
    /// </summary>
    public static string EmptyReason(string databasePath, int skippedRows = 0)
    {
        var lines = new List<string>
        {
            $"opencode sessions are read from {databasePath}.",
            "A session shows once opencode has saved it in that database, as a top-level session with a folder on record.",
        };
        if (skippedRows > 0)
            lines.Add($"{Count(skippedRows, "row of the database was", "rows of the database were")} skipped, as it could not be used.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The status line of a read that skipped rows: "opencode history may be incomplete: N rows skipped.". Null when nothing was skipped, so a clean read
    /// says nothing.
    /// </summary>
    public static string? IncompleteNote(int skippedRows) =>
        skippedRows <= 0 ? null : "opencode history may be incomplete: " + Count(skippedRows, "row", "rows") + " skipped.";

    /// <summary>The refusal of a resume whose id is not in the app's list of opencode sessions. No silent fallback to another session.</summary>
    public const string NotListed = "Not resumed: this opencode session is not in the list. Reload the opencode history, then try again.";

    /// <summary>The refusal of a resume whose session folder no longer exists: opencode resumes a session in its own folder (OC-D8).</summary>
    public static string FolderMissing(string? folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? "Not resumed: the session has no folder on record, so opencode cannot open it there."
            : $"Not resumed: the session's folder is missing: {folder}";

    /// <summary>The refusal of a resume whose id the command line builder refuses (OC-D8).</summary>
    public const string InvalidId = "Not resumed: the session id is not a valid opencode id.";

    /// <summary>The status line of a history read that threw. It names the exception type only.</summary>
    public static string ReadFailed(string exceptionTypeName) => $"opencode history not read: {exceptionTypeName}";

    /// <summary>The heading of the rail when the last read failed. The status line says why.</summary>
    public const string FailedHeading = "opencode history could not be read";

    /// <summary>The tooltip of <see cref="FailedHeading"/>.</summary>
    public const string FailedTip = "The status line says why. Reload to try again.";

    /// <summary>The rail's line while the history is being read.</summary>
    public const string Reading = "reading the opencode history…";

    /// <summary>The tooltip of the reload mark in the rail's header. Reading the database calls no model.</summary>
    public const string ReloadTip = "Reload the opencode history from its database (no model)";

    /// <summary>The rail's note that live opencode sessions are not shown. The tooltip says why.</summary>
    public const string LiveHeading = "opencode live sessions are not shown";

    /// <summary>The tooltip of <see cref="LiveHeading"/>.</summary>
    public const string LiveTip = "This window shows opencode history and the terminal tabs it opens. Live opencode sessions started elsewhere are not shown, and nothing is supervised.";

    /// <summary>The heading under a tag filter that leaves no opencode session.</summary>
    public static string TagEmpty(string tag) => $"No opencode session has the tag #{tag}";

    /// <summary>The tooltip of <see cref="TagEmpty"/>.</summary>
    public const string TagEmptyTip = "The tag filter shows the opencode sessions that carry the tag. Clear it from the filter menu.";

    /// <summary>The heading under the Archived view when no opencode session is archived.</summary>
    public const string ArchivedEmpty = "No opencode session is archived";

    /// <summary>The tooltip of <see cref="ArchivedEmpty"/>.</summary>
    public const string ArchivedEmptyTip = "Archive an opencode session from its menu, and it is listed here.";

    /// <summary>The search's status line when the history has not been read yet, or could not be read.</summary>
    public static string SearchNotRead(bool failed) =>
        failed
            ? "The opencode history could not be read, so there is nothing to search. Reload it to try again."
            : "The opencode history is still being read. Search again in a moment.";

    /// <summary>The search's status line when no session matches.</summary>
    public static string SearchNoMatch(string query) => $"No opencode session matches \"{query}\".";

    /// <summary>The search's status line when some sessions match: "2 of 9 opencode sessions match "x". Newest first.".</summary>
    public static string SearchMatches(int matched, int total, string query) =>
        $"{matched} of {total} opencode sessions match \"{query}\". Newest first.";

    private static string Count(int value, string singular, string plural) =>
        value == 1 ? $"1 {singular}" : $"{value.ToString("N0", CultureInfo.InvariantCulture)} {plural}";
}

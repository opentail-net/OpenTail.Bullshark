namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The arithmetic of the transcript view's window (perf and testability batch, Q3): the pages it reads, how many lines it keeps drawn, and what it
/// releases when it is over the cap. Pure. Positions are record indexes (zero-based) of the file, as <see cref="TranscriptPager"/> reads them; line
/// numbers are the one-based numbers the view shows.
/// </summary>
public static class TranscriptWindow
{
    /// <summary>The most lines the view keeps drawn. Past it, the lines at the far end are released (the view says so).</summary>
    public const int DefaultCap = 1000;

    /// <summary>
    /// The first record of the window a view opens on: the page around a focus line (a search result, a jump), centred on it, or the last page when
    /// there is no focus. The focus line is one-based, as the view shows it. Always inside the file, and zero for a file with no records.
    /// </summary>
    public static int StartFor(int recordCount, int pageSize, int? focusLine)
    {
        if (recordCount <= 0 || pageSize <= 0) return 0;
        if (focusLine is not { } line) return Math.Max(0, recordCount - pageSize);
        var focus = Math.Clamp(line - 1, 0, recordCount - 1);
        return Math.Max(0, focus - pageSize / 2);
    }

    /// <summary>The first record of the page before one that starts at <paramref name="first"/>: <paramref name="pageSize"/> earlier, not below zero.</summary>
    public static int EarlierStart(int first, int pageSize) => Math.Max(0, first - Math.Max(0, pageSize));

    /// <summary>
    /// How many drawn lines to release from the far end so that no more than <paramref name="cap"/> remain. Zero when the view is within the cap.
    /// </summary>
    public static int LinesToRelease(int drawn, int cap) => Math.Max(0, drawn - Math.Max(0, cap));

    /// <summary>True when the one-based line lies in the drawn range <paramref name="firstLine"/> to <paramref name="lastLine"/>, both included.</summary>
    public static bool Holds(int firstLine, int lastLine, int line) => line >= firstLine && line <= lastLine;

    /// <summary>
    /// The one muted line the view shows while lines are released: the range drawn, and how to bring the others back. Null when nothing was released.
    /// </summary>
    public static string? ReleasedNote(int released, int firstLine, int lastLine)
    {
        if (released <= 0) return null;
        return $"Showing lines {firstLine:N0}–{lastLine:N0}. {released:N0} lines at the end were released to keep the view light: " +
               "Find or a jump brings any line back.";
    }
}

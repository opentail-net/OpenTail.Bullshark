namespace OpenTail.Bullshark.Core.Git;

/// <summary>
/// The review prompt the owner copies from the Changes tab (batch B1, ideas #6). It is text for the owner to paste into the coder's own
/// tab. Nothing is sent to a session. It names the changed files and the owner's own note, and nothing from the transcript.
/// </summary>
public static class ReviewPrompt
{
    /// <summary>The most files the prompt names. Any others are counted as "and N more".</summary>
    public const int MaxFiles = 30;

    /// <summary>The prompt's length limit in characters. Files are dropped from its end until it fits.</summary>
    public const int MaxLength = 1500;

    /// <summary>The owner's note is cut to this many characters.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>
    /// The prompt for the changed files of a session, or null when there are no files to review. With a note, the coder is asked to
    /// address the note. Without one, it is asked to review the files for bugs, missing tests and leftover debug code.
    /// </summary>
    public static string? Build(string sessionName, IReadOnlyList<string> files, string? note)
    {
        if (files.Count == 0) return null;

        var who = string.IsNullOrWhiteSpace(sessionName) ? "this session" : sessionName;
        var text = NormalNote(note);
        for (var listed = Math.Min(files.Count, MaxFiles); ; listed--)
        {
            var prompt = Compose(who, files, listed, text);
            if (prompt.Length <= MaxLength || listed == 0) return prompt;
        }
    }

    /// <summary>The note with its ends trimmed and its length cut, or null when it is empty.</summary>
    private static string? NormalNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > MaxNoteLength ? trimmed[..MaxNoteLength] : trimmed;
    }

    private static string Compose(string who, IReadOnlyList<string> files, int listed, string? note)
    {
        var lines = new List<string>
        {
            note is null
                ? $"Review the changes in {who}. Re-read the files listed below and check them for bugs, missing tests and leftover debug code. Fix what you find, run the tests, and say what you changed."
                : $"Re-read the files listed below, then address this note from the owner: {note}",
            string.Empty,
            "Files:",
        };
        lines.AddRange(files.Take(listed).Select(file => "- " + file));
        var more = files.Count - listed;
        if (more > 0) lines.Add($"- and {more} more");
        return string.Join(Environment.NewLine, lines);
    }
}

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// One keyboard shortcut: the chord, what it does, and where it works (the scope). <see cref="HintLabel"/> is the short text the transcript's hint
/// line shows after the chord; null when the chord is not on that line.
/// </summary>
public sealed record Shortcut(string Chord, string Does, string Where, string? HintLabel = null);

/// <summary>
/// The one table of keyboard shortcuts (firstrun batch B7). The shortcut sheet in the command palette, the transcript's hint line and the tests read
/// it, so a chord is written once. A chord appears at most once per scope (<see cref="Shortcuts.Duplicates"/> finds one).
/// </summary>
public static class Shortcuts
{
    /// <summary>The scope of the window as a whole: the chords that work wherever the keyboard is in the window.</summary>
    public const string Window = "Anywhere in the window";

    /// <summary>The scope of a session's transcript, where the marks, zoom, find and copy work.</summary>
    public const string Transcript = "In a session's transcript";

    /// <summary>The scope of the find bar, open above a transcript.</summary>
    public const string Find = "In the find bar";

    /// <summary>The scope of the session search results.</summary>
    public const string Results = "In the search results";

    /// <summary>Every shortcut, in the order the sheet shows them.</summary>
    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new("Ctrl+Shift+P", "Opens the command palette: every command, by name", Window),
        new("Ctrl+Shift+N", "Goes to the session that waits next", Window),
        new("Ctrl+plus", "Makes the session text larger", Window),
        new("Ctrl+minus", "Makes the session text smaller", Window),
        new("Ctrl+0", "Sets the session text to 100 %", Window),
        new("[ ]", "Moves to the previous and the next prompt", Transcript, "prompts"),
        new("{ }", "Moves to the previous and the next failure", Transcript, "failures"),
        new("Alt+[ ]", "Moves to the previous and the next file write", Transcript, "writes"),
        new("Ctrl+[ ]", "Moves to the previous and the next compaction", Transcript, "compactions"),
        new("Ctrl+F", "Opens find in this session", Transcript, "find"),
        new("Ctrl+C", "Copies the focused line", Transcript),
        new("Enter", "Goes to the next match", Find),
        new("Shift+Enter", "Goes to the previous match", Find),
        new("Esc", "Closes the find bar", Find),
        new("Tab", "Moves between the results; Enter or Space opens the one that has the focus", Results),
    ];

    /// <summary>The chords of one scope, in the table's order.</summary>
    public static IReadOnlyList<Shortcut> In(string scope) => All.Where(shortcut => shortcut.Where == scope).ToList();

    /// <summary>The transcript's hint line, built from the table: "[ ] prompts   { } failures   Alt+[ ] writes   Ctrl+[ ] compactions   Ctrl+F find".</summary>
    public static string TranscriptHint { get; } = string.Join("   ",
        All.Where(shortcut => shortcut.Where == Transcript && shortcut.HintLabel is not null)
            .Select(shortcut => $"{shortcut.Chord} {shortcut.HintLabel}"));

    /// <summary>The (scope, chord) pairs that appear more than once. Empty when the table is sound.</summary>
    public static IReadOnlyList<string> Duplicates() =>
        All.GroupBy(shortcut => (shortcut.Where, shortcut.Chord))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Where}: {group.Key.Chord}")
            .ToList();
}

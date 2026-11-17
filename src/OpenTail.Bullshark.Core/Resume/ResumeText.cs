using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Resume;

/// <summary>The summary levels of D-S11. Minimal, normal and complete are written by a model; the tail is copied.</summary>
public enum SummaryLevel
{
    Minimal,
    Normal,
    Complete,
}

/// <summary>
/// The text a resumed session starts from (D-S11). The summary comes from a model. The tail is copied word for word: the last
/// N turns, with each tool call and result on one line. The tail is capped in tokens. When it is over the cap, the oldest turn
/// goes first, and the latest turn is never cut.
/// </summary>
public static class ResumeText
{
    /// <summary>The default number of turns kept verbatim.</summary>
    public const int DefaultTurns = 10;

    /// <summary>The longest summary a start text carries, in characters. A session starts from a command-line argument.</summary>
    public const int MaxSummaryChars = 6000;

    /// <summary>The most tokens the verbatim tail may take (D-S11).</summary>
    public const int TailTokenCap = 4000;

    /// <summary>The most transcript text a summary reads, in characters (about 100,000 tokens). The oldest text goes first.</summary>
    public const int SummaryInputChars = 400_000;

    /// <summary>A rough token count: about four characters to a token.</summary>
    public static int EstimateTokens(string text) => (text.Length + 3) / 4;

    /// <summary>The instruction for a summary of each level. The word limits are the sizes D-S11 names.</summary>
    public static string Instructions(SummaryLevel level) => level switch
    {
        SummaryLevel.Minimal =>
            "Summarise this Claude Code session in at most 120 words: the goal, what was done, and what is still open. " +
            "Plain text, no headings. Do not invent anything; say when something is unknown.",
        SummaryLevel.Normal =>
            "Summarise this Claude Code session in at most 400 words: the goal, the decisions and why they were made, " +
            "the files changed, the current state, and the open questions. Plain text, with short lists where they help. " +
            "Do not invent anything; say when something is unknown.",
        SummaryLevel.Complete =>
            "Summarise this Claude Code session in at most 1,200 words, in order: the goals, each decision with its reason, " +
            "every file changed, the tests and their results, the errors and how they were fixed, the current state, the " +
            "open questions, and the next steps. Plain text with headings. Do not invent anything; say when something is unknown.",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>
    /// The conversation the summary reads: one line for each message of the owner and each reply of Claude, after a list of the
    /// files the session wrote. Tool calls and results are left out. They are most of a long transcript and add little to a
    /// summary. When the text is longer than the limit, the oldest part is left out and the summary is told so.
    /// </summary>
    public static string SummaryInput(
        IReadOnlyList<TranscriptLine> lines,
        IReadOnlyList<string>? filesWritten = null,
        int maxChars = SummaryInputChars)
    {
        var header = FilesHeader(filesWritten);
        var budget = maxChars - header.Length;
        var rendered = lines.Select(SummaryLine).OfType<string>().ToList();
        var kept = new List<string>();
        var size = 0;
        for (var i = rendered.Count - 1; i >= 0; i--)
        {
            if (size + rendered[i].Length + 1 > budget) break;
            kept.Add(rendered[i]);
            size += rendered[i].Length + 1;
        }
        kept.Reverse();
        var notice = kept.Count == rendered.Count
            ? string.Empty
            : "(The start of this session is left out: it is longer than the summary reads.)\n";
        return header + notice + string.Join("\n", kept);
    }

    /// <summary>"Files written in this session: a, b" and a blank line, or nothing. At most 200 names are listed.</summary>
    private static string FilesHeader(IReadOnlyList<string>? filesWritten)
    {
        if (filesWritten is null) return string.Empty;
        var names = filesWritten.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return string.Empty;
        var listed = string.Join(", ", names.Take(200));
        var more = names.Count > 200 ? $" and {names.Count - 200} more" : string.Empty;
        return "Files written in this session: " + listed + more + "\n\n";
    }

    /// <summary>
    /// The last <paramref name="turns"/> turns, copied word for word, within the token cap. A turn starts at each "you" line.
    /// Returns an empty string when there are no turns to keep. With <paramref name="maxChars"/>, the text is also kept within that
    /// many characters: the oldest turns go first, and then the latest turn is cut in its middle (<see cref="CutMiddle"/>).
    /// </summary>
    public static string Tail(IReadOnlyList<TranscriptLine> lines, int turns = DefaultTurns, int maxTokens = TailTokenCap, int maxChars = int.MaxValue)
    {
        if (turns <= 0 || maxChars <= 0) return string.Empty;
        var chosen = SplitTurns(lines).TakeLast(turns).ToList();
        while (chosen.Count > 1 && chosen.Sum(turn => EstimateTokens(Render(turn))) > maxTokens) chosen.RemoveAt(0);

        var rendered = chosen.Select(turn => Render(turn)).ToList();
        while (rendered.Count > 1 && Joined(rendered).Length > maxChars) rendered.RemoveAt(0);
        if (rendered.Count == 1) rendered[0] = CutMiddle(rendered[0], maxChars);
        return Joined(rendered);
    }

    private static string Joined(IEnumerable<string> turns) => string.Join("\n\n", turns);

    /// <summary>The marker that stands in for the characters cut from a text.</summary>
    public static string CutMarker(int cut) => $" … [cut {cut.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} characters] … ";

    /// <summary>
    /// The text cut in its middle so that it is at most <paramref name="max"/> characters. Its start and its end are kept, and a
    /// marker between them says how many characters were cut. A text that already fits is returned as it is. A cut never splits a
    /// surrogate pair, and when <paramref name="max"/> is too small for the marker the result is empty.
    /// </summary>
    public static string CutMiddle(string text, int max)
    {
        if (text.Length <= max) return text;
        // The marker is allowed its widest form (the count has no more digits than the whole text), so the result never passes max.
        var room = max - CutMarker(text.Length).Length;
        if (room < 2) return string.Empty;

        var head = (room + 1) / 2;
        var tailLength = room - head;
        if (head > 0 && char.IsHighSurrogate(text[head - 1])) head--;
        var tailStart = text.Length - tailLength;
        if (tailStart < text.Length && char.IsLowSurrogate(text[tailStart])) tailStart++;
        return text[..head] + CutMarker(tailStart - head) + text[tailStart..];
    }

    /// <summary>A resumed session's start text, fitted to the command line it is launched with.</summary>
    /// <param name="Text">The start text, cut if it had to be.</param>
    /// <param name="CutCharacters">The characters left out of the start text to fit it (zero when it fitted as it was).</param>
    /// <param name="Fits">False only when the text is still over the limit after the cut.</param>
    public sealed record FittedStart(string Text, int CutCharacters, bool Fits);

    /// <summary>
    /// The start text of a resumed session, fitted to the launch. <paramref name="commandLineOf"/> gives the length of the command line
    /// the text would make (<c>SetupPlan.LongestCommandLine</c>, which counts the escaped arguments). When it is over <paramref name="limit"/>,
    /// the text is composed again with the tail cut by the excess, so the latest turn is cut in its middle and the older turns stay whole.
    /// A character cut from the tail can take more than one character off the command line (a quote is written as two), and a character
    /// that is cut can also leave the rest as it was. So the cut is taken again while the text is still over (round 5 item 12).
    /// </summary>
    public static FittedStart Fit(
        string? summary,
        SummaryLevel? level,
        IReadOnlyList<TranscriptLine> lines,
        int turns,
        string sourceSessionId,
        Func<string, int> commandLineOf,
        int limit = ClaudeCli.MaxLaunchCharacters)
    {
        var fullTail = Tail(lines, turns);
        var text = Compose(summary, level, fullTail, turns, sourceSessionId);
        var excess = commandLineOf(text) - limit;
        if (excess <= 0) return new FittedStart(text, 0, Fits: true);

        // Even with the tail gone, the text is over: nothing more can be cut to fit it, so the whole tail is left out.
        if (commandLineOf(Compose(summary, level, string.Empty, turns, sourceSessionId)) > limit)
            return new FittedStart(Compose(summary, level, string.Empty, turns, sourceSessionId), fullTail.Length, Fits: false);

        var cut = excess;
        while (true)
        {
            var cutTail = Tail(lines, turns, maxChars: Math.Max(0, fullTail.Length - cut));
            text = Compose(summary, level, cutTail, turns, sourceSessionId);
            var stillOver = commandLineOf(text) - limit;
            if (stillOver <= 0 || cut >= fullTail.Length)
                return new FittedStart(text, fullTail.Length - cutTail.Length, Fits: stillOver <= 0);
            cut += stillOver;
        }
    }

    /// <summary>The turns of a session, oldest first. Lines before the first "you" line form a turn of their own.</summary>
    public static List<List<TranscriptLine>> SplitTurns(IReadOnlyList<TranscriptLine> lines)
    {
        var turns = new List<List<TranscriptLine>>();
        foreach (var line in lines)
        {
            if (line.Kind == "you" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(line);
        }
        return turns;
    }

    /// <summary>
    /// The text the resumed session starts from: the line that names the session it continues (D-L8), a header, the summary
    /// when there is one, and the verbatim tail. <paramref name="sourceSessionId"/> is null when nothing is continued.
    /// </summary>
    public static string Compose(string? summary, SummaryLevel? level, string tail, int turns, string? sourceSessionId = null)
    {
        var text = new StringBuilder();
        if (sourceSessionId is { Length: > 0 } source) text.AppendLine($"Continues session: {source}");
        text.AppendLine("This session continues an earlier Claude Code session that had gone cold. What follows is not the full conversation.");
        if (summary is not null && level is not null)
        {
            text.AppendLine();
            text.AppendLine($"## Summary ({level.Value.ToString().ToLowerInvariant()})");
            text.AppendLine(Cap(summary.Trim()));
        }
        if (turns > 0 && tail.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"## The last {turns} turns, word for word (tool calls and results on one line each)");
            text.AppendLine(tail);
        }
        text.AppendLine();
        text.Append("Carry on from this. Where something is unclear, make the sensible decision and say what you decided.");
        return text.ToString();
    }

    private static string Cap(string summary) =>
        summary.Length <= MaxSummaryChars ? summary : summary[..MaxSummaryChars] + " (the rest of the summary is left out)";

    private static string Render(IEnumerable<TranscriptLine> turn) =>
        string.Join("\n", turn.Select(RenderLine).OfType<string>());

    private static string? RenderLine(TranscriptLine line) => line.Kind switch
    {
        "you" => "you: " + line.Text,
        "claude" => "claude: " + line.Text,
        "tool" or "result" => OneLine(line.Text),
        _ => null,
    };

    /// <summary>The summary's view of a line: the owner's messages and Claude's replies only.</summary>
    private static string? SummaryLine(TranscriptLine line) => line.Kind is "you" or "claude" ? RenderLine(line) : null;

    private static string OneLine(string text) => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}
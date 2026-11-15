using System.Text;
using OpenTail.Bullshark.Core.Activity;

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
    /// The transcript the summary reads, one line per record: "you:", "claude:", and one line for each tool call and result.
    /// When the text is longer than the limit, the oldest part is left out and the summary is told so.
    /// </summary>
    public static string SummaryInput(IReadOnlyList<TranscriptLine> lines, int maxChars = SummaryInputChars)
    {
        var rendered = lines.Select(RenderLine).OfType<string>().ToList();
        var kept = new List<string>();
        var size = 0;
        for (var i = rendered.Count - 1; i >= 0; i--)
        {
            if (size + rendered[i].Length + 1 > maxChars) break;
            kept.Add(rendered[i]);
            size += rendered[i].Length + 1;
        }
        kept.Reverse();
        var body = string.Join("\n", kept);
        return kept.Count == rendered.Count
            ? body
            : "(The start of this session is left out: it is longer than the summary reads.)\n" + body;
    }

    /// <summary>
    /// The last <paramref name="turns"/> turns, copied word for word, within the token cap. A turn starts at each "you" line.
    /// Returns an empty string when there are no turns to keep.
    /// </summary>
    public static string Tail(IReadOnlyList<TranscriptLine> lines, int turns = DefaultTurns, int maxTokens = TailTokenCap)
    {
        if (turns <= 0) return string.Empty;
        var chosen = SplitTurns(lines).TakeLast(turns).ToList();
        while (chosen.Count > 1 && chosen.Sum(turn => EstimateTokens(Render(turn))) > maxTokens) chosen.RemoveAt(0);
        return string.Join("\n\n", chosen.Select(turn => Render(turn)));
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

    /// <summary>The text the resumed session starts from: a header, the summary when there is one, and the verbatim tail.</summary>
    public static string Compose(string? summary, SummaryLevel? level, string tail, int turns)
    {
        var text = new StringBuilder();
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

    private static string OneLine(string text) => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}
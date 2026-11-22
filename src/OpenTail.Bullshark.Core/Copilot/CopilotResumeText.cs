using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The start text of a new Copilot session that continues an earlier one (G1.8). A copy of <see cref="ResumeText"/> and the Codex resume text, with
/// Copilot's wording and labels: the last turns are copied word for word (lines of <c>you: </c> and <c>copilot: </c>, messages only), within a token
/// cap and an optional character cap, and the summary, when there is one, is capped as Claude's is. The token and character limits are Claude's
/// constants. There are no injected-context prefixes here: the Codex prefixes do not occur in Copilot's data (G1.8, C11).
/// </summary>
public static class CopilotResumeText
{
    /// <summary>
    /// The turns of a session, oldest first. A turn starts at each <c>you</c> line; lines before the first one form a turn of their own.
    /// </summary>
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
    /// The last <paramref name="turns"/> turns, copied word for word, within <paramref name="maxTokens"/> (the oldest turn goes first while the
    /// tail is over the cap; the latest turn is never removed for the token cap). With <paramref name="maxChars"/>, the text is also kept within that
    /// many characters: the oldest turns go first, and then the latest turn is cut in its middle with <see cref="ResumeText.CutMiddle"/>. Returns an
    /// empty string when there is nothing to keep.
    /// </summary>
    public static string Tail(
        IReadOnlyList<TranscriptLine> lines,
        int turns = ResumeText.DefaultTurns,
        int maxTokens = ResumeText.TailTokenCap,
        int maxChars = int.MaxValue)
    {
        if (turns <= 0 || maxChars <= 0) return string.Empty;
        var chosen = SplitTurns(lines).TakeLast(turns).ToList();
        while (chosen.Count > 1 && chosen.Sum(turn => ResumeText.EstimateTokens(Render(turn))) > maxTokens) chosen.RemoveAt(0);

        var rendered = chosen.Select(turn => Render(turn)).ToList();
        while (rendered.Count > 1 && Joined(rendered).Length > maxChars) rendered.RemoveAt(0);
        if (rendered.Count == 1) rendered[0] = ResumeText.CutMiddle(rendered[0], maxChars);
        return Joined(rendered);
    }

    /// <summary>
    /// The text the resumed session starts from: the line that names the session it continues (when <paramref name="sourceSessionId"/> is set), a
    /// header in Copilot's words, the summary when there is one, the verbatim tail, and the closing line. Its newlines are the ones
    /// <see cref="ResumeText.Compose"/> uses.
    /// </summary>
    public static string Compose(string? summary, SummaryLevel? level, string tail, int turns, string? sourceSessionId = null)
    {
        var text = new StringBuilder();
        if (sourceSessionId is { Length: > 0 } source) text.AppendLine($"Continues session: {source}");
        text.AppendLine("This session continues an earlier GitHub Copilot CLI session that had gone cold. What follows is not the full conversation.");
        if (summary is not null && level is not null)
        {
            text.AppendLine();
            text.AppendLine($"## Summary ({level.Value.ToString().ToLowerInvariant()})");
            text.AppendLine(Cap(summary.Trim()));
        }
        if (turns > 0 && tail.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"## The last {turns} turns, word for word (messages only)");
            text.AppendLine(tail);
        }
        text.AppendLine();
        text.Append("Carry on from this. Where something is unclear, make the sensible decision and say what you decided.");
        return text.ToString();
    }

    private static string Joined(IEnumerable<string> turns) => string.Join("\n\n", turns);

    private static string Cap(string summary) =>
        summary.Length <= ResumeText.MaxSummaryChars
            ? summary
            : summary[..ResumeText.MaxSummaryChars] + " (the rest of the summary is left out)";

    private static string Render(IEnumerable<TranscriptLine> turn) =>
        string.Join("\n", turn.Select(line => RenderLine(line)).OfType<string>());

    /// <summary>The label of a message: <c>you: </c> for the owner, <c>copilot: </c> for Copilot. Any other kind is not in the tail.</summary>
    private static string? RenderLine(TranscriptLine line) => line.Kind switch
    {
        "you" => "you: " + line.Text,
        "copilot" => "copilot: " + line.Text,
        _ => null,
    };
}

using System.Text;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Resume;

/// <summary>
/// The parts of a start text that the provider CLIs share (step R4): the turns, the token and character caps, the summary cap, the join, the
/// render of a message and the header. Each provider passes its CLI's full phrase (<c>Codex CLI</c>, <c>GitHub Copilot CLI</c>) and the word its
/// messages carry in the transcript, which is also the label written before each of them (<c>codex</c>, <c>copilot</c>). The limits are Claude's
/// constants in <see cref="ResumeText"/>, which is read here and not changed. Claude does not use this class.
/// </summary>
public static class ProviderResumeText
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
    /// empty string when there is nothing to keep. Only the owner's messages and the <paramref name="assistant"/>'s are kept.
    /// </summary>
    public static string Tail(
        IReadOnlyList<TranscriptLine> lines,
        string assistant,
        int turns = ResumeText.DefaultTurns,
        int maxTokens = ResumeText.TailTokenCap,
        int maxChars = int.MaxValue)
    {
        if (turns <= 0 || maxChars <= 0) return string.Empty;
        var chosen = SplitTurns(lines).TakeLast(turns).ToList();
        while (chosen.Count > 1 && chosen.Sum(turn => ResumeText.EstimateTokens(Render(turn, assistant))) > maxTokens) chosen.RemoveAt(0);

        var rendered = chosen.Select(turn => Render(turn, assistant)).ToList();
        while (rendered.Count > 1 && Joined(rendered).Length > maxChars) rendered.RemoveAt(0);
        if (rendered.Count == 1) rendered[0] = ResumeText.CutMiddle(rendered[0], maxChars);
        return Joined(rendered);
    }

    /// <summary>
    /// The text the resumed session starts from: the line that names the session it continues (when <paramref name="sourceSessionId"/> is set), a
    /// header that names <paramref name="cliPhrase"/>, the summary when there is one, the verbatim tail, and the closing line. Its newlines are the
    /// ones <see cref="ResumeText.Compose"/> uses.
    /// </summary>
    public static string Compose(string cliPhrase, string? summary, SummaryLevel? level, string tail, int turns, string? sourceSessionId = null)
    {
        var text = new StringBuilder();
        if (sourceSessionId is { Length: > 0 } source) text.AppendLine($"Continues session: {source}");
        text.AppendLine($"This session continues an earlier {cliPhrase} session that had gone cold. What follows is not the full conversation.");
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

    /// <summary>The summary as the start text carries it: at most <see cref="ResumeText.MaxSummaryChars"/> characters, with a note when it is cut.</summary>
    public static string Cap(string summary) =>
        summary.Length <= ResumeText.MaxSummaryChars
            ? summary
            : summary[..ResumeText.MaxSummaryChars] + " (the rest of the summary is left out)";

    /// <summary>The rendered turns, joined with a blank line between them.</summary>
    public static string Joined(IEnumerable<string> turns) => string.Join("\n\n", turns);

    /// <summary>The messages of a turn, one line each. Any kind other than the owner's or the <paramref name="assistant"/>'s is left out.</summary>
    public static string Render(IEnumerable<TranscriptLine> turn, string assistant) =>
        string.Join("\n", turn.Select(line => RenderLine(line, assistant)).OfType<string>());

    /// <summary>The label of a message: <c>you: </c> for the owner, and <c>{assistant}: </c> for the CLI. Any other kind is not in the tail.</summary>
    private static string? RenderLine(TranscriptLine line, string assistant)
    {
        if (line.Kind == "you") return "you: " + line.Text;
        return line.Kind == assistant ? assistant + ": " + line.Text : null;
    }
}

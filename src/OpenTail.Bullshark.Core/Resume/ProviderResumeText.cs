using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Providers.Summary;

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
        int maxChars = int.MaxValue) =>
        TailWithTurns(lines, assistant, turns, maxTokens, maxChars).Text;

    /// <summary>
    /// <see cref="Tail"/> with the number of turns it keeps (<see cref="ResumeText.TailWithTurns"/> for the provider's messages). The turns kept are the ones
    /// <see cref="ResumeText.TurnsLeftOut"/> leaves, the same split the summary uses (RS-D23), cut by <paramref name="maxChars"/>.
    /// </summary>
    public static (string Text, int Turns) TailWithTurns(
        IReadOnlyList<TranscriptLine> lines,
        string assistant,
        int turns = ResumeText.DefaultTurns,
        int maxTokens = ResumeText.TailTokenCap,
        int maxChars = int.MaxValue)
    {
        if (turns <= 0 || maxChars <= 0) return (string.Empty, 0);
        var first = ResumeText.TurnsLeftOut(lines, turns, maxTokens, turn => ResumeText.EstimateTokens(Render(turn, assistant)));
        var rendered = SplitTurns(lines).Skip(first).Select(turn => Render(turn, assistant)).ToList();
        while (rendered.Count > 1 && Joined(rendered).Length > maxChars) rendered.RemoveAt(0);
        if (rendered.Count == 1) rendered[0] = ResumeText.CutMiddle(rendered[0], maxChars);
        var text = Joined(rendered);
        // A latest turn cut to nothing (too few characters for its marker) keeps no text, so it counts as no turn kept.
        return (text, text.Length == 0 ? 0 : rendered.Count);
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

    /// <summary>
    /// The text a new session starts from when it is in another platform than the session it is made from (RS-D6), with a summary of that session.
    /// It is <see cref="Compose"/> with a different header: the first line names the source platform (<paramref name="sourcePhrase"/>), the level
    /// heading is as <see cref="Compose"/> writes it, and a line says that the new <paramref name="targetPhrase"/> session starts from the summary and
    /// that the source session is not continued. The tail and the closing line are written as <see cref="Compose"/> writes them. There is no session
    /// line: a cross-platform start is not a continuation of the source session (RS-D5).
    /// With no summary and no level, the start is Zero (RS-D25): two header lines that say the new session starts from the last <paramref name="turns"/>
    /// turns of the source, then the tail and the closing line. Zero makes no model call, so it needs a tail. A summary and its level go together (RS-D2):
    /// one without the other throws, and so does a summary that is blank.
    /// </summary>
    public static string ComposeFrom(
        string targetPhrase,
        string sourcePhrase,
        string? summary,
        SummaryLevel? level,
        string tail,
        int turns)
    {
        if ((summary is null) != (level is null))
            throw new ArgumentException("A summary and its level go together (RS-D2).");
        if (summary is not null && string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("A cross-platform start needs a summary with text (RS-D2).");
        var hasTail = turns > 0 && tail.Length > 0;
        if (summary is null && !hasTail)
            throw new ArgumentException("A Zero start needs a tail (RS-D25): there is nothing to start from.");

        var text = new StringBuilder();
        if (summary is { } given && level is { } shown)
        {
            text.AppendLine($"Summary of a {sourcePhrase} session");
            text.AppendLine($"This new {targetPhrase} session starts from that summary. The {sourcePhrase} session itself is not continued.");
            text.AppendLine($"## Summary ({shown.ToString().ToLowerInvariant()})");
            text.AppendLine(Cap(given.Trim()));
        }
        else
        {
            text.AppendLine($"Turns from a {sourcePhrase} session");
            text.AppendLine($"This new {targetPhrase} session starts from the last {turns} turns of that session. The {sourcePhrase} session itself is not continued.");
        }
        if (hasTail)
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

    /// <summary>The tokens of one turn as the provider's tail renders it (<see cref="Render"/>), for <see cref="ResumeText.SummaryPart"/> and <see cref="ResumeText.SummaryHasLines"/>.</summary>
    public static int TurnTokens(IEnumerable<TranscriptLine> turn, string assistant) => ResumeText.EstimateTokens(Render(turn, assistant));

    /// <summary>
    /// The summary's input for a provider's session (RS-D20): the messages of <paramref name="lines"/> (the summary part, see <see cref="ResumeText.SummaryPart"/>),
    /// one line each under the label the tail uses, within <paramref name="maxChars"/>. The oldest text is left out first, with the summary's notice.
    /// </summary>
    public static string SummaryInput(IReadOnlyList<TranscriptLine> lines, string assistant, int maxChars = SummaryInputBuilder.MaxInputChars) =>
        SummaryInputBuilder.Build(lines.Select(line => Render([line], assistant)).Where(text => text.Length > 0), maxChars);

    /// <summary>The label of a message: <c>you: </c> for the owner, and <c>{assistant}: </c> for the CLI. Any other kind is not in the tail.</summary>
    private static string? RenderLine(TranscriptLine line, string assistant)
    {
        if (line.Kind == "you") return "you: " + line.Text;
        return line.Kind == assistant ? assistant + ": " + line.Text : null;
    }
}

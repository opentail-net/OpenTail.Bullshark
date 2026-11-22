using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The start text of a new Copilot session that continues an earlier one (G1.8). A copy of <see cref="ResumeText"/> and the Codex resume text, with
/// Copilot's wording and labels: the last turns are copied word for word (lines of <c>you: </c> and <c>copilot: </c>, messages only), within a token
/// cap and an optional character cap, and the summary, when there is one, is capped as Claude's is. The token and character limits are Claude's
/// constants. There are no injected-context prefixes here: the Codex prefixes do not occur in Copilot's data (G1.8, C11). The shared parts are in
/// <see cref="ProviderResumeText"/> (step R4); this class gives them Copilot's phrase and its message label.
/// </summary>
public static class CopilotResumeText
{
    /// <summary>
    /// The turns of a session, oldest first. A turn starts at each <c>you</c> line; lines before the first one form a turn of their own.
    /// </summary>
    public static List<List<TranscriptLine>> SplitTurns(IReadOnlyList<TranscriptLine> lines) => ProviderResumeText.SplitTurns(lines);

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
        int maxChars = int.MaxValue) =>
        ProviderResumeText.Tail(lines, "copilot", turns, maxTokens, maxChars);

    /// <summary>
    /// The text the resumed session starts from: the line that names the session it continues (when <paramref name="sourceSessionId"/> is set), a
    /// header in Copilot's words, the summary when there is one, the verbatim tail, and the closing line. Its newlines are the ones
    /// <see cref="ResumeText.Compose"/> uses.
    /// </summary>
    public static string Compose(string? summary, SummaryLevel? level, string tail, int turns, string? sourceSessionId = null) =>
        ProviderResumeText.Compose("GitHub Copilot CLI", summary, level, tail, turns, sourceSessionId);
}

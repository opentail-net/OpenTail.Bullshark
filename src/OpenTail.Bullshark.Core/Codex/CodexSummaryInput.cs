using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The conversation a Codex summary reads (S1, decisions S-D3 and S-D7). It is built from a rollout's lines: only the owner's messages (<c>you: </c>)
/// and Codex's replies (<c>codex: </c>) are kept, one per message, and tool calls and results are left out, as Claude's
/// <c>ResumeText.SummaryInput</c> does. Injected context at the start of a session is left out too (R-D5). A line that is not JSON, or not a message, is skipped. When the text is over the cap, the oldest
/// messages go first, the newest message is always kept (cut in its middle if it alone is over the cap), and the summary is told so with
/// <see cref="CutNotice"/>. The result is never longer than the cap.
/// </summary>
public static class CodexSummaryInput
{
    /// <summary>The line put at the top when the start of the session is left out.</summary>
    public const string CutNotice = "(The start of this session is left out: it is longer than the summary reads.)";

    /// <summary>The blank line between messages, and between the notice and the messages.</summary>
    private const string Separator = "\n\n";

    /// <summary>
    /// The messages of the rollout as one text, within <paramref name="maxChars"/> characters. The lines are the rollout's lines, verbatim; an
    /// overlong line should be left out by the caller (it has no text here). Throws <see cref="ArgumentOutOfRangeException"/> when the cap is too
    /// small to hold the notice.
    /// </summary>
    public static string Build(IEnumerable<string> rolloutLines, int maxChars = CodexSummarizer.MaxInputChars)
    {
        if (maxChars <= CutNotice.Length + Separator.Length)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "The cap is too small to hold the notice.");

        var messages = new List<string>();
        foreach (var line in rolloutLines)
        {
            if (Rendered(line) is { } message) messages.Add(message);
        }

        var whole = string.Join(Separator, messages);
        if (whole.Length <= maxChars) return whole;

        // Cut: the notice and its blank line are paid for first, then the newest messages are kept while they fit.
        var budget = maxChars - CutNotice.Length - Separator.Length;
        var kept = new List<string>();
        var size = 0;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var cost = messages[i].Length + (kept.Count > 0 ? Separator.Length : 0);
            if (size + cost > budget) break;
            kept.Add(messages[i]);
            size += cost;
        }
        kept.Reverse();

        // The newest message alone is over the budget: it is kept, cut in its middle (its start and its end stay), so the latest text is never lost.
        var body = kept.Count > 0 ? string.Join(Separator, kept) : ResumeText.CutMiddle(messages[^1], budget);
        return CutNotice + Separator + body;
    }

    /// <summary>
    /// The line as <c>you: </c> or <c>codex: </c> followed by its text (read by <see cref="CodexMessages.ReadMessage"/>), or null when the line is
    /// not a message that is kept.
    /// </summary>
    internal static string? Rendered(string line) =>
        CodexMessages.ReadMessage(line) is { } message ? message.Label + ": " + message.Text : null;
}

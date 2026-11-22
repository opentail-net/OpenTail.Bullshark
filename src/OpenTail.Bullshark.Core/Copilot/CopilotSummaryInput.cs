using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The conversation a Copilot summary reads (G1.4, S-D3, S-D7): the session's messages as <c>you: </c> and <c>copilot: </c> lines, one per message,
/// with a blank line between them. When the text is over the cap, the oldest messages go first, the newest message is always kept (cut in its middle
/// if it alone is over the cap), and the summary is told so with <see cref="CutNotice"/>. The result is never longer than the cap.
/// </summary>
public static class CopilotSummaryInput
{
    /// <summary>The most transcript text one summary reads, in characters (S-D3). The oldest text goes first.</summary>
    public const int MaxInputChars = 120_000;

    /// <summary>The line put at the top when the start of the session is left out.</summary>
    public const string CutNotice = "(The start of this session is left out: it is longer than the summary reads.)";

    /// <summary>The blank line between messages, and between the notice and the messages.</summary>
    private const string Separator = "\n\n";

    /// <summary>
    /// The messages as one text, within <paramref name="maxChars"/> characters. Throws <see cref="ArgumentOutOfRangeException"/> when the cap is too
    /// small to hold the notice.
    /// </summary>
    public static string Build(IEnumerable<TranscriptLine> messages, int maxChars = MaxInputChars)
    {
        if (maxChars <= CutNotice.Length + Separator.Length)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "The cap is too small to hold the notice.");

        var rendered = messages.Select(CopilotTranscriptReader.Render).ToList();
        var whole = string.Join(Separator, rendered);
        if (whole.Length <= maxChars) return whole;

        // Cut: the notice and its blank line are paid for first, then the newest messages are kept while they fit.
        var budget = maxChars - CutNotice.Length - Separator.Length;
        var kept = new List<string>();
        var size = 0;
        for (var i = rendered.Count - 1; i >= 0; i--)
        {
            var cost = rendered[i].Length + (kept.Count > 0 ? Separator.Length : 0);
            if (size + cost > budget) break;
            kept.Add(rendered[i]);
            size += cost;
        }
        kept.Reverse();

        // The newest message alone is over the budget: it is kept, cut in its middle, so the latest text is never lost.
        var body = kept.Count > 0 ? string.Join(Separator, kept) : ResumeText.CutMiddle(rendered[^1], budget);
        return CutNotice + Separator + body;
    }
}

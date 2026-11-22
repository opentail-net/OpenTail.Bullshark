using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Providers.Summary;

/// <summary>
/// The conversation a summary reads, built from the messages each provider has rendered (provider kit R2). The cap is 120,000 characters (S-D3). When
/// the messages are over the cap, the oldest go first, the newest message is always kept (cut in its middle if it alone is over the cap), and the
/// summary is told so with <see cref="CutNotice"/>. The result is never longer than the cap. Each provider reads and renders its own messages; this
/// class only cuts and joins them.
/// </summary>
public static class SummaryInputBuilder
{
    /// <summary>The most transcript text one summary reads, in characters (S-D3). The oldest text goes first.</summary>
    public const int MaxInputChars = 120_000;

    /// <summary>The line put at the top when the start of the session is left out.</summary>
    public const string CutNotice = "(The start of this session is left out: it is longer than the summary reads.)";

    /// <summary>The blank line between messages, and between the notice and the messages.</summary>
    private const string Separator = "\n\n";

    /// <summary>
    /// The rendered messages as one text, within <paramref name="maxChars"/> characters. The cap is checked before the messages are read, so a cap
    /// too small for the notice throws <see cref="ArgumentOutOfRangeException"/> without reading anything.
    /// </summary>
    public static string Build(IEnumerable<string> renderedMessages, int maxChars = MaxInputChars)
    {
        if (maxChars <= CutNotice.Length + Separator.Length)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "The cap is too small to hold the notice.");

        var messages = renderedMessages.ToList();
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
}

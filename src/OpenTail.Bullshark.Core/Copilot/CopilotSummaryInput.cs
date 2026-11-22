using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Providers.Summary;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The conversation a Copilot summary reads (G1.4, S-D3, S-D7): the session's messages as <c>you: </c> and <c>copilot: </c> lines, one per message,
/// with a blank line between them. The cap and the cut are <see cref="SummaryInputBuilder"/>'s (provider kit R2): when the text is over the cap, the
/// oldest messages go first, the newest message is always kept, and the summary is told so with <see cref="CutNotice"/>.
/// </summary>
public static class CopilotSummaryInput
{
    /// <summary>The most transcript text one summary reads, in characters (S-D3). The oldest text goes first.</summary>
    public const int MaxInputChars = SummaryInputBuilder.MaxInputChars;

    /// <summary>The line put at the top when the start of the session is left out.</summary>
    public const string CutNotice = SummaryInputBuilder.CutNotice;

    /// <summary>
    /// The messages as one text, within <paramref name="maxChars"/> characters. Throws <see cref="ArgumentOutOfRangeException"/> when the cap is too
    /// small to hold the notice.
    /// </summary>
    public static string Build(IEnumerable<TranscriptLine> messages, int maxChars = MaxInputChars) =>
        SummaryInputBuilder.Build(messages.Select(CopilotTranscriptReader.Render), maxChars);
}

using OpenTail.Bullshark.Core.Providers.Summary;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The conversation a Codex summary reads (S1, decisions S-D3 and S-D7). It is built from a rollout's lines: only the owner's messages (<c>you: </c>)
/// and Codex's replies (<c>codex: </c>) are kept, one per message, and tool calls and results are left out, as Claude's
/// <c>ResumeText.SummaryInput</c> does. Injected context at the start of a session is left out too (R-D5). A line that is not JSON, or not a message, is skipped. The cut
/// and the cap are <see cref="SummaryInputBuilder"/>'s (provider kit R2); this class reads and renders the Codex messages.
/// </summary>
public static class CodexSummaryInput
{
    /// <summary>The line put at the top when the start of the session is left out.</summary>
    public const string CutNotice = SummaryInputBuilder.CutNotice;

    /// <summary>
    /// The messages of the rollout as one text, within <paramref name="maxChars"/> characters. The lines are the rollout's lines, verbatim; an
    /// overlong line should be left out by the caller (it has no text here). Throws <see cref="ArgumentOutOfRangeException"/> when the cap is too
    /// small to hold the notice.
    /// </summary>
    public static string Build(IEnumerable<string> rolloutLines, int maxChars = CodexSummarizer.MaxInputChars) =>
        SummaryInputBuilder.Build(rolloutLines.Select(Rendered).OfType<string>(), maxChars);

    /// <summary>
    /// The line as <c>you: </c> or <c>codex: </c> followed by its text (read by <see cref="CodexMessages.ReadMessage"/>), or null when the line is
    /// not a message that is kept.
    /// </summary>
    internal static string? Rendered(string line) =>
        CodexMessages.ReadMessage(line) is { } message ? message.Label + ": " + message.Text : null;
}

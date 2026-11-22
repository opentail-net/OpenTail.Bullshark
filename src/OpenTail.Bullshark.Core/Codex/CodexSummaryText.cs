using OpenTail.Bullshark.Core.Providers.Summary;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The instructions of a Codex summary (S-D4). The word limits are the same as <see cref="ResumeText.Instructions"/> (120, 400 and 1,200 words);
/// the wording says Codex CLI session, as the Claude side says Claude Code session. The text is written by <see cref="SummaryInstructions"/>.
/// </summary>
public static class CodexSummaryText
{
    /// <summary>The instruction for a summary of each level.</summary>
    public static string Instructions(SummaryLevel level) => SummaryInstructions.For(level, "Codex CLI session");
}

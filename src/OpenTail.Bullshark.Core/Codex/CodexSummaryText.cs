using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The instructions of a Codex summary (S-D4). The word limits are the same as <see cref="ResumeText.Instructions"/> (120, 400 and 1,200 words);
/// the wording says Codex CLI session, as the Claude side says Claude Code session.
/// </summary>
public static class CodexSummaryText
{
    /// <summary>The instruction for a summary of each level.</summary>
    public static string Instructions(SummaryLevel level) => level switch
    {
        SummaryLevel.Minimal =>
            "Summarise this Codex CLI session in at most 120 words: the goal, what was done, and what is still open. " +
            "Plain text, no headings. Do not invent anything; say when something is unknown.",
        SummaryLevel.Normal =>
            "Summarise this Codex CLI session in at most 400 words: the goal, the decisions and why they were made, " +
            "the files changed, the current state, and the open questions. Plain text, with short lists where they help. " +
            "Do not invent anything; say when something is unknown.",
        SummaryLevel.Complete =>
            "Summarise this Codex CLI session in at most 1,200 words, in order: the goals, each decision with its reason, " +
            "every file changed, the tests and their results, the errors and how they were fixed, the current state, the " +
            "open questions, and the next steps. Plain text with headings. Do not invent anything; say when something is unknown.",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}

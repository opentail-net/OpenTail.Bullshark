using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Providers.Summary;

/// <summary>
/// The instruction of a summary at each level (S-D4; provider kit R2). The word limits are the sizes the decisions name: 120, 400 and 1,200 words.
/// The texts are the same for every CLI except the name of the session, so one function writes them and each provider passes its own phrase.
/// </summary>
public static class SummaryInstructions
{
    /// <summary>
    /// The instruction for a summary of <paramref name="level"/>. <paramref name="cliPhrase"/> is the session's name as the text says it, in full:
    /// "Codex CLI session" or "GitHub Copilot CLI session". Throws <see cref="ArgumentOutOfRangeException"/> for a level that is not defined.
    /// </summary>
    public static string For(SummaryLevel level, string cliPhrase) => level switch
    {
        SummaryLevel.Minimal =>
            "Summarise this " + cliPhrase + " in at most 120 words: the goal, what was done, and what is still open. " +
            "Plain text, no headings. Do not invent anything; say when something is unknown.",
        SummaryLevel.Normal =>
            "Summarise this " + cliPhrase + " in at most 400 words: the goal, the decisions and why they were made, " +
            "the files changed, the current state, and the open questions. Plain text, with short lists where they help. " +
            "Do not invent anything; say when something is unknown.",
        SummaryLevel.Complete =>
            "Summarise this " + cliPhrase + " in at most 1,200 words, in order: the goals, each decision with its reason, " +
            "every file changed, the tests and their results, the errors and how they were fixed, the current state, the " +
            "open questions, and the next steps. Plain text with headings. Do not invent anything; say when something is unknown.",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}

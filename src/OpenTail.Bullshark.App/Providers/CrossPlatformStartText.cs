using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Setup;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The start text of a new session in another platform than the session it is made from (resume source and target plan, S3b-A). Only a summary
/// starts in another platform (RS-D2). The new session is not a continuation of the source session (RS-D5): its text names the source platform in
/// its first line and has no session line, and then the summary and the tail (RS-D6). The tail keeps the target's limits, as the target's own tail
/// does. The source's messages keep the source's label: the tail takes the source's kind, so it keeps them, and no label is changed.
/// Pure: it reads no file, starts no process and makes no model call. Same-platform starts do not come here.
/// </summary>
public static class CrossPlatformStartText
{
    /// <summary>The words a refused start says when the start text does not fit the Claude command line, even with its latest turn cut (RS-D11).</summary>
    public const string TooLongMessage = "the start text is too long for the command line, even with its latest turn cut.";

    /// <summary>The CLI phrase a platform's start text names. Claude's is the phrase <see cref="ResumeText.Compose"/> writes.</summary>
    public static string PhraseOf(ProviderId platform) =>
        platform == ProviderId.Claude ? "Claude Code"
        : platform == ProviderId.Codex ? "Codex CLI"
        : platform == ProviderId.Copilot ? "GitHub Copilot CLI"
        : platform == ProviderId.OpenCode ? "opencode"
        : throw new ArgumentOutOfRangeException(nameof(platform));

    /// <summary>The kind a platform's messages carry in the transcript lines, which the platform's tail keeps.</summary>
    private static string MessageKindOf(ProviderId platform) =>
        platform == ProviderId.Claude ? "claude"
        : platform == ProviderId.Codex ? "codex"
        : platform == ProviderId.Copilot ? "copilot"
        : platform == ProviderId.OpenCode ? "opencode"
        : throw new ArgumentOutOfRangeException(nameof(platform));

    /// <summary>
    /// The start text of a cross-platform start: the header that names <paramref name="source"/>, the summary at <paramref name="level"/>, and the last
    /// <paramref name="turns"/> turns of the source, copied with each message under the label the source wrote it (the shared provider tail keeps the
    /// source's kind, so a Claude reply reads "claude:" in a Codex start). A Claude target is fitted to the Claude command line
    /// (<see cref="ResumeText.FitWith"/>, with <paramref name="commandLineOf"/> measuring the command line the start would make, and
    /// <see cref="ClaudeCli.MaxLaunchCharacters"/> as the limit). A Codex or a Copilot target takes no fit: its start goes through a start file (RS-D11).
    /// <see cref="ProviderResumeText.ComposeFrom"/> throws on a summary without its level, and on a Zero start (no summary) with no tail (RS-D2, RS-D25).
    /// </summary>
    public static ResumeText.FittedStart Build(
        ProviderId source, ProviderId target, string? summary, SummaryLevel? level,
        IReadOnlyList<TranscriptLine> lines, int turns, Func<string, int>? commandLineOf = null)
    {
        if (source == target) throw new ArgumentException("A cross-platform start goes to another platform than its source.", nameof(target));
        var sourcePhrase = PhraseOf(source);
        var targetPhrase = PhraseOf(target);
        if (target == ProviderId.Claude)
        {
            ArgumentNullException.ThrowIfNull(commandLineOf);
            return ResumeText.FitWith(
                summary, level, lines, turns, string.Empty, commandLineOf, ClaudeCli.MaxLaunchCharacters,
                (turnLines, keep, maxChars) => ProviderResumeText.TailWithTurns(turnLines, MessageKindOf(source), keep, maxChars: maxChars),
                (text, shown, tail, keep, _) => ProviderResumeText.ComposeFrom(targetPhrase, sourcePhrase, text, shown, tail, keep));
        }

        var tail = ProviderResumeText.Tail(lines, MessageKindOf(source), turns);
        var text = ProviderResumeText.ComposeFrom(targetPhrase, sourcePhrase, summary, level, tail, turns);
        return new ResumeText.FittedStart(text, 0, Fits: true);
    }

    /// <summary>
    /// The model and effort a cross-platform start into Claude takes (RS-D12): the planner model and effort the Claude resume screen selects when it
    /// opens. That screen takes them from the saved solo pattern, or from the defaults when none is saved (<see cref="PatternStore.For"/>), so the
    /// argument is the same <c>SavedSolo()</c> the screen reads. Codex and Copilot take the CLI default (no model).
    /// </summary>
    public static (string Model, string Effort) ClaudeDefaults(PatternSettings solo) => (solo.PlannerModel, solo.PlannerEffort);

    /// <summary>
    /// The refusal that ends a cross-platform attempt before its summary call (RS-D13), or null when the attempt may go on. The mode and the Copilot rule
    /// come first, through <see cref="ResumeTarget.RefusalReason"/> (RS-D17); the target's own check runs only when they allow it, so a refused mode
    /// costs no check. <paramref name="targetCheck"/> is the target's launch and folder check, and it returns a refusal or null.
    /// </summary>
    public static async Task<string?> RefusalBeforeSummaryAsync(
        ProviderId source, ProviderId target, ContinueMode mode, Func<Task<string?>> targetCheck)
    {
        var refusal = ResumeTarget.RefusalReason(source, mode, target);
        return refusal ?? await targetCheck();
    }
}

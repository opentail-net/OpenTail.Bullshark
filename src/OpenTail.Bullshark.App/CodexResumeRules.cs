using System.Globalization;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The rules of the Codex resume screen (steps R5 and R7, decisions R-D2 and R-D3): the labels of its modes and levels, the cost line of each mode,
/// the turns check, the size line of a preview, and the rule that turns Start on. Pure, so the tests need no window; the screen's code-behind only
/// shows what these return.
/// </summary>
public static class CodexResumeRules
{
    /// <summary>The most turns the screen accepts. The same limit as Claude's resume prompt.</summary>
    public const int MaxTurns = 200;

    /// <summary>The turns kept when the box is not a valid number. The same default as Claude's resume prompt.</summary>
    public const int DefaultTurns = ResumeText.DefaultTurns;

    /// <summary>The cost line of the two modes that make no model call from the summary path (the last turns, and In full, below).</summary>
    public const string NoModelCallLine = "No model call. The text is shown before anything starts.";

    /// <summary>
    /// The cost line of In full. The text of the other modes ("The text is shown before anything starts") does not fit it: In full sends no text,
    /// it only runs <c>codex resume</c> in a terminal tab.
    /// </summary>
    public const string FullCostLine = "No model call from OpenTail. The session opens in a terminal tab with codex resume, and OpenTail sends it no text.";

    /// <summary>The line under the turns box when the box is not a valid number.</summary>
    public static readonly string TurnsMessage = $"Enter a whole number from 0 to {MaxTurns} for the turns.";

    /// <summary>The label of a mode, as its radio button shows it.</summary>
    public static string ModeLabel(CodexResumeMode mode) => mode switch
    {
        CodexResumeMode.Full => "In full: the whole conversation, with codex resume",
        CodexResumeMode.LastTurns => "From the last turns (no model call)",
        CodexResumeMode.Summary => "From a Codex summary (one Codex call)",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The cost line of a mode. The summary line names the model and the cap on the characters sent (both are the Core's constants, so the screen
    /// says what the call does).
    /// </summary>
    public static string CostLine(CodexResumeMode mode) => mode switch
    {
        CodexResumeMode.Full => FullCostLine,
        CodexResumeMode.LastTurns => NoModelCallLine,
        CodexResumeMode.Summary =>
            $"One Codex call on {CodexSummarizer.DefaultModel}. Up to {CodexSummarizer.MaxInputChars:N0} characters of this session are sent.",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The label of a summary level, with its length in words.</summary>
    public static string LevelLabel(SummaryLevel level) => level switch
    {
        SummaryLevel.Minimal => "Minimal · about 120 words",
        SummaryLevel.Normal => "Normal · about 400 words",
        SummaryLevel.Complete => "Complete · about 1,200 words",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>
    /// The turns in the box: a whole number from 0 to <see cref="MaxTurns"/>. Returns false, with <see cref="DefaultTurns"/>, when it is not one.
    /// </summary>
    public static bool TryTurns(string text, out int turns)
    {
        var parsed = int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out turns);
        if (parsed && turns is >= 0 and <= MaxTurns) return true;
        turns = DefaultTurns;
        return false;
    }

    /// <summary>
    /// True when Start may be pressed. In full is always allowed. From the last turns needs a preview for the current choice. From a summary needs a
    /// preview and a summary kept for the current level (<paramref name="keptMatches"/>): a summary is never made by Start.
    /// </summary>
    public static bool CanStart(CodexResumeMode mode, bool previewReady, bool keptMatches) => mode switch
    {
        CodexResumeMode.Full => true,
        CodexResumeMode.LastTurns => previewReady,
        CodexResumeMode.Summary => previewReady && keptMatches,
        _ => false,
    };

    /// <summary>
    /// The size line above a preview: "About N tokens (C characters). Start sends exactly this text." The tokens are the characters over four, rounded
    /// (as Claude's preview counts them).
    /// </summary>
    public static string SizeLine(int characters)
    {
        var tokens = (int)Math.Round(characters / 4.0, MidpointRounding.AwayFromZero);
        return $"About {tokens.ToString("N0", CultureInfo.InvariantCulture)} tokens ({characters.ToString("N0", CultureInfo.InvariantCulture)} characters). Start sends exactly this text.";
    }
}

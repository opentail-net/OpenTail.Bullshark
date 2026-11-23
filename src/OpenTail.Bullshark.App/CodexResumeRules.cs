using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The rules of the Codex resume screen (steps R5 and R7, decisions R-D2 and R-D3). The rules are written once in <see cref="ProviderResumeRules"/>;
/// this class holds Codex's words and maps its mode to the shared one. The model step is Codex's own and is not in the shared rules. Pure, so the
/// tests need no window; the screen's code-behind only shows what these return.
/// </summary>
public static class CodexResumeRules
{
    /// <summary>Codex's words: the name, the resume command, the shim, and the summary's cost line (it names the model and the cap on the characters sent).</summary>
    private static readonly ProviderResumeWording Wording = new(
        CliName: "Codex",
        ResumeCommand: "codex resume",
        ShimName: "codex.ps1",
        SummaryCostLine: () => $"One Codex call on {CodexSummarizer.DefaultModel}. Up to {CodexSummarizer.MaxInputChars:N0} characters of this session are sent.");

    /// <summary>The most turns the screen accepts. The same limit as Claude's resume prompt.</summary>
    public const int MaxTurns = ProviderResumeRules.MaxTurns;

    /// <summary>The turns kept when the box is not a valid number. The same default as Claude's resume prompt.</summary>
    public const int DefaultTurns = ProviderResumeRules.DefaultTurns;

    /// <summary>The cost line of the two modes that make no model call from the summary path (the last turns, and In full, below).</summary>
    public const string NoModelCallLine = ProviderResumeRules.NoModelCallLine;

    /// <summary>The cost line of In full: it runs <c>codex resume</c> in a terminal tab, and OpenTail sends no text.</summary>
    public static readonly string FullCostLine = ProviderResumeRules.FullCostLine(Wording);

    /// <summary>The line under the turns box when the box is not a valid number.</summary>
    public static readonly string TurnsMessage = ProviderResumeRules.TurnsMessage;

    /// <summary>
    /// The status line of a start when codex.ps1 is not on PATH: the start runs the shim, and a codex.exe alone cannot take the text. The text is in the
    /// preview, so the owner can copy it and start Codex by hand.
    /// </summary>
    public const string NoShimOnPath =
        "Not started: codex.ps1 is not on PATH, so a start from text cannot run.";

    /// <summary>The shared mode of one of Codex's modes, or null for a value Codex does not have.</summary>
    private static ProviderResumeMode? ToShared(CodexResumeMode mode) => mode switch
    {
        CodexResumeMode.Full => ProviderResumeMode.Full,
        CodexResumeMode.LastTurns => ProviderResumeMode.LastTurns,
        CodexResumeMode.Summary => ProviderResumeMode.Summary,
        _ => null,
    };

    /// <summary>The label of a mode, as its radio button shows it.</summary>
    public static string ModeLabel(CodexResumeMode mode) =>
        ProviderResumeRules.ModeLabel(ToShared(mode) ?? throw new ArgumentOutOfRangeException(nameof(mode)), Wording);

    /// <summary>
    /// The cost line of a mode. The summary line names the model and the cap on the characters sent (both are the Core's constants, so the screen
    /// says what the call does).
    /// </summary>
    public static string CostLine(CodexResumeMode mode) =>
        ProviderResumeRules.CostLine(ToShared(mode) ?? throw new ArgumentOutOfRangeException(nameof(mode)), Wording);

    /// <summary>The label of a summary level, with its length in words.</summary>
    public static string LevelLabel(SummaryLevel level) => ProviderResumeRules.LevelLabel(level);

    /// <summary>
    /// The turns in the box: a whole number from 0 to <see cref="MaxTurns"/>. Returns false, with <see cref="DefaultTurns"/>, when it is not one.
    /// </summary>
    public static bool TryTurns(string text, out int turns) => ProviderResumeRules.TryTurns(text, out turns);

    /// <summary>
    /// True when Start may be pressed. In full is always allowed. From the last turns needs a preview for the current choice. From a summary needs a
    /// preview and a summary kept for the current level (<paramref name="keptMatches"/>): a summary is never made by Start.
    /// </summary>
    public static bool CanStart(CodexResumeMode mode, bool previewReady, bool keptMatches) =>
        ToShared(mode) is { } shared && ProviderResumeRules.CanStart(shared, previewReady, keptMatches);

    /// <summary>True when the preview of a choice is built at once, with no press: the last turns, or a kept summary for the level on screen.</summary>
    public static bool RunsAtOnce(CodexResumeMode mode, bool keptMatches) =>
        ToShared(mode) is { } shared && ProviderResumeRules.RunsAtOnce(shared, keptMatches);

    /// <summary>
    /// The size line above a preview: "About N tokens (C characters). Start sends exactly this text." The tokens are the characters over four, rounded
    /// (as Claude's preview counts them).
    /// </summary>
    public static string SizeLine(int characters) => ProviderResumeRules.SizeLine(characters);
}

using System.Globalization;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The rules of the Copilot resume screen (step G3.1, copied from the Codex rules R-D2 and R-D3). The rules are written once in
/// <see cref="ProviderResumeRules"/>; this class holds Copilot's words and maps its mode to the shared one. The refusals (G-D12) and the started line
/// (G3.2) are the shared rules with Copilot's texts. Pure, so the tests need no window; the screen's code-behind only shows what these return.
/// </summary>
public static class CopilotResumeRules
{
    /// <summary>Copilot's words: the name, the resume command, the shim, and the summary's cost line (the cap on the characters sent).</summary>
    internal static readonly ProviderResumeWording Wording = new(
        CliName: "Copilot",
        ResumeCommand: "copilot --resume",
        ShimName: "copilot.ps1",
        SummaryCostLine: () => $"One Copilot call. Up to {CopilotSummarizer.MaxInputChars.ToString("N0", CultureInfo.InvariantCulture)} characters of this session are sent.");

    /// <summary>The most turns the screen accepts. The same limit as Claude's and Codex's resume screens.</summary>
    public const int MaxTurns = ProviderResumeRules.MaxTurns;

    /// <summary>The turns kept when the box is not a valid number. The same default as Claude's resume prompt.</summary>
    public const int DefaultTurns = ProviderResumeRules.DefaultTurns;

    /// <summary>The cost line of In full: it runs <c>copilot --resume</c> in a terminal tab, and OpenTail sends no text.</summary>
    public static readonly string FullCostLine = ProviderResumeRules.FullCostLine(Wording);

    /// <summary>The line shown while a summary is made, when another press would start a second call (G3.2): it waits for the first.</summary>
    public const string SummaryRunning = "A Copilot summary is already being made. Wait for it to finish.";

    /// <summary>
    /// The status line of a start when copilot.ps1 is not on PATH: the start runs the shim, and copilot.exe alone cannot take the text.
    /// </summary>
    public const string NoShimOnPath =
        "Not started: copilot.ps1 is not on PATH, so a start from text cannot run. Only copilot.exe was found.";

    /// <summary>
    /// The line when a summary would be made by a window that does not hold the single-instance guard for the state file. The start-up sweep of the scratch
    /// folders runs only in the window that holds the guard, so this window's summary folder could be removed by another window; the summary is refused.
    /// </summary>
    public const string SummaryNeedsGuard =
        "Summaries are not made from this window: it does not hold the lock for this state file, so another window may be using its scratch folders. Open the session from the window that holds the lock.";

    /// <summary>The line under the turns box when the box is not a valid number.</summary>
    public static readonly string TurnsMessage = ProviderResumeRules.TurnsMessage;

    /// <summary>The shared mode of one of Copilot's modes, or null for a value Copilot does not have.</summary>
    internal static ProviderResumeMode? ToShared(CopilotResumeMode mode) => mode switch
    {
        CopilotResumeMode.Full => ProviderResumeMode.Full,
        CopilotResumeMode.LastTurns => ProviderResumeMode.LastTurns,
        CopilotResumeMode.Summary => ProviderResumeMode.Summary,
        _ => null,
    };

    /// <summary>Copilot's own mode for a shared one (the presenter works on the shared modes).</summary>
    internal static CopilotResumeMode FromShared(ProviderResumeMode mode) => mode switch
    {
        ProviderResumeMode.Full => CopilotResumeMode.Full,
        ProviderResumeMode.LastTurns => CopilotResumeMode.LastTurns,
        ProviderResumeMode.Summary => CopilotResumeMode.Summary,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The cost line of a mode. The summary line states the cap on the characters sent (the Core's constant, so the screen says what the call does).
    /// </summary>
    public static string CostLine(CopilotResumeMode mode) =>
        ProviderResumeRules.CostLine(ToShared(mode) ?? throw new ArgumentOutOfRangeException(nameof(mode)), Wording);

    /// <summary>The label of a summary level, with its length in words.</summary>
    public static string LevelLabel(SummaryLevel level) => ProviderResumeRules.LevelLabel(level);

    /// <summary>
    /// The turns in the box: a whole number from 0 to <see cref="MaxTurns"/>. Returns false, with <see cref="DefaultTurns"/>, when it is not one.
    /// </summary>
    public static bool TryTurns(string text, out int turns) => ProviderResumeRules.TryTurns(text, out turns);

    /// <summary>
    /// The size line above a preview: "About N tokens (C characters). Start sends exactly this text." The tokens are the characters over four, rounded
    /// (as Claude's preview counts them).
    /// </summary>
    public static string SizeLine(int characters) => ProviderResumeRules.SizeLine(characters);

    /// <summary>
    /// The refusal of a resume or a start (G-D12), or null when it may go on: the id must be in the app's own list of Copilot sessions, and the session's
    /// folder must exist. There is no silent fallback to another session.
    /// </summary>
    /// <param name="listed">True when the id is in the list the rail shows.</param>
    /// <param name="folder">The session's folder as the history gives it (may be null or blank).</param>
    /// <param name="folderExists">True when the folder exists on disk.</param>
    public static string? RefusalFor(bool listed, string? folder, bool folderExists) =>
        ProviderResumeRules.RefusalFor(listed, folder, folderExists, CopilotRailText.NotListed, CopilotRailText.FolderMissing);

    /// <summary>
    /// The status line after a start (G3.2): what the new session starts from, and that it makes a model turn. The line also says where the start text is
    /// kept when the launch refuses (the Core script keeps the file and writes that line into the tab).
    /// </summary>
    public static string StartedLine(SummaryLevel? level, int turns) => ProviderResumeRules.StartedLine(Wording, level, turns);
}

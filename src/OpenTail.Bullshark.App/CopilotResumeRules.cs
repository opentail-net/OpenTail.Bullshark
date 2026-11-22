using System.Globalization;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The rules of the Copilot resume screen (step G3.1, copied from the Codex rules R-D2 and R-D3): the labels of its modes and levels, the cost line of
/// each mode, the turns check, the size line of a preview, the rule that turns Start on, and the refusals and status lines of a resume (G-D12, G-D14).
/// Pure, so the tests need no window; the screen's code-behind only shows what these return.
/// </summary>
public static class CopilotResumeRules
{
    /// <summary>The most turns the screen accepts. The same limit as Claude's and Codex's resume screens.</summary>
    public const int MaxTurns = 200;

    /// <summary>The turns kept when the box is not a valid number. The same default as Claude's resume prompt.</summary>
    public const int DefaultTurns = ResumeText.DefaultTurns;

    /// <summary>The cost line of the mode that makes no model call from the summary path (the last turns).</summary>
    public const string NoModelCallLine = "No model call. The text is shown before anything starts.";

    /// <summary>
    /// The cost line of In full. The text of the other modes ("The text is shown before anything starts") does not fit it: In full sends no text,
    /// it only runs <c>copilot --resume</c> in a terminal tab.
    /// </summary>
    public const string FullCostLine = "No model call from OpenTail. The session opens in a terminal tab with copilot --resume, and OpenTail sends it no text.";

    /// <summary>The line shown while a summary is made, when another press would start a second call (G3.2): it waits for the first.</summary>
    public const string SummaryRunning = "A Copilot summary is already being made. Wait for it to finish.";

    /// <summary>
    /// The status line of a start when copilot.ps1 is not on PATH: the start runs the shim, and copilot.exe alone cannot take the text. The text is in the
    /// preview, so the owner can copy it and start Copilot by hand.
    /// </summary>
    public const string NoShimOnPath =
        "Not started: copilot.ps1 is not on PATH, so a start from text cannot run. Only copilot.exe was found. The start text is in the preview: use Copy and start Copilot yourself.";

    /// <summary>
    /// The line when a summary would be made by a window that does not hold the single-instance guard for the state file. The start-up sweep of the scratch
    /// folders runs only in the window that holds the guard, so this window's summary folder could be removed by another window; the summary is refused.
    /// </summary>
    public const string SummaryNeedsGuard =
        "Summaries are not made from this window: it does not hold the lock for this state file, so another window may be using its scratch folders. Open the session from the window that holds the lock.";

    /// <summary>The line under the turns box when the box is not a valid number.</summary>
    public static readonly string TurnsMessage = $"Enter a whole number from 0 to {MaxTurns} for the turns.";

    /// <summary>The label of a mode, as its radio button shows it.</summary>
    public static string ModeLabel(CopilotResumeMode mode) => mode switch
    {
        CopilotResumeMode.Full => "In full: the whole conversation, with copilot --resume",
        CopilotResumeMode.LastTurns => "From the last turns (no model call)",
        CopilotResumeMode.Summary => "From a Copilot summary (one Copilot call)",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The cost line of a mode. The summary line states the cap on the characters sent (the Core's constant, so the screen says what the call does).
    /// </summary>
    public static string CostLine(CopilotResumeMode mode) => mode switch
    {
        CopilotResumeMode.Full => FullCostLine,
        CopilotResumeMode.LastTurns => NoModelCallLine,
        CopilotResumeMode.Summary =>
            $"One Copilot call. Up to {CopilotSummarizer.MaxInputChars.ToString("N0", CultureInfo.InvariantCulture)} characters of this session are sent.",
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
    public static bool CanStart(CopilotResumeMode mode, bool previewReady, bool keptMatches) => mode switch
    {
        CopilotResumeMode.Full => true,
        CopilotResumeMode.LastTurns => previewReady,
        CopilotResumeMode.Summary => previewReady && keptMatches,
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

    /// <summary>
    /// The refusal of a resume or a start (G-D12), or null when it may go on: the id must be in the app's own list of Copilot sessions, and the session's
    /// folder must exist. There is no silent fallback to another session.
    /// </summary>
    /// <param name="listed">True when the id is in the list the rail shows.</param>
    /// <param name="folder">The session's folder as the history gives it (may be null or blank).</param>
    /// <param name="folderExists">True when the folder exists on disk.</param>
    public static string? RefusalFor(bool listed, string? folder, bool folderExists)
    {
        if (!listed) return CopilotRailText.NotListed;
        if (folder is not { Length: > 0 } || !folderExists) return CopilotRailText.FolderMissing(folder);
        return null;
    }

    /// <summary>
    /// The status line after a start (G3.2): what the new session starts from, and that it makes a model turn. The line also says where the start text is
    /// kept when the launch refuses (the Core script keeps the file and writes that line into the tab).
    /// </summary>
    public static string StartedLine(SummaryLevel? level, int turns)
    {
        var how = level is { } chosen
            ? $"a Copilot summary ({chosen.ToString().ToLowerInvariant()}) and the last {turns} turns"
            : $"the last {turns} turns";
        return $"Started a Copilot session from {how}. It starts with a model turn. If the tab reports that copilot.ps1 is missing, the start text is kept in the Starts folder beside the state file.";
    }
}

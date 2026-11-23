using System.Globalization;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The mode of a resume screen, as the shared rules see it. Each provider keeps its own mode enum (the Codex and Copilot screens are public types with
/// their own names) and maps it to this one.
/// </summary>
public enum ProviderResumeMode
{
    /// <summary>The same session, with the CLI's resume command in a terminal tab. No text is sent by OpenTail.</summary>
    Full,

    /// <summary>A new session that starts from the last turns. No model call.</summary>
    LastTurns,

    /// <summary>A new session that starts from a summary made by one model call, then the last turns.</summary>
    Summary,
}

/// <summary>
/// The words that differ between the CLIs: the CLI's name, its resume command, its shim file (the script the start runs), and the cost line of the
/// summary. The summary's cost line is a function, so a provider's own formatting (Codex names its model) is evaluated at the time it is shown.
/// </summary>
public sealed record ProviderResumeWording(string CliName, string ResumeCommand, string ShimName, Func<string> SummaryCostLine);

/// <summary>
/// The rules of a provider's resume screen, written once: the modes, the levels, the turns, the cost lines, the Start rule, the size line, the rule
/// for a kept summary, and the refusal and the started line of a resume. A provider supplies only its words (<see cref="ProviderResumeWording"/>)
/// and its mode mapping. Pure, so the tests need no window.
/// </summary>
public static class ProviderResumeRules
{
    /// <summary>The most turns the screen accepts. The same limit as Claude's resume prompt.</summary>
    public const int MaxTurns = 200;

    /// <summary>The turns kept when the box is not a valid number. The same default as Claude's resume prompt.</summary>
    public const int DefaultTurns = ResumeText.DefaultTurns;

    /// <summary>The cost line of the mode that makes no model call from the summary path (the last turns).</summary>
    public const string NoModelCallLine = "No model call. The text is shown before anything starts.";

    /// <summary>The line under the turns box when the box is not a valid number.</summary>
    public static readonly string TurnsMessage = $"Enter a whole number from 0 to {MaxTurns} for the turns.";

    /// <summary>
    /// The cost line of In full. The text of the other modes ("The text is shown before anything starts") does not fit it: In full sends no text,
    /// it only runs the CLI's resume command in a terminal tab.
    /// </summary>
    public static string FullCostLine(ProviderResumeWording wording) =>
        $"No model call from OpenTail. The session opens in a terminal tab with {wording.ResumeCommand}, and OpenTail sends it no text.";

    /// <summary>The label of a mode, as its radio button shows it.</summary>
    public static string ModeLabel(ProviderResumeMode mode, ProviderResumeWording wording) => mode switch
    {
        ProviderResumeMode.Full => $"In full: the whole conversation, with {wording.ResumeCommand}",
        ProviderResumeMode.LastTurns => "From the last turns (no model call)",
        ProviderResumeMode.Summary => $"From a {wording.CliName} summary (one {wording.CliName} call)",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The cost line of a mode. The summary line is the provider's own (it states the model, if the provider names one, and the cap on the characters
    /// sent).
    /// </summary>
    public static string CostLine(ProviderResumeMode mode, ProviderResumeWording wording) => mode switch
    {
        ProviderResumeMode.Full => FullCostLine(wording),
        ProviderResumeMode.LastTurns => NoModelCallLine,
        ProviderResumeMode.Summary => wording.SummaryCostLine(),
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
    public static bool CanStart(ProviderResumeMode mode, bool previewReady, bool keptMatches) => mode switch
    {
        ProviderResumeMode.Full => true,
        ProviderResumeMode.LastTurns => previewReady,
        ProviderResumeMode.Summary => previewReady && keptMatches,
        _ => false,
    };

    /// <summary>
    /// True when the preview is built at once, with no button press: the last turns, or a summary kept for the level on screen. A summary is never
    /// made by a change of the choice; only a kept one is shown again.
    /// </summary>
    public static bool RunsAtOnce(ProviderResumeMode mode, bool keptMatches) =>
        mode == ProviderResumeMode.LastTurns || (mode == ProviderResumeMode.Summary && keptMatches);

    /// <summary>
    /// The mode a resume screen opens on, for the Continue mode it is given (stage C4). Null when no mode is given: the screen keeps its own start (CN-D2).
    /// Opening on a mode never makes a summary (CN-D3): the Summary mode only checks the summary radio, and the call stays with "Make summary".
    /// A stored last-turns mode reads as Summary (RS-D27): it opens on the summary at the Zero level with the tail on (<see cref="OpensAtZeroWithTail"/>),
    /// and makes no call.
    /// </summary>
    public static ProviderResumeMode? StartMode(ContinueMode? mode) => mode switch
    {
        ContinueMode.Full => ProviderResumeMode.Full,
        ContinueMode.LastTurns => ProviderResumeMode.Summary,
        ContinueMode.Summary => ProviderResumeMode.Summary,
        _ => null,
    };

    /// <summary>
    /// True when a stored mode opens at the Zero level with the tail on (RS-D27): only the old last-turns mode does. Zero is no summary, so it makes no
    /// model call; its turns are <see cref="StartTurns"/>.
    /// </summary>
    public static bool OpensAtZeroWithTail(ContinueMode? mode) => mode == ContinueMode.LastTurns;

    /// <summary>
    /// The turns a stored last-turns mode opens with (RS-D27): the remembered value when one is given and is a valid count, else <see cref="DefaultTurns"/>.
    /// No stored mark holds a turns value today, so a stored mode gets the default.
    /// </summary>
    public static int StartTurns(int? remembered) =>
        remembered is { } turns && turns is >= 0 and <= MaxTurns ? turns : DefaultTurns;

    /// <summary>
    /// The size line above a preview: "About N tokens (C characters). Start sends exactly this text." The tokens are the characters over four, rounded
    /// (as Claude's preview counts them).
    /// </summary>
    public static string SizeLine(int characters)
    {
        var tokens = (int)Math.Round(characters / 4.0, MidpointRounding.AwayFromZero);
        return $"About {tokens.ToString("N0", CultureInfo.InvariantCulture)} tokens ({characters.ToString("N0", CultureInfo.InvariantCulture)} characters). Start sends exactly this text.";
    }

    /// <summary>True when the kept summary is for the level chosen. It is kept only for the level and turns it was made at (R-D3).</summary>
    public static bool KeptMatches(SummaryLevel? level, KeptSummary? kept) => level is { } chosen && kept is not null && kept.MatchesLevel(chosen);

    /// <summary>
    /// The kept summary after a change of the choice (R-D3): a change of mode or of turns keeps it, and a change of level to another level than the
    /// summary was made at drops it. Only a summary choice can drop it.
    /// </summary>
    /// <param name="summaryChosen">True when the choice on screen is a summary.</param>
    /// <param name="level">The level on screen (null when there is no choice).</param>
    /// <param name="kept">The summary the dialog holds, or null.</param>
    public static KeptSummary? KeptAfterChange(bool summaryChosen, SummaryLevel? level, KeptSummary? kept) =>
        kept is not null && summaryChosen && level != kept.Level ? null : kept;

    /// <summary>
    /// The refusal of a resume or a start, or null when it may go on: the id must be in the app's own list of the CLI's sessions, and the session's
    /// folder must exist. There is no silent fallback to another session. The refusal texts are the provider's.
    /// </summary>
    /// <param name="listed">True when the id is in the list the rail shows.</param>
    /// <param name="folder">The session's folder as the history gives it (may be null or blank).</param>
    /// <param name="folderExists">True when the folder exists on disk.</param>
    /// <param name="notListed">The refusal of an id that is not listed.</param>
    /// <param name="folderMissing">The refusal of a session whose folder is missing or not on record.</param>
    public static string? RefusalFor(bool listed, string? folder, bool folderExists, string notListed, Func<string?, string> folderMissing)
    {
        if (!listed) return notListed;
        if (folder is not { Length: > 0 } || !folderExists) return folderMissing(folder);
        return null;
    }

    /// <summary>
    /// The status line after a start: what the new session starts from, and that it makes a model turn. The line also says where the start text is
    /// kept when the launch refuses (the Core script keeps the file and writes that line into the tab).
    /// </summary>
    public static string StartedLine(ProviderResumeWording wording, SummaryLevel? level, int turns)
    {
        var how = level is { } chosen
            ? $"a {wording.CliName} summary ({chosen.ToString().ToLowerInvariant()}) and the last {turns} turns"
            : $"the last {turns} turns";
        return $"Started a {wording.CliName} session from {how}. It starts with a model turn. If the tab reports that {wording.ShimName} is missing, the start text is kept in the Starts folder beside the state file.";
    }
}

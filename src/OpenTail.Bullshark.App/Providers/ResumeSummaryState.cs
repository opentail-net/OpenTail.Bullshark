using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The decisions of a resume screen's Summary mode (resume source and target plan, RS-D3, RS-D19 to RS-D26): the level (null is Zero, which makes no
/// summary call), the tail (the "Include last [N] turns" box) and its turns. The turns are zero when the tail is off, so a summary's input is the whole
/// session. A summary is tied to its tail and to its transcript (RS-D21): <see cref="KeptSummary.MatchesChoice"/> is the test. Pure, so the tests need no window.
/// </summary>
public static class ResumeSummaryState
{
    /// <summary>The Full radio, on each resume screen (RS-D3).</summary>
    public const string FullLabel = "Full";

    /// <summary>The Summary radio, on each resume screen (RS-D3).</summary>
    public const string SummaryLabel = "Summary";

    /// <summary>The Zero choice of the level row. It makes no summary call; the start text is still sent as the new session's first message (S6c review).</summary>
    public const string ZeroLabel = "Zero · no summary call";

    /// <summary>The status line of a Zero start with the tail off (RS-D25).</summary>
    public const string NothingToStartLine = "Nothing to start from: tick the tail or choose a summary level.";

    /// <summary>The status line of a summary made for another tail or level, until Make summary is pressed again (RS-D21).</summary>
    public const string OutOfDateLine = "The summary no longer matches the tail you chose. Make summary again.";

    /// <summary>The status line of Make summary when the tail keeps every turn, so the summary has nothing to read (RS-D26).</summary>
    public const string NothingToSummariseLine = "The tail covers the whole session, so there is nothing to summarise.";

    /// <summary>The turns a summary setting holds: the box's turns when the tail is on, else zero.</summary>
    public static int TurnsOf(bool tailOn, int turns) => tailOn ? turns : 0;

    /// <summary>True when the tail is on and keeps at least one turn. A Zero start needs one (RS-D25).</summary>
    public static bool TailKeepsTurns(bool tailOn, int turns) => tailOn && turns > 0;

    /// <summary>
    /// The refusal of a Zero start with no tail (RS-D25), or null. Zero is refused before Start, with its status line, when the tail is off. The other
    /// levels are never refused here.
    /// </summary>
    public static string? ZeroRefusal(SummaryLevel? level, bool tailOn, int turns) =>
        level is null && !TailKeepsTurns(tailOn, turns) ? NothingToStartLine : null;

    /// <summary>The Make summary button's text for a summary level (RS-D21).</summary>
    public const string MakeSummaryLabel = "Make summary";

    /// <summary>The Make summary button's text for Zero: it builds the text with no summary call (the button is the only gate, 2026-11-24).</summary>
    public const string BuildTextLabel = "Build text";

    /// <summary>The Make summary button's tooltip for Zero: the text is built from the transcript the screen holds, with no model call.</summary>
    public const string BuildTextTip = "Builds the start text from the last turns, word for word. No model call. Nothing is sent to a session.";

    /// <summary>The Make summary button's text: "Build text" for Zero, else "Make summary".</summary>
    public static string MakeButtonLabelOf(bool zero) => zero ? BuildTextLabel : MakeSummaryLabel;

    /// <summary>
    /// True when Make summary may be pressed (RS-D26). Zero (level null) builds its text with no call, so it needs the tail to keep turns
    /// (<see cref="TailKeepsTurns"/>) and valid turns. A summary level needs valid turns and a summary part with lines to read
    /// (<paramref name="summaryHasLines"/> is false when the tail keeps every turn of the session).
    /// </summary>
    public static bool MakeSummaryAllowed(SummaryLevel? level, bool tailOn, int turns, bool turnsValid, bool summaryHasLines) =>
        turnsValid && (level is null ? TailKeepsTurns(tailOn, turns) : summaryHasLines);

    /// <summary>
    /// True when the summary the screen holds is the one for the setting on screen. Zero has no summary to keep, so it always matches. A level matches
    /// only a summary made at that level, tail and transcript (RS-D21, FIX 1 of the S6c review). <paramref name="lines"/> is the transcript's line count
    /// the screen holds now.
    /// </summary>
    public static bool KeptMatches(SummaryLevel? level, int turns, int? lines, KeptSummary? kept) =>
        level is null || (kept is not null && kept.MatchesChoice(level, turns, lines ?? kept.Lines));

    /// <summary>
    /// True when a summary was made, but for another level, tail or transcript than the setting on screen (RS-D21): it is out of date, the preview is hidden,
    /// and Start waits for a new one.
    /// </summary>
    public static bool IsOutOfDate(SummaryLevel? level, int turns, int? lines, KeptSummary? kept) =>
        level is not null && kept is not null && !KeptMatches(level, turns, lines, kept);

    /// <summary>
    /// True when Start may go on for the setting: the text is shown, the turns are valid, Zero has a tail (RS-D25), and the summary is the one for the
    /// setting (RS-D21). Start never makes a summary.
    /// </summary>
    public static bool StartAllowed(SummaryLevel? level, bool tailOn, int turns, bool turnsValid, bool previewReady, int? lines, KeptSummary? kept) =>
        previewReady && turnsValid && ZeroRefusal(level, tailOn, turns) is null && KeptMatches(level, turns, lines, kept);

    /// <summary>
    /// True when the text is built at once, with no summary call: Zero (with the tail on, see <see cref="ZeroRefusal"/>), or a summary kept for the setting
    /// on screen. A summary is made only by Make summary (RS-D21), so a change of setting never makes one.
    /// </summary>
    public static bool RunsAtOnce(SummaryLevel? level, int turns, int? lines, KeptSummary? kept) => KeptMatches(level, turns, lines, kept);
}

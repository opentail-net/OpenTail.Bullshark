using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>The mode of the opencode resume screen (OC3, OC-D18 to OC-D20).</summary>
public enum OpenCodeResumeMode
{
    /// <summary>The same session, in a terminal tab (<c>opencode -s &lt;id&gt;</c>). No text is sent by OpenTail.</summary>
    Full,

    /// <summary>A new session that starts from the last turns. Opencode submits the text at once: one model turn (OC-D9).</summary>
    LastTurns,

    /// <summary>
    /// A summary, then the last turns. The summary is one model call on the owner's login, made by the Make summary button (OC-O4, decided 2026-11-23:
    /// allowed; the app keeps nothing for it).
    /// </summary>
    Summary,
}

/// <summary>
/// The choice made in the opencode resume screen. Turns is 0 for In full. Level is set for a summary only (null otherwise). Remember is the screen's tick;
/// the window writes it.
/// </summary>
public sealed record OpenCodeResumeChoice(OpenCodeResumeMode Mode, int Turns, bool Remember, SummaryLevel? Level = null);

/// <summary>
/// What the screen's preview callback gives: the start text (empty when the session has no messages, or when there is a problem) and the reason no text
/// was made (null when there is none).
/// </summary>
public sealed record OpenCodePreview(string Text, string? Problem = null);

/// <summary>
/// The rules and words of the opencode resume screen (OC3). The cost lines are opencode's own (OC-D9): neither the "No model call" line of the other
/// screens nor the shared Last-turns line is used here. Pure, so the tests need no window; the screen and the window only show what these return.
/// </summary>
public static class OpenCodeResumeRules
{
    /// <summary>The most turns the screen accepts. The same limit as the other resume screens.</summary>
    public const int MaxTurns = ProviderResumeRules.MaxTurns;

    /// <summary>The turns kept when the box is not a valid number. The same default as the other resume screens.</summary>
    public const int DefaultTurns = ProviderResumeRules.DefaultTurns;

    /// <summary>The line under the turns box when the box is not a valid number.</summary>
    public static readonly string TurnsMessage = ProviderResumeRules.TurnsMessage;

    /// <summary>The cost line of In full (OC-D9): the session opens in opencode's terminal, and nothing is sent until the owner types.</summary>
    public const string FullCostLine = "Opens the session in opencode's terminal. Nothing is sent until you type.";

    /// <summary>The cost line of Last turns (OC-D9, OC-O5): one model turn on the owner's login, and nothing else is sent.</summary>
    public const string LastTurnsCostLine = "Start makes one model turn on your opencode login: opencode submits the prompt at once. Nothing else is sent.";

    /// <summary>
    /// The cost line of the summary, shown before its call (OC-O4, decided 2026-11-23). The call is on the owner's opencode login, and opencode writes a
    /// session row into the owner's database when it runs.
    /// </summary>
    public const string SummaryCostLine =
        "Making the summary is one model call on your opencode login, and opencode writes a session row into your opencode database.";

    /// <summary>The line shown while the summary is made. It is a single call, so the line says so.</summary>
    public const string SummaryRunningLine = "Making the summary. This is one model call on your opencode login.";

    /// <summary>
    /// The refusal of a summary for a session with more messages than the transcript reader gives (5,000): the summary would read only the first ones.
    /// In full resumes such a session, as it needs no text.
    /// </summary>
    public static string LongSessionSummaryRefusal =>
        $"Not made: this session has more messages than the reader gives ({OpenCodeTranscriptReader.MaxLines:N0}), so its summary cannot read all of it. In full resumes it.";

    /// <summary>
    /// The status line when <c>opencode.ps1</c> is not on PATH. The screen is closed by Start, so the text is not offered again: nothing is written, and the
    /// session is started again once the shim is on PATH.
    /// </summary>
    public const string NoShimOnPath =
        "Not started: opencode.ps1 is not on PATH, so a start from text cannot run. Nothing was written. Put opencode.ps1 on PATH, then start the session again.";

    /// <summary>The status line when PowerShell 7 (<c>pwsh.exe</c>) is not on PATH, which the start tab needs. Nothing is written.</summary>
    public const string NoPwshOnPath =
        "Not started: PowerShell 7 (pwsh.exe) is not on PATH, so a start from text cannot run. Nothing was written. Put pwsh.exe on PATH, then start the session again.";

    /// <summary>
    /// The refusal of Last turns for a session with more messages than the transcript reader gives (5,000): the reader returns the first ones only, so the
    /// newest turns cannot be read. In full resumes such a session, as it needs no text.
    /// </summary>
    public static string LongSessionRefusal =>
        $"Not started: this session has more messages than the reader gives ({OpenCodeTranscriptReader.MaxLines:N0}), so its last turns cannot be read. In full resumes it.";

    /// <summary>The label of a mode, as its radio button shows it.</summary>
    public static string ModeLabel(OpenCodeResumeMode mode) => mode switch
    {
        OpenCodeResumeMode.Full => "In full: the whole conversation, in opencode's own terminal",
        OpenCodeResumeMode.LastTurns => "From the last turns",
        OpenCodeResumeMode.Summary => "From an opencode summary (one model call)",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The cost line of a mode (<see cref="FullCostLine"/>, <see cref="LastTurnsCostLine"/>, <see cref="SummaryCostLine"/>). The summary's line is shown
    /// before its call. Its start makes one model turn, as the last turns do (<see cref="LastTurnsCostLine"/>).
    /// </summary>
    public static string CostLine(OpenCodeResumeMode mode) => mode switch
    {
        OpenCodeResumeMode.Full => FullCostLine,
        OpenCodeResumeMode.LastTurns => LastTurnsCostLine,
        OpenCodeResumeMode.Summary => SummaryCostLine,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// The turns in the box: a whole number from 0 to <see cref="MaxTurns"/>. Returns false, with <see cref="DefaultTurns"/>, when it is not one.
    /// </summary>
    public static bool TryTurns(string text, out int turns) => ProviderResumeRules.TryTurns(text, out turns);

    /// <summary>
    /// True when Start may be pressed. In full is always allowed. From the last turns, and from a summary, it needs a preview for the current choice: a
    /// summary is made only by its Make summary button, never by Start.
    /// </summary>
    public static bool CanStart(OpenCodeResumeMode mode, bool previewReady) => mode switch
    {
        OpenCodeResumeMode.Full => true,
        OpenCodeResumeMode.LastTurns => previewReady,
        OpenCodeResumeMode.Summary => previewReady,
        _ => false,
    };

    /// <summary>True when the preview is built at once, with no press: the last turns only.</summary>
    public static bool RunsAtOnce(OpenCodeResumeMode mode) => mode == OpenCodeResumeMode.LastTurns;

    /// <summary>The Continue mode of a mode, for the "Remember for this session" mark (CN-D6). The mapping is kept here, so the shared choice file is not changed.</summary>
    public static ContinueMode ContinueModeOf(OpenCodeResumeMode mode) => mode switch
    {
        OpenCodeResumeMode.Full => ContinueMode.Full,
        OpenCodeResumeMode.LastTurns => ContinueMode.LastTurns,
        OpenCodeResumeMode.Summary => ContinueMode.Summary,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The size line above a preview (the shared wording).</summary>
    public static string SizeLine(int characters) => ProviderResumeRules.SizeLine(characters);

    /// <summary>The status line after a Last turns start: what it starts from, and that it makes one model turn. The log is not written with the title.</summary>
    public static string StartedLine(string title) => $"Started opencode with the last turns of {title} (one model turn).";

    /// <summary>The status line after a summary start: the summary and the last turns, and one model turn (the start itself).</summary>
    public static string StartedSummaryLine(string title) =>
        $"Started opencode with the summary and the last turns of {title} (one model turn).";

    /// <summary>The status line after a session is opened in a terminal tab (In full).</summary>
    public static string OpenedLine(string title) => $"Opened {title} in opencode.";
}

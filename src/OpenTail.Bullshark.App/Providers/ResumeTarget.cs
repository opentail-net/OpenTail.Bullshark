using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The target of a resume (the resume source and target plan, RS-D1 to RS-D4 and RS-D8): the platform the new session starts in. The target is the
/// source's own platform unless the owner picks another, and another is allowed only from a summary. Pure: the screens ask it, and it reads no file,
/// no process and no clock.
/// </summary>
public static class ResumeTarget
{
    /// <summary>RS-D8: G-O5 — the copy of Copilot that runs is not confirmed to have the flags the app passes; the owner enables this when settled.</summary>
    public const bool CopilotTargetEnabled = false;

    /// <summary>The target a resume screen starts on: the source's own platform (RS-D3).</summary>
    public static ProviderId DefaultTarget(ProviderId source) => source;

    /// <summary>
    /// The platform a resume starts in (RS-D2, RS-D3): the owner's pick when the mode is a summary, else the source's own platform. Full and Last turns
    /// always start where the session is, whatever the chooser shows. Pure: the three resume screens use it for their preview and their Start.
    /// </summary>
    public static ProviderId ChosenTarget(ProviderId source, ContinueMode mode, ProviderId selected) =>
        mode == ContinueMode.Summary ? selected : source;

    /// <summary>
    /// Whether a resume from <paramref name="source"/> in <paramref name="mode"/> may start in <paramref name="target"/>. The source's own platform is
    /// always allowed (RS-D1, RS-D2). Another platform is allowed only from a summary (RS-D2), and Copilot as a target only while
    /// <paramref name="copilotEnabled"/> is true (RS-D8).
    /// </summary>
    public static bool Allowed(ProviderId source, ContinueMode mode, ProviderId target, bool copilotEnabled = CopilotTargetEnabled)
    {
        if (target == source) return true;
        return mode == ContinueMode.Summary && (target != ProviderId.Copilot || copilotEnabled);
    }

    /// <summary>
    /// Why the target is refused, or null when <see cref="Allowed"/> is true. A cross-platform target in a non-summary mode is refused first; then a
    /// disabled Copilot target.
    /// </summary>
    public static string? RefusalReason(ProviderId source, ContinueMode mode, ProviderId target, bool copilotEnabled = CopilotTargetEnabled)
    {
        if (Allowed(source, mode, target, copilotEnabled)) return null;
        if (mode != ContinueMode.Summary)
            return "Only a summary can start in another platform. A full or last-turns resume stays in " + ContinueDecision.NameOf(source) + ".";
        return "Copilot cannot be the target yet: the Copilot install that runs is not confirmed (G-O5).";
    }

    /// <summary>The title of a resume screen, which names the source (RS-D4, RS-O4).</summary>
    public static string TitleOf(ProviderId source) => "Resume a " + ContinueDecision.NameOf(source) + " session";

    /// <summary>The button of a resume screen, which names the target (RS-D4, RS-O2).</summary>
    public static string ButtonOf(ProviderId target) => "Start in " + ContinueDecision.NameOf(target);

    /// <summary>
    /// The line that says a summary of the source starts a new session in the target (RS-D4). Null when the target is the source: the line shows only
    /// for a cross-platform start.
    /// </summary>
    public static string? SummaryLine(ProviderId source, ProviderId target) =>
        target == source ? null : "Summary of " + ContinueDecision.NameOf(source) + " session → new " + ContinueDecision.NameOf(target) + " session";
}

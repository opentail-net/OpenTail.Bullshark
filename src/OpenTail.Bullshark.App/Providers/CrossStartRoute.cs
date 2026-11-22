using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>Where a cross-platform start goes: refused, or started in Claude or in Codex (resume source and target plan, S3b-B1).</summary>
public enum CrossStartKind
{
    /// <summary>Nothing starts. The reason is in <see cref="CrossStartRoute.Refusal"/>.</summary>
    Refused,

    /// <summary>A start in Claude: the supervised run, through the request of <c>SessionWindow.CrossClaudeRequest</c> (RS-D15).</summary>
    Claude,

    /// <summary>A start in Codex, through its terminal tab.</summary>
    Codex,
}

/// <summary>
/// The dispatch decision of a start in another platform than its source (RS-D2, RS-D5, RS-D8, RS-D17), and the texts it shows. Pure: it reads no file,
/// starts no process and makes no model call. A Copilot target is refused here whatever <paramref name="copilotEnabled"/> says: the start of a Copilot
/// target is not built (RS-D8), so the enabling constant alone does not start one.
/// </summary>
public sealed record CrossStartRoute(CrossStartKind Kind, string? Refusal = null)
{
    /// <summary>The reason a start is refused.</summary>
    public static CrossStartRoute Refuse(string reason) => new(CrossStartKind.Refused, reason);

    /// <summary>
    /// The route of a start from <paramref name="source"/> in <paramref name="mode"/> into <paramref name="target"/>. The source's own platform is refused:
    /// it is not a start in another platform. Another platform is refused when <see cref="ResumeTarget.RefusalReason"/> refuses it (the mode first, then
    /// a disabled Copilot), and a Copilot target is refused in every case. Otherwise the route is Claude or Codex.
    /// </summary>
    public static CrossStartRoute For(
        ProviderId source, ProviderId target, ContinueMode mode, bool copilotEnabled = ResumeTarget.CopilotTargetEnabled)
    {
        if (target == source) return Refuse("Not started: the target is the session's own platform, so this is not a start in another platform.");
        var refusal = ResumeTarget.RefusalReason(source, mode, target, copilotEnabled);
        if (refusal is not null) return Refuse(refusal);
        if (target == ProviderId.Copilot)
            return Refuse(ResumeTarget.RefusalReason(source, mode, target, copilotEnabled: false) ?? "Copilot cannot be the target yet.");
        return new CrossStartRoute(target == ProviderId.Claude ? CrossStartKind.Claude : CrossStartKind.Codex);
    }

    /// <summary>
    /// The title of the new tab of a start in <paramref name="target"/> from a summary of the session titled <paramref name="sourceTitle"/> (D-TAB): the
    /// target's name, then "summary of", then the source session's title as its screen shows it. It names the source by its title only.
    /// </summary>
    public static string TabTitleOf(ProviderId target, string sourceTitle) => ContinueDecision.NameOf(target) + ": summary of " + sourceTitle;

    /// <summary>
    /// The status line after a start in <paramref name="target"/> from a summary of <paramref name="source"/> succeeds (D-STATUS).
    /// </summary>
    public static string StartedLine(ProviderId source, ProviderId target) =>
        "Started " + ContinueDecision.NameOf(target) + " from a summary of the " + ContinueDecision.NameOf(source) + " session.";
}

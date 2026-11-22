using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// What Continue opens (Continue plan, C5): the session the chooser picks from the scope, and the mode its resume screen starts on (CN-D5). Pure: the
/// window gives the sessions of the scope, the marks and the settings, and this reads no file and no clock. A null session means nothing is left to
/// continue in the scope, and then the mode is null too.
/// </summary>
public sealed record ContinueDecision(SessionSummary? Session, ContinueMode? Mode)
{
    /// <summary>
    /// The session to continue and the mode it opens on. The scope <see cref="ContinueScope.Provider"/> keeps to the active provider's sessions; the scope
    /// <see cref="ContinueScope.All"/> takes the newest of every provider given. The mode is the session's own remembered mode, else the default setting,
    /// else null (the screen decides).
    /// </summary>
    /// <param name="settings">The continue settings: the scope and the default mode.</param>
    /// <param name="activeProvider">The provider the window shows. Only the provider scope reads it.</param>
    /// <param name="sessions">The listed sessions of the scope.</param>
    /// <param name="marks">The library marks keyed by <c>provider:id</c> (<see cref="SessionKey.ToString"/>).</param>
    public static ContinueDecision Decide(
        ContinueSettings settings, ProviderId activeProvider, IReadOnlyList<SessionSummary> sessions, IReadOnlyDictionary<string, LibraryEntry> marks)
    {
        ProviderId? onlyProvider = settings.Scope == ContinueScope.Provider ? activeProvider : null;
        var session = ContinueChooser.Choose(sessions, marks, onlyProvider);
        if (session is null) return new ContinueDecision(null, null);
        var remembered = marks.TryGetValue(session.Key.ToString(), out var mark) ? mark.ResumeMode : null;
        return new ContinueDecision(session, ResumeModePolicy.Resolve(remembered, settings.DefaultMode));
    }

    /// <summary>The provider's name as the status line gives it.</summary>
    public static string NameOf(ProviderId provider) =>
        provider == ProviderId.Claude ? "Claude"
        : provider == ProviderId.Codex ? "Codex"
        : provider == ProviderId.Copilot ? "Copilot"
        : provider.Value;

    /// <summary>The mode as the status line says it. Summary says that nothing is made until the screen's own button is pressed (CN-D3).</summary>
    public static string ModeText(ContinueMode? mode) => mode switch
    {
        ContinueMode.Full => "in full",
        ContinueMode.LastTurns => "from the last turns",
        ContinueMode.Summary => "from a summary (made only when you press Make summary)",
        _ => "in the screen's own default mode",
    };

    /// <summary>
    /// The status line for a picked session: its title (the owner's own text, shown here and never logged), its provider and the mode it opens on.
    /// </summary>
    public static string PickedText(string title, ProviderId provider, ContinueMode? mode) =>
        $"Continue: {title} ({NameOf(provider)}). Opening its resume screen {ModeText(mode)}. Nothing starts until you confirm it there.";
}

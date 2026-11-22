using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The "Remember for this session" tick of the resume screens (Continue plan, C6, CN-D6). When the owner ticks it and starts a resume, the mode the screen
/// chose is written as the session's <see cref="LibraryEntry.ResumeMode"/> mark, under the provider's own key. This is the pure part: each screen's mode
/// mapped to a <see cref="ContinueMode"/>, the key text, and the new entry. The window writes the entry through its one mark path
/// (<c>SessionWindow.RememberResumeMode</c>). Pure, so the tests need no window.
/// </summary>
public static class RememberChoice
{
    /// <summary>Claude's resume screen: its last-turns choice (TailOnly) is the last-turns mode.</summary>
    public static ContinueMode ModeOf(ResumeMode mode) => mode switch
    {
        ResumeMode.Full => ContinueMode.Full,
        ResumeMode.Summary => ContinueMode.Summary,
        ResumeMode.TailOnly => ContinueMode.LastTurns,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The Codex resume screen's mode, as the Continue mode.</summary>
    public static ContinueMode ModeOf(CodexResumeMode mode) => mode switch
    {
        CodexResumeMode.Full => ContinueMode.Full,
        CodexResumeMode.LastTurns => ContinueMode.LastTurns,
        CodexResumeMode.Summary => ContinueMode.Summary,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The Copilot resume screen's mode, as the Continue mode.</summary>
    public static ContinueMode ModeOf(CopilotResumeMode mode) => mode switch
    {
        CopilotResumeMode.Full => ContinueMode.Full,
        CopilotResumeMode.LastTurns => ContinueMode.LastTurns,
        CopilotResumeMode.Summary => ContinueMode.Summary,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The key of a session's mark: <c>provider:id</c>, the text the library file uses for every provider's entries.</summary>
    public static string KeyOf(ProviderId provider, string sessionId) => new SessionKey(provider, sessionId).ToString();

    /// <summary>
    /// The entry with the remembered mode set. Every other field is kept as it is. A session with no entry yet (<paramref name="existing"/> is null)
    /// gets a new entry that holds only the mode.
    /// </summary>
    public static LibraryEntry Apply(LibraryEntry? existing, ContinueMode mode) => (existing ?? new LibraryEntry()) with { ResumeMode = mode };
}

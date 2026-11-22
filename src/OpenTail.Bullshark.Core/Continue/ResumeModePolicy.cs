namespace OpenTail.Bullshark.Core.Continue;

/// <summary>
/// The mode a Continue opens a session at (CN-D5): the session's own remembered mode first, then the global setting, then null. A null result
/// means the screen uses its own default: Claude keeps its recommendation (CN-D9), and Codex and Copilot start on last turns (CN-D2). This policy
/// holds no default of its own, so it changes no provider's behaviour.
/// </summary>
public static class ResumeModePolicy
{
    /// <param name="sessionMode">The mode the session remembered from its resume screen (its library mark), or null when it has none.</param>
    /// <param name="globalDefault">The <c>continue.defaultMode</c> setting, or null when it is not set or not known.</param>
    public static ContinueMode? Resolve(ContinueMode? sessionMode, ContinueMode? globalDefault) => sessionMode ?? globalDefault;
}

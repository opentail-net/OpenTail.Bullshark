namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The tool a session belongs to. The text of <see cref="Value"/> is what is persisted, as the prefix of a
/// <see cref="SessionKey"/> (<c>claude:&lt;id&gt;</c>, <c>codex:&lt;id&gt;</c>).
/// </summary>
public readonly record struct ProviderId(string Value)
{
    /// <summary>Claude Code.</summary>
    public static readonly ProviderId Claude = new("claude");

    /// <summary>OpenAI Codex. Its history is read on net10.0 and net11.0; net10.0 decodes the compressed rollouts with ZstdSharp.Port.</summary>
    public static readonly ProviderId Codex = new("codex");

    /// <summary>GitHub Copilot CLI. Its history is plain files, so it is read on net10.0 and net11.0 alike.</summary>
    public static readonly ProviderId Copilot = new("copilot");

    /// <summary>opencode (the npm CLI, read against 1.4.x). Its history is one SQLite database, which is only read, never written (OC-D2).</summary>
    public static readonly ProviderId OpenCode = new("opencode");

    public override string ToString() => Value;
}

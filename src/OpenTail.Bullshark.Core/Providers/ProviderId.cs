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

    public override string ToString() => Value;
}

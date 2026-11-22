using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// What a provider's terminal tab takes from its CLI for the shared tab open and exit watch (R7): the provider, the name its log lines use, and the
/// provider's own exit classification and ended title. Codex and Copilot each pass one. Their classifications are identical in Core (<c>CodexExit</c>,
/// <c>CopilotExit</c>), and both stay in Core.
/// </summary>
internal sealed record ProviderTerminalRules(
    ProviderId Provider,
    string LogName,
    Func<bool, bool, int?, ExitRecord> Classify,
    Func<string, string> EndedTitle);

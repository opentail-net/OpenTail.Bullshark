using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The ended state of a Copilot terminal tab (G2.8, G-D20): the same watch as Codex's (R6). A tab whose process ends on its own is marked "(ended)" and
/// its status line says "Ended; no exit code was reported." The watch and the tab open are shared (<c>Providers/SessionWindow.ProviderTerminal.cs</c>, R7);
/// this file gives them Copilot's rules. The classification is <see cref="CopilotExit"/> in Core.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The Copilot terminal's rules for the shared tab open and exit watch (R7): its log name, and Copilot's own exit classification.</summary>
    private static readonly ProviderTerminalRules CopilotTerminal = new(ProviderId.Copilot, "copilot", CopilotExit.Classify, CopilotExit.EndedTitle);
}

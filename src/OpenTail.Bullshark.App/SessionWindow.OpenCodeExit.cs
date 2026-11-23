using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The ended state of an opencode terminal tab (OC2, copied from the Copilot exit partial, G2.8): the same watch as Copilot's, a tab whose process ends on
/// its own is marked "(ended)" and its status line says why. The watch and the tab open are shared (<c>Providers/SessionWindow.ProviderTerminal.cs</c>).
/// The classification and the ended title are <see cref="CopilotExit"/> in Core: its texts name no provider ("Ended.", "Ended with exit code N.", "Ended;
/// no exit code was reported.", and the " (ended)" suffix), so they are the same for opencode. The log name is "opencode".
/// </summary>
public partial class SessionWindow
{
    /// <summary>The opencode terminal's rules for the shared tab open and exit watch (R7): its log name, and the exit classification and ended title.</summary>
    private static readonly ProviderTerminalRules OpenCodeTerminal = new(ProviderId.OpenCode, "opencode", CopilotExit.Classify, CopilotExit.EndedTitle);
}

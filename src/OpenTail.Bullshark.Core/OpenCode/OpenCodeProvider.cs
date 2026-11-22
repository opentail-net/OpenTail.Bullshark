using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// opencode as a provider (the first stage, OC1a). It has its paths and its terminal commands. Its history source and transcript reader come in the
/// next stage (OC1b, OC-D5, OC-D7). It implements no supervisor and no live list, as Codex and Copilot do not (OC-D16).
/// </summary>
public sealed class OpenCodeProvider : IProvider, ITerminalOpener
{
    private readonly OpenCodeTerminalOpener _opener = new();

    /// <summary>The paths the provider reads: <c>XDG_DATA_HOME\opencode</c> when that is set, else <c>~\.local\share\opencode</c>, when none are given.</summary>
    public OpenCodeProvider(ProviderPaths? paths = null)
    {
        Paths = paths ?? ProviderPaths.ForOpenCode();
    }

    public ProviderPaths Paths { get; }

    public ProviderId Id => ProviderId.OpenCode;

    public string DisplayName => "opencode";

    /// <summary>The command of a new opencode tab, in <paramref name="folder"/>.</summary>
    public string NewCommandLine(string folder) => _opener.NewCommandLine(folder);

    /// <summary>The command that resumes a saved opencode session by its id (no folder; the window sets it).</summary>
    public string ResumeCommandLine(string sessionId) => _opener.ResumeCommandLine(sessionId);
}

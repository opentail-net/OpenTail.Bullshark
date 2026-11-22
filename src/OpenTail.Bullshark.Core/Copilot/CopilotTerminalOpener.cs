using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The command lines of a Copilot terminal tab (G1.5, G-D8, G-D11, G-D12). A new session runs
/// <c>cmd.exe /d /c copilot -C "&lt;folder&gt;" --sync-session=off</c>; a resume runs <c>cmd.exe /d /c copilot --resume=&lt;id&gt; --sync-session=off</c>, with no
/// <c>-C</c>: the window starts the tab in the session's folder, because <see cref="ITerminalOpener.ResumeCommandLine"/> takes no folder. The command goes
/// through <c>cmd.exe</c> because the install is a shim (as Codex's is). Every line carries <c>--sync-session=off</c> (G-D8). Nothing is started here.
/// The folder and id rules are shared with the other CLIs (<see cref="CommandLineRules"/>).
/// </summary>
public sealed class CopilotTerminalOpener : ITerminalOpener
{
    /// <summary>The flag every Copilot command line carries: no read-only account history sync for the run (G-D8, G-O2).</summary>
    public const string SyncOff = "--sync-session=off";

    /// <summary>
    /// <c>cmd.exe /d /c copilot -C "&lt;folder&gt;" --sync-session=off</c>. The folder must pass <see cref="CommandLineRules.RequireFolder"/>; otherwise
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public string NewCommandLine(string folder) =>
        CommandLineRules.CmdWrap("copilot", [CommandLineRules.FolderOption(folder), SyncOff]);

    /// <summary>
    /// <c>cmd.exe /d /c copilot --resume=&lt;id&gt; --sync-session=off</c>. The id must be a UUID (see <see cref="CommandLineRules.IsSessionIdShape"/>);
    /// otherwise <see cref="ArgumentException"/>. The working folder is the session's folder, which the window sets.
    /// </summary>
    public string ResumeCommandLine(string sessionId)
    {
        if (!CommandLineRules.IsSessionIdShape(sessionId))
            throw new ArgumentException("The session id is not a UUID, so no resume command is built.", nameof(sessionId));
        return CommandLineRules.CmdWrap("copilot", [$"--resume={sessionId}", SyncOff]);
    }
}

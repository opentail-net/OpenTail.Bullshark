using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// The command lines of an opencode terminal tab (OC-D8). A new session runs <c>cmd.exe /d /c opencode "&lt;folder&gt;"</c>: the program's root command
/// takes the project folder as its first argument. A resume runs <c>cmd.exe /d /c opencode -s &lt;id&gt;</c>, with no folder: the window starts the tab in
/// the session's folder. The command goes through <c>cmd.exe</c> because the install is a shim (as Codex's and Copilot's are). Nothing is started here.
/// The folder rule is shared with the other CLIs (<see cref="CommandLineRules.RequireFolder"/>); the id rule is opencode's own
/// (<see cref="OpenCodeSessionId"/>).
/// </summary>
public sealed class OpenCodeTerminalOpener : ITerminalOpener
{
    /// <summary>
    /// <c>cmd.exe /d /c opencode "&lt;folder&gt;"</c>. The folder must pass <see cref="CommandLineRules.RequireFolder"/>; otherwise
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public string NewCommandLine(string folder)
    {
        CommandLineRules.RequireFolder(folder);
        return CommandLineRules.CmdWrap("opencode", [$"\"{folder}\""]);
    }

    /// <summary>
    /// <c>cmd.exe /d /c opencode -s &lt;id&gt;</c>. The id must have the shape of <see cref="OpenCodeSessionId"/>; otherwise
    /// <see cref="ArgumentException"/>, whose message does not carry the id.
    /// </summary>
    public string ResumeCommandLine(string sessionId)
    {
        if (!OpenCodeSessionId.IsValid(sessionId))
            throw new ArgumentException("The session id does not have the opencode session id shape, so no resume command is built.", nameof(sessionId));
        return CommandLineRules.CmdWrap("opencode", ["-s", sessionId]);
    }
}

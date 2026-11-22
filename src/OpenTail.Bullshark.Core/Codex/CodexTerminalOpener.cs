using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The command lines of a Codex terminal tab (stage C3, C-D5). The window owns the tab and sets its working directory: a new session runs
/// <c>cmd.exe /d /c codex -C "&lt;folder&gt;"</c>, and a resume runs <c>cmd.exe /d /c codex resume &lt;id&gt;</c> in the session's own folder.
/// The command goes through <c>cmd.exe</c> because on a machine with the npm install, <c>codex</c> is a <c>codex.cmd</c> shim and there is no
/// <c>codex.exe</c>; a terminal that starts the name directly cannot run a shim. Codex has no background session here (no <c>--bg</c>), so
/// nothing is supervised. The folder and id rules are shared with the other CLIs (<see cref="CommandLineRules"/>).
/// </summary>
public sealed class CodexTerminalOpener : ITerminalOpener
{
    /// <summary>
    /// <c>cmd.exe /d /c codex -C "&lt;folder&gt;"</c>. The folder must pass <see cref="CommandLineRules.RequireFolder"/>: non-blank, no double quote,
    /// no percent sign (cmd expands <c>%NAME%</c> even inside quotes), no control character, and not ending with a backslash (a backslash before the
    /// closing quote would escape it). An ampersand or a caret is inside the quotes and is taken as text. Otherwise <see cref="ArgumentException"/>.
    /// </summary>
    public string NewCommandLine(string folder) => NewCommandLine(folder, null);

    /// <summary>
    /// The same line with <c>-m &lt;model&gt;</c> after the folder (P9, decision M-D5), when <paramref name="model"/> is set. The model must pass
    /// <see cref="CodexModelList.IsValidModelId"/>, or <see cref="ArgumentException"/>. Null gives the line without <c>-m</c>.
    /// </summary>
    public string NewCommandLine(string folder, string? model)
    {
        if (model is not null && !CodexModelList.IsValidModelId(model))
            throw new ArgumentException("The model id has a character outside lower-case letters, digits, dots and hyphens.", nameof(model));
        var folderOption = CommandLineRules.FolderOption(folder);
        return model is null
            ? CommandLineRules.CmdWrap("codex", [folderOption])
            : CommandLineRules.CmdWrap("codex", [folderOption, "-m", model]);
    }

    /// <summary>
    /// <c>cmd.exe /d /c codex resume &lt;id&gt;</c>, run through <c>cmd.exe</c> for the same reason as <see cref="NewCommandLine(string)"/>. The id must
    /// be a UUID (see <see cref="CommandLineRules.IsSessionIdShape"/>); otherwise <see cref="ArgumentException"/>. The working directory is the session's
    /// folder, which the window sets.
    /// </summary>
    public string ResumeCommandLine(string sessionId)
    {
        if (!CommandLineRules.IsSessionIdShape(sessionId))
            throw new ArgumentException("The session id is not a UUID, so no resume command is built.", nameof(sessionId));
        return CommandLineRules.CmdWrap("codex", ["resume", sessionId]);
    }

    /// <summary>
    /// <c>cmd.exe /d /c codex resume</c>: the Codex picker, with no id, so the owner chooses the session (R7). It is the fallback of the resume ladder
    /// when the saved id is not found. Run through <c>cmd.exe</c> for the same reason as <see cref="ResumeCommandLine"/>, in the folder the window sets.
    /// </summary>
    public string PickerCommandLine() => CommandLineRules.CmdWrap("codex", ["resume"]);
}

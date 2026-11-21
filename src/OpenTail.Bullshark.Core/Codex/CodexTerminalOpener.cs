using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The command lines of a Codex terminal tab (stage C3, C-D5). The window owns the tab and sets its working directory: a new session runs
/// <c>cmd.exe /d /c codex -C "&lt;folder&gt;"</c>, and a resume runs <c>cmd.exe /d /c codex resume &lt;id&gt;</c> in the session's own folder.
/// The command goes through <c>cmd.exe</c> because on a machine with the npm install, <c>codex</c> is a <c>codex.cmd</c> shim and there is no
/// <c>codex.exe</c>; a terminal that starts the name directly cannot run a shim. Codex has no background session here (no <c>--bg</c>), so
/// nothing is supervised.
/// </summary>
public sealed class CodexTerminalOpener : ITerminalOpener
{
    /// <summary>A Codex session id: a UUID in the usual 8-4-4-4-12 hexadecimal form. Anything else is refused, so an id cannot add arguments.</summary>
    private static readonly Regex SessionIdShape = new(
        @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>cmd.exe /d /c codex -C "&lt;folder&gt;"</c>. The folder must be non-blank, hold no double quote, no percent sign (cmd expands
    /// <c>%NAME%</c> even inside quotes) and no control character, and not end with a backslash (a backslash before the closing quote would
    /// escape it). An ampersand or a caret is inside the quotes and is taken as text. Otherwise <see cref="ArgumentException"/>.
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
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("The folder is empty.", nameof(folder));
        if (folder.Contains('"'))
            throw new ArgumentException("The folder has a double quote, so it cannot be put in the command line.", nameof(folder));
        if (folder.Contains('!'))
            throw new ArgumentException("The folder has an exclamation mark, which cmd.exe expands when delayed expansion is on, so no command line is built.", nameof(folder));
        if (folder.Contains('%'))
            throw new ArgumentException("The folder has a percent sign, which cmd.exe would expand, so no command line is built.", nameof(folder));
        if (folder.Any(char.IsControl))
            throw new ArgumentException("The folder has a control character, so it cannot be put in the command line.", nameof(folder));
        if (folder.EndsWith('\\'))
            throw new ArgumentException("The folder ends with a backslash, which would escape the closing quote.", nameof(folder));
        return model is null
            ? $"cmd.exe /d /c codex -C \"{folder}\""
            : $"cmd.exe /d /c codex -C \"{folder}\" -m {model}";
    }

    /// <summary>
    /// <c>cmd.exe /d /c codex resume &lt;id&gt;</c>, run through <c>cmd.exe</c> for the same reason as <see cref="NewCommandLine"/>. The id must
    /// be a UUID (see <see cref="SessionIdShape"/>); otherwise <see cref="ArgumentException"/>. The working directory is the session's folder,
    /// which the window sets.
    /// </summary>
    public string ResumeCommandLine(string sessionId)
    {
        if (sessionId is null || !SessionIdShape.IsMatch(sessionId))
            throw new ArgumentException("The session id is not a UUID, so no resume command is built.", nameof(sessionId));
        return $"cmd.exe /d /c codex resume {sessionId}";
    }
}

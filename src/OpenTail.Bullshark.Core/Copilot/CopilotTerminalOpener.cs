using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The command lines of a Copilot terminal tab (G1.5, G-D8, G-D11, G-D12). A new session runs
/// <c>cmd.exe /d /c copilot -C "&lt;folder&gt;" --sync-session=off</c>; a resume runs <c>cmd.exe /d /c copilot --resume=&lt;id&gt; --sync-session=off</c>, with no
/// <c>-C</c>: the window starts the tab in the session's folder, because <see cref="ITerminalOpener.ResumeCommandLine"/> takes no folder. The command goes
/// through <c>cmd.exe</c> because the install is a shim (as Codex's is). Every line carries <c>--sync-session=off</c> (G-D8). Nothing is started here.
/// </summary>
public sealed class CopilotTerminalOpener : ITerminalOpener
{
    /// <summary>The flag every Copilot command line carries: no read-only account history sync for the run (G-D8, G-O2).</summary>
    public const string SyncOff = "--sync-session=off";

    /// <summary>A session id in the usual 8-4-4-4-12 hexadecimal form (all 52 local folder names have this form). Anything else is refused.</summary>
    private static readonly Regex SessionIdShape = new(
        @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>cmd.exe /d /c copilot -C "&lt;folder&gt;" --sync-session=off</c>. The folder must pass <see cref="RequireFolder"/>; otherwise
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public string NewCommandLine(string folder)
    {
        RequireFolder(folder);
        return $"cmd.exe /d /c copilot -C \"{folder}\" {SyncOff}";
    }

    /// <summary>
    /// <c>cmd.exe /d /c copilot --resume=&lt;id&gt; --sync-session=off</c>. The id must be a UUID; otherwise <see cref="ArgumentException"/>. The working
    /// folder is the session's folder, which the window sets.
    /// </summary>
    public string ResumeCommandLine(string sessionId)
    {
        if (sessionId is null || !SessionIdShape.IsMatch(sessionId))
            throw new ArgumentException("The session id is not a UUID, so no resume command is built.", nameof(sessionId));
        return $"cmd.exe /d /c copilot --resume={sessionId} {SyncOff}";
    }

    /// <summary>
    /// Refuses a folder that cannot sit inside the double quotes of a <c>cmd.exe</c> line: blank, a double quote, an exclamation mark (delayed
    /// expansion), a percent sign (expanded even inside quotes), a control character, or a trailing backslash (it would escape the closing quote).
    /// Ampersands and carets are inside the quotes and are taken as text. Used by the terminal and the summary call (G1.4).
    /// </summary>
    internal static void RequireFolder(string folder)
    {
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
    }
}

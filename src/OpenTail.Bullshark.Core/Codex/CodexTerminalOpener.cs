using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The command lines of a Codex terminal tab (stage C3, C-D5). The window owns the tab and sets its working directory: a new session runs
/// <c>codex -C "&lt;folder&gt;"</c>, and a resume runs <c>codex resume &lt;id&gt;</c> in the session's own folder. Codex has no background
/// session here (no <c>--bg</c>), so nothing is supervised.
/// </summary>
public sealed class CodexTerminalOpener : ITerminalOpener
{
    /// <summary>A Codex session id: a UUID in the usual 8-4-4-4-12 hexadecimal form. Anything else is refused, so an id cannot add arguments.</summary>
    private static readonly Regex SessionIdShape = new(
        @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>codex -C "&lt;folder&gt;"</c>. The folder must be non-blank, hold no double quote and no control character, and not end with a
    /// backslash (a backslash before the closing quote would escape it). Otherwise <see cref="ArgumentException"/>.
    /// </summary>
    public string NewCommandLine(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("The folder is empty.", nameof(folder));
        if (folder.Contains('"'))
            throw new ArgumentException("The folder has a double quote, so it cannot be put in the command line.", nameof(folder));
        if (folder.Any(char.IsControl))
            throw new ArgumentException("The folder has a control character, so it cannot be put in the command line.", nameof(folder));
        if (folder.EndsWith('\\'))
            throw new ArgumentException("The folder ends with a backslash, which would escape the closing quote.", nameof(folder));
        return $"codex -C \"{folder}\"";
    }

    /// <summary>
    /// <c>codex resume &lt;id&gt;</c>. The id must be a UUID (see <see cref="SessionIdShape"/>); otherwise <see cref="ArgumentException"/>.
    /// The working directory is the session's folder, which the window sets.
    /// </summary>
    public string ResumeCommandLine(string sessionId)
    {
        if (sessionId is null || !SessionIdShape.IsMatch(sessionId))
            throw new ArgumentException("The session id is not a UUID, so no resume command is built.", nameof(sessionId));
        return $"codex resume {sessionId}";
    }
}

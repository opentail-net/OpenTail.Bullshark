using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The rules for putting a folder and a session id into a <c>cmd.exe</c> command line (provider kit R3). A terminal tab's line is
/// <c>cmd.exe /d /c &lt;cli&gt; ...</c>, because the CLI is a shim on the machine. The folder goes in double quotes, so it must not hold a character
/// that breaks the quotes or that cmd.exe expands. A session id goes in the line bare, so it must be a UUID. The readers' path-safety checks are not
/// here: they guard file paths, not command lines.
/// </summary>
public static class CommandLineRules
{
    /// <summary>A session id in the usual 8-4-4-4-12 hexadecimal form (all 52 local folder names have this form). Anything else is refused.</summary>
    private static readonly Regex SessionIdShape = new(
        @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Refuses a folder that cannot sit inside the double quotes of a <c>cmd.exe</c> line: blank, a double quote, an exclamation mark (delayed
    /// expansion), a percent sign (expanded even inside quotes), a control character, or a trailing backslash (it would escape the closing quote).
    /// Ampersands and carets are inside the quotes and are taken as text. Otherwise <see cref="ArgumentException"/>.
    /// </summary>
    public static void RequireFolder(string folder)
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

    /// <summary>
    /// <c>-C "&lt;folder&gt;"</c>: the folder option that both CLIs take, with the folder in double quotes. The folder is checked by
    /// <see cref="RequireFolder"/> first, so a folder that would break the line is refused before anything is quoted.
    /// </summary>
    public static string FolderOption(string folder)
    {
        RequireFolder(folder);
        return $"-C \"{folder}\"";
    }

    /// <summary>
    /// The line that runs a CLI through <c>cmd.exe</c>: <c>cmd.exe /d /c &lt;program&gt; &lt;arguments&gt;</c>, with the arguments joined by single spaces.
    /// Each argument is already in its line form (a <see cref="FolderOption"/>, a flag, or a value the caller has checked). This method adds no quotes.
    /// </summary>
    public static string CmdWrap(string program, IReadOnlyList<string> arguments) =>
        "cmd.exe /d /c " + string.Join(' ', arguments.Prepend(program));

    /// <summary>
    /// True when <paramref name="id"/> is a UUID in the 8-4-4-4-12 hexadecimal form, the shape of a Codex or Copilot session id. Null is not.
    /// </summary>
    public static bool IsSessionIdShape(string id) => id is not null && SessionIdShape.IsMatch(id);
}

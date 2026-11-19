using System.ComponentModel;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// The sentence the owner reads when a supervisor pass fails (firstrun batch B2): the status line and the "Supervisor problem" row show it instead
/// of the raw exception. A known cause gets a next step; anything else gets its exception type and its first line. No stack trace, and each
/// path is cut to its file name (<see cref="PassHealth.ReducePaths"/>). Pure: the window passes the exception in.
/// </summary>
public static class PassErrorText
{
    /// <summary>The most characters of an unknown failure's first line that are kept.</summary>
    public const int MaxLength = 200;

    /// <summary>The sentence for a `claude` that cannot be started: the one next step the owner can take from the window.</summary>
    public const string ClaudeNotStarted =
        "Claude Code could not be started: check that `claude` is installed and on PATH (Settings > Claude > Check setup)";

    /// <summary>The sentence for one exception. Null-safe only for a non-null exception; the window never passes null.</summary>
    public static string Of(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (IsClaudeNotStarted(ex) || (ex.InnerException is { } inner && IsClaudeNotStarted(inner))) return ClaudeNotStarted;
        if (StoreOf(ex) is { } store) return $"{store.Name} cannot be read: {FirstLine(store.Reason)}";
        return Clip($"{ex.GetType().Name}: {FirstLine(ex.Message)}");
    }

    /// <summary>
    /// The `claude` program could not be started: the process start fails with the program's name in the message, and the runner's own message
    /// names it too. A git or other missing program is not this case, so it is not given the claude sentence.
    /// </summary>
    private static bool IsClaudeNotStarted(Exception ex) =>
        ex is Win32Exception && ClaudeStart.IsMatch(ex.Message)
        || ex is InvalidOperationException && ClaudeRunner.IsMatch(ex.Message);

    private static readonly Regex ClaudeStart = new(@"process\s+'[^']*claude[^']*'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClaudeRunner = new(@"Could not start\s+'[^']*claude[^']*'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A file that cannot be read, with its name alone: the store's own exception carries the path, and a file error names the file in quotes
    /// (`'C:\...\runs.json'`). Null for any other exception.
    /// </summary>
    private static (string Name, string Reason)? StoreOf(Exception ex)
    {
        if (ex is StoreUnreadableException store) return (NameOf(store.FilePath), store.Reason);
        if ((ex is IOException or UnauthorizedAccessException) && JsonName.Match(ex.Message) is { Success: true } match)
            return (NameOf(match.Groups[1].Value), ex.Message);
        return null;
    }

    private static readonly Regex JsonName = new(@"'([^']*\.json)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The file name of a path: the part after the last separator (a bare name stays as it is).</summary>
    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Length == 0 ? "a file" : name;
    }

    /// <summary>The first non-empty line, with each path cut to its file name and the spaces collapsed.</summary>
    private static string FirstLine(string text)
    {
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return string.Join(' ', PassHealth.ReducePaths(line).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Clip(string text) => text.Length <= MaxLength ? text : text[..MaxLength] + "…";
}

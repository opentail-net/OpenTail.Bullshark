using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>What a pass's report did to the problem state (reliability batch R9).</summary>
public enum PassHealthChange
{
    /// <summary>The pass worked, and it worked before too: nothing to show.</summary>
    None,

    /// <summary>The pass failed after it worked: the row shows, and one Problems toast is raised.</summary>
    Failed,

    /// <summary>The pass still fails: the row is updated (its reason may have changed), and no toast is raised again.</summary>
    StillFailed,

    /// <summary>The pass works again: the row is cleared.</summary>
    Recovered,
}

/// <summary>
/// Whether the supervisor's pass is failing (reliability batch R9): an unreadable runs.json or state.json, an unreadable Claude settings file,
/// a missing or failing `claude`, or an unexpected error. The pass reports after each attempt: null when it went well, else the reason. A failure
/// that starts raises one notice; one that goes on does not raise another every 15 seconds (the owner sees it once, at 3 AM).
/// </summary>
public sealed class PassHealth
{
    /// <summary>The longest reason shown, in characters.</summary>
    public const int MaxReason = 160;

    /// <summary>
    /// A path written out in full (review R3): a drive, a server share, or a Git Bash, WSL or Unix root (<c>/c/</c>, <c>/mnt/c/</c>, <c>/home/</c>,
    /// <c>/Users/</c>). Its folders may hold spaces. It ends at a file name with an extension (<c>state.json</c>, followed by a space or a colon) or,
    /// with no extension, at a quote, a bracket, a comma, a semicolon or the end of the line: when unsure, the rest of the line is part of the path,
    /// so no folder name is left behind. A drive letter is not the last letter of a scheme: "https:/" is not the drive "s:/" (review R1).
    /// </summary>
    private static readonly Regex PathLike = new(
        @"(?:(?<![A-Za-z])[A-Za-z]:[\\/]|\\\\|(?<![A-Za-z0-9_.~\\/-])/(?:mnt/[A-Za-z]/|[A-Za-z]/|home/|Users/))" +
        @"(?:[^\\/\r\n'""()\[\],;<>|*?]*[\\/])*" +
        @"(?:[^\s\\/\r\n'""()\[\],;<>|*?]*\.[A-Za-z0-9]{1,8}(?![A-Za-z0-9])|[^\\/\r\n'""()\[\],;<>|*?]*)",
        RegexOptions.Compiled);

    /// <summary>The reason of the current failure, or null when the pass works.</summary>
    public string? Problem { get; private set; }

    /// <summary>Reports the result of one pass and returns what it changed. The reason is shortened by <see cref="Short"/>.</summary>
    public PassHealthChange Observe(string? problem)
    {
        if (problem is null)
        {
            if (Problem is null) return PassHealthChange.None;
            Problem = null;
            return PassHealthChange.Recovered;
        }

        var was = Problem;
        Problem = Short(problem);
        return was is null ? PassHealthChange.Failed : PassHealthChange.StillFailed;
    }

    /// <summary>
    /// A reason for the owner to read: each path in it is cut to its file name (a folder name, the owner's user name and the rest of the path do not
    /// go on the rail or in a toast), the spaces are collapsed, and the text is at most <see cref="MaxReason"/> characters.
    /// </summary>
    public static string Short(string message)
    {
        var text = ReducePaths(message);
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= MaxReason ? text : text[..MaxReason] + "…";
    }

    /// <summary>The text with each full path (a drive or a server share) cut to its file name; other text is unchanged.</summary>
    public static string ReducePaths(string text) => PathLike.Replace(text, match =>
    {
        // A bare drive ("C:" or "C:\") or a share with no name has no file name: it is "a path". A name may hold a colon (a line number), so it is kept.
        var trimmed = match.Value.TrimEnd('\\', '/');
        if (trimmed.EndsWith(':') || trimmed.Length <= 2) return "a path";
        var name = Path.GetFileName(trimmed);
        return name.Length == 0 ? "a path" : name;
    });
}

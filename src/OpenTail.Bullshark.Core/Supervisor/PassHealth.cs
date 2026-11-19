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

    /// <summary>A path written out in full: a drive or a server share, then the rest up to a space, a quote, a bracket or a comma.</summary>
    private static readonly Regex PathLike = new(@"(?:[A-Za-z]:[\\/]|\\\\)[^\s'""()\[\],;]*", RegexOptions.Compiled);

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
        var text = PathLike.Replace(message, match =>
        {
            var name = Path.GetFileName(match.Value.TrimEnd('\\', '/'));
            return name.Length == 0 || name.Contains(':') ? "a path" : name;
        });
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= MaxReason ? text : text[..MaxReason] + "…";
    }
}

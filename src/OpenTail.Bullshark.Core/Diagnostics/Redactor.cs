using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Diagnostics;

/// <summary>
/// Makes text safe to put in a crash file or a diagnostics zip (firstrun batch B3). Pure: the caller passes the user's profile folder.
/// Each line goes through these rules, in this order:
/// <list type="number">
/// <item>A line that carries prompt or transcript text (a <c>prompt:</c>, <c>brief:</c>, <c>transcript:</c> marker, or a JSON <c>"text"</c>,
/// <c>"content"</c>, <c>"role"</c> or <c>"prompt"</c> key) is replaced by <see cref="PromptMark"/>. Output that a failed `claude` printed is cut
/// after its "did not print an id:" words.</item>
/// <item>The user's profile folder is written as <c>~</c>.</item>
/// <item>A URL in any scheme (<c>https://…</c>) keeps its scheme, host, port and path; its user name and password, its query and its fragment
/// are removed (<see cref="UrlWithoutSecrets"/>). A remote MCP server's URL often carries a key in its query.</item>
/// <item>Every other full path (a drive or a share) is cut to its file name; a stack frame keeps its file name and line.</item>
/// <item>A session id in GUID form is cut to its first 8 characters.</item>
/// <item>A bearer token, a key with a known prefix (<c>sk-</c>, <c>ghp_</c>, <c>github_pat_</c>, <c>xox…-</c>), or a run of 32 or more letters,
/// digits, <c>+</c> or <c>/</c> (a base64 or hex secret) is replaced by <see cref="TokenMark"/>.</item>
/// <item>A line longer than <see cref="MaxLineLength"/> characters is cut, with an ellipsis.</item>
/// </list>
/// The marker rule and the run rule are heuristics: a secret that is short, or prompt text without a marker, is not caught. The rules are
/// tested with planted canary strings (RedactorTests), and the owner reads a crash file before sending it.
/// </summary>
public static class Redactor
{
    /// <summary>The longest line kept, in characters.</summary>
    public const int MaxLineLength = 400;

    /// <summary>What replaces a token-like run.</summary>
    public const string TokenMark = "[token removed]";

    /// <summary>What replaces a line that may carry prompt or transcript text.</summary>
    public const string PromptMark = "[line removed: it may hold prompt or transcript text]";

    /// <summary>What replaces the output a failed command printed.</summary>
    public const string OutputMark = "[output removed]";

    /// <summary>The markers that make a whole line prompt or transcript text (compared without case).</summary>
    private static readonly string[] PromptMarkers =
        ["prompt:", "brief:", "transcript:", "\"prompt\"", "\"text\":", "\"content\":", "\"role\":"];

    private static readonly Regex CliOutput = new(@"(did not print an id:).*$", RegexOptions.Compiled);
    private static readonly Regex Guid = new(@"\b([0-9a-fA-F]{8})-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex Bearer = new(@"\bBearer\s+\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KeyLike = new(@"\b(?:sk|ghp|gho|ghu|ghs|ghr|github_pat|xox[abprs])[-_][A-Za-z0-9_\-]{8,}", RegexOptions.Compiled);
    private static readonly Regex LongRun = new(@"[A-Za-z0-9+/]{32,}={0,2}", RegexOptions.Compiled);
    private static readonly Regex AnyUrl = new(@"\b[A-Za-z][A-Za-z0-9+.\-]*://[^\s'""<>]*", RegexOptions.Compiled);
    private static readonly Regex SchemePrefix = new(@"^[A-Za-z][A-Za-z0-9+.\-]*://", RegexOptions.Compiled);

    /// <summary>
    /// A URL with its userinfo, query and fragment taken out (rule 3a of the redactor): the scheme, the host (and port) and the path stay, so the owner
    /// can still tell which server it was. A token in the query or the fragment, or a user name and password, never comes out of this method.
    /// The userinfo is everything up to the last "@" before the query, so a password with a "/" in it is removed too.
    /// </summary>
    public static string UrlWithoutSecrets(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var scheme = SchemePrefix.Match(url).Value;
        var rest = url[scheme.Length..];
        var query = rest.IndexOfAny(['?', '#']);
        if (query >= 0) rest = rest[..query];
        var at = rest.LastIndexOf('@');
        if (at >= 0) rest = rest[(at + 1)..];
        return scheme + rest;
    }

    /// <summary>Every line of a text, redacted; the lines are joined with the platform's line break.</summary>
    public static string Text(string text, string userProfile) =>
        string.Join(Environment.NewLine, Lines(text.Split('\n').Select(line => line.TrimEnd('\r')), userProfile));

    /// <summary>The given lines, each redacted, as a list.</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<string> lines, string userProfile) =>
        lines.Select(line => Line(line, userProfile)).ToList();

    /// <summary>One line, redacted by the rules above.</summary>
    public static string Line(string line, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (PromptMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))) return PromptMark;

        var text = CliOutput.Replace(line, $"$1 {OutputMark}");
        text = AnyUrl.Replace(text, match => UrlWithoutSecrets(match.Value));
        text = ReplaceProfile(text, userProfile);
        text = PassHealth.ReducePaths(text);
        text = Guid.Replace(text, "$1");
        text = Bearer.Replace(text, $"Bearer {TokenMark}");
        text = KeyLike.Replace(text, TokenMark);
        text = LongRun.Replace(text, TokenMark);
        return text.Length <= MaxLineLength ? text : text[..MaxLineLength] + "…";
    }

    /// <summary>The user's profile folder, in either slash form, is written as "~". An empty profile changes nothing.</summary>
    private static string ReplaceProfile(string text, string userProfile)
    {
        var profile = userProfile.TrimEnd('\\', '/');
        if (profile.Length == 0) return text;
        text = Regex.Replace(text, Regex.Escape(profile), "~", RegexOptions.IgnoreCase);
        var forward = profile.Replace('\\', '/');
        if (forward != profile) text = Regex.Replace(text, Regex.Escape(forward), "~", RegexOptions.IgnoreCase);
        return text;
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The helpers the Copilot readers share: the session folder and its events file, the id check (the same rule as Codex's transcript reader),
/// the white-space and title rules, and the time parse. Copilot's own files only; nothing here reads or writes Codex or Claude data.
/// </summary>
internal static class CopilotFiles
{
    /// <summary>The events file of a session, one JSON object per line, in its session folder.</summary>
    public const string EventsFileName = "events.jsonl";

    /// <summary>The longest title taken from a first user message (G-D16).</summary>
    public const int MaxTitleLength = 80;

    /// <summary>Characters a session id may not hold: wildcards, path separators, a drive colon and the other characters a file name refuses.</summary>
    private static readonly char[] UnsafeIdCharacters = ['*', '?', '/', '\\', ':', '"', '<', '>', '|'];

    private static readonly Regex WhiteSpace = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Opens a Copilot file for reading as text. The file is opened with sharing, so a Copilot process that is still writing it is not blocked
    /// (as Codex's rollouts are).
    /// </summary>
    public static StreamReader OpenText(string path) => new(
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true);

    /// <summary>Refuses an id that could widen a search or a path (a wildcard, a separator, a colon, <c>..</c>, a control character). Empty is refused too.</summary>
    public static void RequireSafeId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.IndexOfAny(UnsafeIdCharacters) >= 0
            || sessionId.Contains("..", StringComparison.Ordinal)
            || sessionId.Any(char.IsControl))
        {
            throw new ArgumentException("The session id is empty or has a wildcard, a path separator, a colon or '..'.", nameof(sessionId));
        }
    }

    /// <summary>The folder of one session under the history root (<c>session-state\&lt;id&gt;</c>). The id is checked by the caller.</summary>
    public static string SessionFolder(ProviderPaths paths, string sessionId) => Path.Combine(paths.HistoryRoot, sessionId);

    /// <summary>The events file of a session, or null when the session has none.</summary>
    public static string? EventsPath(ProviderPaths paths, string sessionId)
    {
        var file = Path.Combine(SessionFolder(paths, sessionId), EventsFileName);
        return File.Exists(file) ? file : null;
    }

    /// <summary>The text with every run of white space (new lines included) made one space and the ends trimmed; null when nothing is left.</summary>
    public static string? Collapse(string? text)
    {
        if (text is null) return null;
        var collapsed = WhiteSpace.Replace(text, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>
    /// The text cut to <see cref="MaxTitleLength"/> characters. A cut never splits a surrogate pair: when the first dropped character is a low
    /// surrogate, the cut is one character earlier.
    /// </summary>
    public static string? Cut(string? text)
    {
        if (text is null || text.Length <= MaxTitleLength) return text;
        var end = MaxTitleLength;
        if (char.IsLowSurrogate(text[end])) end--;
        return text[..end];
    }

    /// <summary>A time in the text form the files use, in UTC when the text gives no zone; null when it does not parse.</summary>
    public static DateTimeOffset? ParseTime(string? text) =>
        text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    /// <summary>The trimmed value of a key, or null when the key is missing or blank.</summary>
    public static string? Value(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

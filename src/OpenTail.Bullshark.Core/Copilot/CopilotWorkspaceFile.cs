using System.Text;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>How a session's <c>workspace.yaml</c> came out: not there, read, or not understood.</summary>
public enum CopilotWorkspaceState
{
    Missing,
    Readable,
    Unreadable,
}

/// <summary>A <c>workspace.yaml</c> read: its state, and its key values (empty unless the state is <see cref="CopilotWorkspaceState.Readable"/>).</summary>
public sealed record CopilotWorkspaceRead(CopilotWorkspaceState State, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// The flat <c>key: value</c> reader for a Copilot session's <c>workspace.yaml</c> (G1.2a, G-D3). It reads top-level keys only. A value may be
/// quoted with double or single quotes (the quotes are stripped); a plain value is taken as it is. Keys it does not use are kept and ignored by
/// the callers. A line it does not understand (a nested or list line, a line with no key, an unterminated quote, a line over 16 MB) makes the whole
/// file unreadable: the caller counts it and goes on without its values. Nothing is written.
/// </summary>
public static class CopilotWorkspaceFile
{
    /// <summary>The file's name in each session folder.</summary>
    public const string FileName = "workspace.yaml";

    /// <summary>A top-level key at the start of a line, a colon, then either the end of the line or a space or tab before the value.</summary>
    private static readonly Regex KeyLine = new(@"\A([A-Za-z_][A-Za-z0-9_-]*):(?:[ \t]+(.*))?\z", RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Reads the file at <paramref name="path"/>. A missing file is <see cref="CopilotWorkspaceState.Missing"/>; an IO failure is unreadable.</summary>
    public static CopilotWorkspaceRead Read(string path)
    {
        if (!File.Exists(path)) return new CopilotWorkspaceRead(CopilotWorkspaceState.Missing, NoValues);
        try
        {
            using var reader = CopilotFiles.OpenText(path);
            var lines = new List<string>();
            foreach (var line in RolloutReader.Lines(reader, CancellationToken.None))
            {
                if (line.Overlong) return Unreadable();
                lines.Add(line.Text);
            }
            return Parse(lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable();
        }
    }

    /// <summary>
    /// The values of the lines. A blank line and a line that starts with <c>#</c> are skipped. A later key replaces an earlier one. Any other line
    /// that does not match the top-level shape makes the result unreadable.
    /// </summary>
    public static CopilotWorkspaceRead Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0) continue;
            if (line[0] == '#') continue;
            var match = KeyLine.Match(line);
            if (!match.Success) return Unreadable();
            if (!TryScalar(match.Groups[2].Value, out var value)) return Unreadable();
            values[match.Groups[1].Value] = value;
        }
        return new CopilotWorkspaceRead(CopilotWorkspaceState.Readable, values);
    }

    private static CopilotWorkspaceRead Unreadable() => new(CopilotWorkspaceState.Unreadable, NoValues);

    /// <summary>
    /// The value text: trimmed; a double-quoted value loses its quotes and <c>\"</c> and <c>\\</c> are unescaped; a single-quoted value loses its
    /// quotes and <c>''</c> becomes one apostrophe. False for a quote that is not closed.
    /// </summary>
    private static bool TryScalar(string raw, out string value)
    {
        var text = raw.Trim();
        value = string.Empty;
        if (text.Length == 0) return true;
        var quote = text[0];
        if (quote is not ('"' or '\''))
        {
            value = text;
            return true;
        }
        if (text.Length < 2 || text[^1] != quote) return false;
        var inner = text[1..^1];
        value = quote == '"' ? UnescapeDouble(inner) : inner.Replace("''", "'", StringComparison.Ordinal);
        return true;
    }

    /// <summary>The inside of a double-quoted value: <c>\"</c> is a quote and <c>\\</c> is a backslash; any other character is kept.</summary>
    private static string UnescapeDouble(string inner)
    {
        var text = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length && (inner[i + 1] == '"' || inner[i + 1] == '\\'))
            {
                text.Append(inner[i + 1]);
                i++;
                continue;
            }
            text.Append(inner[i]);
        }
        return text.ToString();
    }
}

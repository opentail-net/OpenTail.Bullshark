using System.Text;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// The checkboxes of a plan file and how many of them are done (plan progress on the rail, 2026-11-18). <see cref="Done"/> and <see cref="Total"/>
/// count the list items that are checked (<c>[x]</c> or <c>[X]</c>) and all the list items with a box. <see cref="Next"/> is the text of the first
/// item that is not done, cut to <see cref="PlanProgress.MaxNextLength"/> characters, or null when there is none or it has no text.
/// </summary>
public sealed record PlanProgressCount(int Done, int Total, string? Next);

/// <summary>
/// Reads the checkboxes of a Markdown plan file. Only a list item with a box counts: a dash, any indentation, then <c>[ ]</c>, <c>[x]</c>,
/// <c>[X]</c> or <c>[!]</c> (not done). A line inside a fenced code block does not count. Pure in <see cref="Parse"/>; <see cref="ReadFile"/>
/// reads at most <see cref="MaxBytes"/> and never throws. The plan's text is shown only in a tooltip and is never logged.
/// </summary>
public static class PlanProgress
{
    /// <summary>The longest part of a plan that is read. A longer file is counted on its first 512 KB only.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>The longest <see cref="PlanProgressCount.Next"/> text. A longer one is cut and ends with an ellipsis.</summary>
    public const int MaxNextLength = 80;

    private static readonly Regex Item = new(@"^\s*-\s+\[([ xX!])\](?:\s+(.*))?$", RegexOptions.CultureInvariant);

    /// <summary>The counts of a plan's lines. Lines may end with CRLF or LF; a fence opens with three or more backticks or tildes.</summary>
    public static PlanProgressCount Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var done = 0;
        var total = 0;
        string? next = null;
        var fence = string.Empty;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (fence.Length > 0)
            {
                if (IsClosingFence(trimmed, fence)) fence = string.Empty;
                continue;
            }
            var opening = FenceOf(trimmed);
            if (opening.Length > 0)
            {
                fence = opening;
                continue;
            }

            var match = Item.Match(line);
            if (!match.Success) continue;
            total++;
            var box = match.Groups[1].Value;
            if (box is "x" or "X")
            {
                done++;
                continue;
            }
            if (next is null)
            {
                var text = match.Groups[2].Value.Trim();
                if (text.Length > 0) next = Cut(text);
            }
        }
        return new PlanProgressCount(done, total, next);
    }

    /// <summary>
    /// The counts of a plan file, or null when it is missing or cannot be read. The file is opened for shared reading, so the session that writes
    /// it is not held up. Only its first <see cref="MaxBytes"/> are read. Never throws.
    /// </summary>
    public static PlanProgressCount? ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaxBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            // A byte order mark is dropped, so the first line is read like the others.
            var text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('\uFEFF');
            return Parse(text.Split('\n'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The plan path the owner typed or chose, as it is stored: surrounding spaces and a pair of double quotes (as Windows copies a path) are
    /// removed. Empty or blank gives null, which means no plan file.
    /// </summary>
    public static string? PathOf(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"') trimmed = trimmed[1..^1].Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>True for a full path, such as <c>C:\work\plan.md</c>. A relative path would be read from the app's own folder, so it is refused.</summary>
    public static bool IsFullPath(string path) => Path.IsPathFullyQualified(path);

    /// <summary>The fence this line opens: its run of backticks or tildes (three or more), or empty.</summary>
    private static string FenceOf(string trimmed)
    {
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~')) return string.Empty;
        var run = 0;
        while (run < trimmed.Length && trimmed[run] == trimmed[0]) run++;
        return run >= 3 ? trimmed[..run] : string.Empty;
    }

    /// <summary>A line that closes the fence: the same character, at least as many, and nothing else.</summary>
    private static bool IsClosingFence(string trimmed, string fence)
    {
        var end = trimmed.TrimEnd();
        return end.Length >= fence.Length && end.All(c => c == fence[0]);
    }

    /// <summary>The text cut to <see cref="MaxNextLength"/> characters: the last one is an ellipsis when it is cut.</summary>
    private static string Cut(string text) =>
        text.Length <= MaxNextLength ? text : string.Concat(text.AsSpan(0, MaxNextLength - 1), "…");
}

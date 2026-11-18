using System.Globalization;
using System.Text;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Reports;

/// <summary>
/// "Copy as Markdown" on the Away page (batch B2): the digest the page shows, as Markdown for the owner to paste into a note or a chat. It
/// has the page's fields and no others, and the last reply is cut as the page cuts it (<see cref="AwayDigest.TrimmedText"/>). Nothing here
/// is sent to a session, and nothing is written to a file.
/// </summary>
public static class AwayExport
{
    /// <summary>
    /// The digest as Markdown: a title, the period, then one section per kind of news, in the page's order, one bullet per row. A section with
    /// nothing in it is left out; with nothing at all, "Nothing happened in this period." is said. Text from the transcripts and the log is
    /// flattened to one line and its Markdown characters are escaped, so it reads as typed.
    /// </summary>
    public static string Markdown(AwayDigest digest, DateTimeOffset since, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.Append("# While you were away\n\n");
        text.Append("From ").Append(LocalTime(since)).Append(" to ").Append(LocalTime(now)).Append(".\n\n");
        if (!digest.HasNews) text.Append("Nothing happened in this period.\n\n");

        if (digest.Rows.Count > 0) Section(text, "Sessions, newest first", digest.Rows.Select(SessionLine));
        if (digest.Events.Count > 0) Section(text, "Supervisor events, oldest first", digest.Events.Select(EventLine));
        if (digest.LimitHits.Count > 0) Section(text, "Limits reached, oldest first", digest.LimitHits.Select(LimitLine));
        if (digest.QuietSessions.Count > 0) Section(text, "Quiet sessions", digest.QuietSessions.Select(QuietLine));

        return text.ToString().TrimEnd() + "\n";
    }

    private static void Section(StringBuilder text, string heading, IEnumerable<string> lines)
    {
        text.Append("## ").Append(heading).Append("\n\n");
        foreach (var line in lines) text.Append("- ").Append(line).Append('\n');
        text.Append('\n');
    }

    /// <summary>One session, with the page's fields: name, role, state, last activity, files, failed calls and the last reply.</summary>
    private static string SessionLine(AwayRow row)
    {
        var mark = row.NeedsYou ? ", needs you now" : string.Empty;
        var activity = row.LastActivity is { } at ? $"last activity {LocalTime(at)}" : "no transcript yet";
        var files = row.FilesWritten == 0
            ? "no files written"
            : $"{row.FilesWritten} {(row.FilesWritten == 1 ? "file" : "files")} written in the whole session: " +
              Clean(string.Join(", ", row.FirstFiles)) +
              (row.FilesWritten > row.FirstFiles.Count ? $" and {row.FilesWritten - row.FirstFiles.Count} more" : string.Empty);
        var failed = $"{row.FailedToolCalls} failed tool {(row.FailedToolCalls == 1 ? "call" : "calls")}";
        var reply = AwayDigest.TrimmedText(row.LastText) is { } last ? $" Last reply: \"{Clean(last)}\"." : " No reply text in the transcript.";
        return $"**{Clean(row.Name)}** ({row.Role.ToString().ToLowerInvariant()}, {Clean(row.State)}{mark}): {activity}; {files}; {failed}.{reply}";
    }

    private static string EventLine(SupervisorEvent evt) =>
        string.Join(" · ", new[] { LocalTime(evt.At), Clean(evt.Session), Clean(evt.Event), Clean(evt.Detail) }.Where(part => part.Length > 0));

    private static string LimitLine(LimitHit hit) =>
        $"{LocalTime(hit.Time)} {Clean(LimitStatus.LabelOf(hit.Type))} limit, resets {LocalTime(hit.ResetsAt)}";

    private static string QuietLine(QuietSession quiet) => Clean(quiet.Name) + (quiet.NeedsYou ? " (needs you)" : string.Empty);

    /// <summary>The text on one line, with its white space collapsed, and its Markdown characters escaped.</summary>
    private static string Clean(string text) => Escape(string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

    /// <summary>
    /// A backslash before each character that Markdown reads as markup (emphasis, code, links, HTML, tables, strikethrough, entities), so the
    /// text shows as typed. The backslash itself is escaped too.
    /// </summary>
    private static string Escape(string text)
    {
        const string special = "\\`*_[]<>|&~";
        var escaped = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (special.IndexOf(ch) >= 0) escaped.Append('\\');
            escaped.Append(ch);
        }
        return escaped.ToString();
    }

    private static string LocalTime(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.Text;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The text that says what the session list shows (R9). The window compares the text of the list before and after a scoped refresh, and
/// redraws the rail only when they differ, so a refresh with no visible change leaves the scroll and the selection as they are.
/// </summary>
public static class SessionListSignature
{
    /// <summary>
    /// The signature of the sessions, in the order given. It lists each session's id, last activity (UTC ticks, or "-"), assistant messages (the
    /// count the row shows; turns are not drawn, so they are not listed), title, project
    /// folder (the rail's group key) and working folder (the name the rail and Storage show). The order is part of it: the rail sorts with a
    /// stable sort that keeps the index order for sessions with equal activity, so a reordering can move a row. Each text is written with its
    /// length first, so no two lists give the same text by accident.
    /// </summary>
    public static string Of(IEnumerable<SessionDigest> sessions)
    {
        var text = new StringBuilder();
        foreach (var session in sessions)
        {
            Field(text, session.SessionId);
            Field(text, session.LastActivity is { } last ? last.UtcTicks.ToString(CultureInfo.InvariantCulture) : "-");
            Field(text, session.AssistantMessages.ToString(CultureInfo.InvariantCulture));
            Field(text, session.Title);
            Field(text, session.ProjectFolder);
            Field(text, session.Cwd ?? "-");
            text.Append('\n');
        }
        return text.ToString();
    }

    private static void Field(StringBuilder text, string value) => text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
}

using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Continue;

/// <summary>
/// Picks the session Continue opens: the most recently active one in the listed sessions, the "last thing worked on". Pure: everything it needs
/// is passed in. It reads no file and no clock.
/// </summary>
/// <remarks>
/// Rules. A session whose <see cref="SessionSummary.IsSubagent"/> is true is skipped; null is not a subagent (Copilot's subagents are null, a known
/// limit). A session whose library mark is archived is skipped. A null <see cref="SessionSummary.LastActivity"/> sorts last: it is chosen only when no
/// dated session is left. A tie on the activity time goes to the key text that sorts first (ordinal), so the same input gives the same result.
/// </remarks>
public static class ContinueChooser
{
    /// <param name="sessions">The listed sessions: one provider's, or every provider's for the scope All.</param>
    /// <param name="marks">
    /// The library marks keyed by the full key text, <c>provider:id</c> (<see cref="SessionKey.ToString"/>). A bare-id view (for example Claude's
    /// <c>SessionLibrary.Load</c>) does not match these keys, so it must not be passed.
    /// </param>
    /// <param name="onlyProvider">When set, only that provider's sessions are considered. Null considers the sessions of every provider.</param>
    /// <returns>The session to continue, or null when no session is left.</returns>
    public static SessionSummary? Choose(
        IReadOnlyList<SessionSummary> sessions,
        IReadOnlyDictionary<string, LibraryEntry> marks,
        ProviderId? onlyProvider)
    {
        SessionSummary? chosen = null;
        foreach (var session in sessions)
        {
            if (session.IsSubagent == true) continue;
            if (onlyProvider is { } provider && session.Key.Provider != provider) continue;
            if (marks.TryGetValue(session.Key.ToString(), out var mark) && mark.Archived) continue;
            if (chosen is null || IsMoreRecent(session, chosen)) chosen = session;
        }
        return chosen;
    }

    /// <summary>True when <paramref name="a"/> is to be continued before <paramref name="b"/>.</summary>
    private static bool IsMoreRecent(SessionSummary a, SessionSummary b)
    {
        if (a.LastActivity is null) return b.LastActivity is null && KeyBefore(a, b);
        if (b.LastActivity is null) return true;
        var order = a.LastActivity.Value.CompareTo(b.LastActivity.Value);
        return order != 0 ? order > 0 : KeyBefore(a, b);
    }

    private static bool KeyBefore(SessionSummary a, SessionSummary b) =>
        string.CompareOrdinal(a.Key.ToString(), b.Key.ToString()) < 0;
}

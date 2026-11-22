using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The sessions of one folder that a newest-in-folder resume may pick (R7). <see cref="Sessions"/> are newest first, by last activity. The first
/// <see cref="TopRankCount"/> of them share the newest activity time: those are the candidates at the top rank, and more than one of them is ambiguous.
/// </summary>
public sealed record FolderCandidates(IReadOnlyList<SessionSummary> Sessions, int TopRankCount)
{
    /// <summary>The id of the newest candidate, or null when there is none.</summary>
    public string? NewestId => Sessions.Count > 0 ? Sessions[0].Key.SessionId : null;

    /// <summary>
    /// The earliest of the candidates at the top rank: the one with the earliest first activity (an unknown start sorts last), then the lower id. Null
    /// when there is no candidate. A single candidate is its own earliest.
    /// </summary>
    public string? EarliestTopRankId =>
        TopRankCount == 0 ? null
        : Sessions.Take(TopRankCount)
            .OrderBy(s => s.FirstActivity ?? DateTimeOffset.MaxValue)
            .ThenBy(s => s.Key.SessionId, StringComparer.Ordinal)
            .First().Key.SessionId;
}

/// <summary>
/// Queries over the Codex history for the resume ladder (R7). Pure over the <see cref="SessionSummary"/> values it is given: nothing is read from disk.
/// </summary>
public static class CodexHistoryQuery
{
    /// <summary>
    /// The sessions whose working folder is <paramref name="folder"/> (case-insensitive), newest first by last activity, for the newest-in-folder
    /// step. A session with no last activity is left out, since its age is unknown and it cannot be the newest. A session with the id
    /// <paramref name="exceptId"/> is left out (the saved session, whose file was not found). A session id that appears twice is counted once, as
    /// its newest entry. A blank or null folder gives no candidates.
    /// </summary>
    public static FolderCandidates NewestInFolderCandidates(IEnumerable<SessionSummary> sessions, string? folder, string? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(folder)) return new FolderCandidates([], 0);
        var ranked = sessions
            .Where(s => s.Folder is { } f && string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.LastActivity is not null)
            .Where(s => exceptId is null || !string.Equals(s.Key.SessionId, exceptId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.LastActivity)
            .DistinctBy(s => s.Key.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var top = ranked.Count == 0 ? 0 : ranked.Count(s => s.LastActivity == ranked[0].LastActivity);
        return new FolderCandidates(ranked, top);
    }
}

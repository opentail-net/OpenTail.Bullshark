namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The order of past sessions in the rail's History section. Subagent transcripts are the least interesting entries (owner, 2026-11-18), so
/// they come after the sessions in every list, and a folder is placed by its newest session, not by its newest subagent. Inside each part
/// the newest comes first.
/// </summary>
public static class HistoryOrder
{
    /// <summary>The sessions, main sessions first and subagents last; newest first inside each part.</summary>
    public static IEnumerable<SessionDigest> Sorted(IEnumerable<SessionDigest> digests) =>
        digests
            .OrderBy(d => d.IsSubagent)
            .ThenByDescending(d => d.LastActivity ?? DateTimeOffset.MinValue);

    /// <summary>
    /// How recent a folder is: its newest main session. A folder that holds only subagent transcripts is placed by those, but after
    /// every folder that has a main session (see <see cref="FolderRank"/>).
    /// </summary>
    public static DateTimeOffset FolderRecency(IEnumerable<SessionDigest> folder)
    {
        var list = folder as IReadOnlyCollection<SessionDigest> ?? folder.ToList();
        var main = list.Where(d => !d.IsSubagent).ToList();
        var source = main.Count > 0 ? main : list;
        return source.Max(d => d.LastActivity ?? DateTimeOffset.MinValue);
    }

    /// <summary>0 for a folder with at least one main session, 1 for a folder of subagent transcripts only. Sort by this first.</summary>
    public static int FolderRank(IEnumerable<SessionDigest> folder) => folder.Any(d => !d.IsSubagent) ? 0 : 1;
}

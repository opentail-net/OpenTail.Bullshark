namespace OpenTail.Bullshark.Core.Git;

/// <summary>
/// The big-deletion check of the Changes tab (features batch G2): a label that says a changed file lost most of its lines, so the owner
/// checks it. It is a label, not an alarm: generated files and deliberate removals are flagged too, and nothing is sent, sounded or blocked.
/// </summary>
public static class DeletionCheck
{
    /// <summary>
    /// ASSUMPTION (the orchestrator's, 2026-11-18, not measured): a file is flagged when at least this many lines were removed and the removed
    /// lines are at least <see cref="MostlyDeletedRatio"/> times the added ones. Change both here only.
    /// </summary>
    public const int MinDeletedLines = 50;

    /// <summary>ASSUMPTION (see <see cref="MinDeletedLines"/>): the removed lines must be this many times the added lines.</summary>
    public const int MostlyDeletedRatio = 3;

    /// <summary>
    /// The reason a changed file is flagged, or null. A tracked file that is deleted is flagged as "deleted". A file with counts that lost
    /// most of its lines is "mostly deleted (-N +M)". A binary file, or one whose counts are not known, is never flagged.
    /// </summary>
    public static string? Flag(GitFileChange file)
    {
        if (file.Label == "deleted") return "deleted";
        if (file.Binary || file.Added is not { } added || file.Deleted is not { } deleted) return null;
        if (deleted >= MinDeletedLines && deleted >= MostlyDeletedRatio * added)
            return $"mostly deleted (-{deleted} +{added})";
        return null;
    }
}

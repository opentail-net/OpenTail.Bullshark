namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The stamp of one folder of transcripts (R9): how many transcripts it holds, the newest write time among them, and their total length. Two
/// listings of a folder with equal stamps are taken to show the same files; a change of any of the three shows as a different stamp.
/// </summary>
/// <param name="Count">The number of <c>*.jsonl</c> files directly in the folder.</param>
/// <param name="NewestTicks">The newest <c>LastWriteTimeUtc</c> among them, in UTC ticks.</param>
/// <param name="TotalLength">The sum of their lengths in bytes.</param>
public readonly record struct FolderStamp(int Count, long NewestTicks, long TotalLength);

/// <summary>
/// Makes the folder stamps from a listing of transcripts (R9). It reads only what the listing already holds (no second listing, no extra
/// stat), so the stamp costs nothing beyond the listing. A folder is the directory that directly holds the file, compared without case.
/// </summary>
public static class FolderStamps
{
    /// <summary>The key of the folder a transcript is in: the directory that holds the file, or empty when it has none.</summary>
    public static string KeyOf(FileInfo file) => file.DirectoryName ?? string.Empty;

    /// <summary>The stamp of each folder that holds at least one of the files, keyed by <see cref="KeyOf"/> (compared without case).</summary>
    public static Dictionary<string, FolderStamp> Of(IEnumerable<FileInfo> files)
    {
        var stamps = new Dictionary<string, FolderStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var key = KeyOf(file);
            var written = file.LastWriteTimeUtc.Ticks;
            stamps[key] = stamps.TryGetValue(key, out var soFar)
                ? new FolderStamp(soFar.Count + 1, Math.Max(soFar.NewestTicks, written), soFar.TotalLength + file.Length)
                : new FolderStamp(1, written, file.Length);
        }
        return stamps;
    }
}

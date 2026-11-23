namespace OpenTail.Bullshark.Core.Storage;

/// <summary>
/// Moves a log aside once it has reached a size (dedup review item 5, 2026-11-23). The app log and the events log each keep one rotated copy, which the
/// next rotation replaces. One rule for both, so the size check and the failure rule cannot drift apart.
/// </summary>
public static class LogRotation
{
    /// <summary>
    /// Moves <paramref name="path"/> to <paramref name="rotatedPath"/>, replacing that copy, when the file is at least <paramref name="limit"/> bytes long.
    /// A missing file, or a smaller one, is left as it is. A move that fails because another program holds the file, or the file is denied, leaves the
    /// file where it is, and the error is returned so the caller can say so. Other errors are not caught here.
    /// </summary>
    /// <returns>The error of a move that failed, or null when the file was moved or was not due to be.</returns>
    public static Exception? RotateIfAtLeast(string path, string rotatedPath, long limit)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < limit) return null;
        try
        {
            File.Move(path, rotatedPath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex;
        }
    }
}

using System.IO;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The session's folder, made again before anything runs in it (owner, 2026-11-22): the starts in each platform, the Codex summary in the folder, and the
/// cross starts. The folder rule is <see cref="SessionFolder"/>.
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// Makes the session's folder again when it is gone, before anything runs in it. Returns true when the folder is there and the start (or the summary)
    /// may go on. Otherwise returns false, with the refusal to show. The log holds the count of folders made, never the path.
    /// </summary>
    private bool EnsureSessionFolder(string folder, out string? refusal)
    {
        refusal = null;
        if (string.IsNullOrWhiteSpace(folder))
        {
            refusal = SessionFolder.NoFolderRefusal;
            return false;
        }
        try
        {
            switch (SessionFolder.Ensure(folder, out var madeCount))
            {
                case FolderMake.Exists:
                    return true;
                case FolderMake.Made:
                    AppLog.Write($"session folder made again: {madeCount} folder(s)");
                    return true;
                case FolderMake.Invalid:
                    refusal = SessionFolder.InvalidRefusal;
                    return false;
                default:
                    AppLog.Write("session folder not made: its drive is not there");
                    refusal = SessionFolder.DriveMissingRefusal;
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"session folder not made: {ex.GetType().Name}");
            refusal = SessionFolder.NotMadeRefusal;
            return false;
        }
    }
}

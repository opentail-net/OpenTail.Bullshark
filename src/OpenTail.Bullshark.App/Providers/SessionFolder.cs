using System.IO;

namespace OpenTail.Bullshark.App;

/// <summary>What is decided about a session's folder before anything runs in it (owner, 2026-11-22).</summary>
public enum FolderMake
{
    /// <summary>The folder is there: nothing is made.</summary>
    Exists,

    /// <summary>The folder is gone and its drive is there: the exact recorded path is made, with every missing level of it.</summary>
    Made,

    /// <summary>The folder is gone and its drive is not there (an unplugged or unmapped drive): nothing is made.</summary>
    DriveMissing,

    /// <summary>The path is not an absolute, valid folder path: nothing is made.</summary>
    Invalid,
}

/// <summary>
/// The session's folder, made again when it is gone, before a start or a summary runs in it (owner, 2026-11-22). Only the exact recorded path is made,
/// with the levels above it that are missing. Nothing else is created, and nothing is made when the drive of the path is not there.
/// <see cref="MakeFolder"/> is the decision and is pure; <see cref="Ensure"/> does the disk work that the decision allows.
/// </summary>
public static class SessionFolder
{
    /// <summary>The refusal when the folder's drive is not there: nothing is made.</summary>
    public const string DriveMissingRefusal = "Not started: the session's folder is on a drive that is not there, so it was not made.";

    /// <summary>The refusal when the folder's path is not a valid absolute folder path.</summary>
    public const string InvalidRefusal = "Not started: the session's folder path is not valid.";

    /// <summary>The refusal when a session has no folder on record, so there is nothing to make.</summary>
    public const string NoFolderRefusal = "Not started: the session has no folder on record, so Start cannot make one.";

    /// <summary>The refusal when the folder could not be made (a disk or permission error).</summary>
    public const string NotMadeRefusal = "Not started: the session's folder could not be made.";

    /// <summary>The refusal of a Claude start when the folder was made just now and Claude does not trust it yet.</summary>
    public const string ClaudeNotTrustedAfterMake =
        "Not started: the session's folder was made again, but Claude does not trust it yet. Trust it in Claude, then start again.";

    /// <summary>
    /// What to do with a session's folder: it exists (nothing), it is gone and its drive is there (make it), or it is gone and its drive is not there
    /// (nothing is made). Pure: the caller gives what is on disk.
    /// </summary>
    /// <param name="folder">The exact recorded path.</param>
    /// <param name="exists">True when the folder exists.</param>
    /// <param name="driveExists">True when the path is absolute and its drive root exists.</param>
    public static FolderMake MakeFolder(string folder, bool exists, bool driveExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (exists) return FolderMake.Exists;
        return driveExists ? FolderMake.Made : FolderMake.DriveMissing;
    }

    /// <summary>
    /// Makes the recorded folder when it is gone, on disk, and returns what was decided. <paramref name="madeCount"/> is the number of folders made (the
    /// folder and each missing level above it), and 0 otherwise. Nothing is created unless the path is absolute and its drive root exists.
    /// </summary>
    public static FolderMake Ensure(string folder, out int madeCount)
    {
        madeCount = 0;
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var trimmed = folder.Trim();
        if (!Path.IsPathFullyQualified(trimmed)) return FolderMake.Invalid;
        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FolderMake.Invalid;
        }
        var root = Path.GetPathRoot(full);
        var driveExists = !string.IsNullOrEmpty(root) && Directory.Exists(root);
        var decision = MakeFolder(full, Directory.Exists(full), driveExists);
        if (decision != FolderMake.Made) return decision;
        for (var level = full; level is not null && !Directory.Exists(level); level = Path.GetDirectoryName(level)) madeCount++;
        Directory.CreateDirectory(full);
        return FolderMake.Made;
    }
}

namespace OpenTail.Bullshark.App;

/// <summary>
/// The folder notice of a resume screen: the words shown on the screen when the session's folder is not on record or is missing, and whether it blocks
/// Start. A missing folder is shown and does not block Start: the start makes it again first (owner, 2026-11-22). A session with no folder on record is
/// blocked, as there is no folder to make. Previews and the choosers are never blocked by it. Pure: it reads no window and no file.
/// </summary>
public static class ResumeFolderNotice
{
    /// <summary>The notice for the screen, or null when the folder is set and exists (Start may go on).</summary>
    /// <param name="folder">The session's folder as the history gives it (may be null or blank).</param>
    /// <param name="folderExists">True when the folder exists on disk.</param>
    /// <returns>
    /// Null when the folder is set and exists. A null or blank folder has no folder on record. A folder that is set but does not exist is missing; the
    /// notice names it, and Start makes it again.
    /// </returns>
    public static string? For(string? folder, bool folderExists)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "The session has no folder on record, so Start cannot make one. Start is blocked.";
        return folderExists
            ? null
            : $"The session's folder is missing: {folder}. Start will make it again before it starts.";
    }

    /// <summary>
    /// True when the notice blocks Start: only a session with no folder on record. A null <paramref name="folderExists"/> means no folder check, so no
    /// notice and no block.
    /// </summary>
    /// <param name="folder">The session's folder as the history gives it (may be null or blank).</param>
    /// <param name="folderExists">Whether the folder exists, or null when the screen does not check it.</param>
    public static bool BlocksStart(string? folder, bool? folderExists) =>
        folderExists is not null && string.IsNullOrWhiteSpace(folder);
}

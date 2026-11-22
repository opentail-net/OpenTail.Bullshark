namespace OpenTail.Bullshark.App;

/// <summary>
/// The folder notice of a resume screen: the words shown on the screen when the session's folder is not on record or is missing, and whether they block
/// Start (and Full). Previews and the choosers are never blocked by it. Pure: it reads no window and no file. The screen checks the folder and passes the
/// result in, and the Start-time checks of each start still run.
/// </summary>
public static class ResumeFolderNotice
{
    /// <summary>The notice for the screen, or null when the folder is set and exists (Start may go on).</summary>
    /// <param name="folder">The session's folder as the history gives it (may be null or blank).</param>
    /// <param name="folderExists">True when the folder exists on disk.</param>
    /// <returns>
    /// Null when the folder is set and exists. A null or blank folder has no folder on record. A folder that is set but does not exist is missing; the
    /// notice names it.
    /// </returns>
    public static string? For(string? folder, bool folderExists)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "The session has no folder on record, so it cannot be started. Summary and last turns can still be previewed.";
        return folderExists
            ? null
            : $"The session's folder is missing: {folder}. Start needs it, so it is blocked. Summary and last turns can still be previewed.";
    }

    /// <summary>True when the notice blocks Start: any notice does, and no notice (null) does not.</summary>
    public static bool BlocksStart(string? notice) => notice is not null;
}

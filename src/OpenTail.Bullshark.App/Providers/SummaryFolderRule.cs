namespace OpenTail.Bullshark.App;

/// <summary>
/// The folder rule of a Codex summary: Codex runs its summary (<c>codex exec</c>) in the session's folder, so with the folder missing the process cannot
/// start, and no summary is made. Pure: the window gives whether the folder exists.
/// </summary>
public static class SummaryFolderRule
{
    /// <summary>The refusal of a summary while the session's folder is missing, or null when the summary may be made.</summary>
    /// <param name="folderExists">True when the session's folder exists on disk.</param>
    public static string? Refusal(bool folderExists) =>
        folderExists ? null : "The summary was not made: the session's folder is missing, and Codex runs its summary in that folder.";
}

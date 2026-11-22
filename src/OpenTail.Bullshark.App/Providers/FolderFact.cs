namespace OpenTail.Bullshark.App;

/// <summary>
/// The folder part of a resume screen's facts line: "in {folder}" when the session has a folder on record, and "no folder on record" when it has none
/// (null or blank). Pure. A folder on record gives the same text as before.
/// </summary>
public static class FolderFact
{
    /// <summary>The folder words of the facts line for a session's folder (may be null or blank).</summary>
    public static string For(string? folder) => string.IsNullOrWhiteSpace(folder) ? "no folder on record" : "in " + folder;
}

using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The files a session's transcript says it wrote, for the Changes tab (round 5 item 11). A transcript that is missing is a note, not an
/// error. A transcript that cannot be read (a folder the user cannot open, or a file another program holds) is a note with the reason, so
/// the tab shows it instead of staying on "Reading the changes…". The failures are the ones <see cref="StoreErrors"/> lists.
/// </summary>
public static class TranscriptWrites
{
    /// <summary>The files the session wrote, or null with the note to show instead.</summary>
    public static (IReadOnlyList<string>? Files, string? Note) Of(string projectsRoot, string sessionId)
    {
        try
        {
            var path = TranscriptReader.FindTranscript(projectsRoot, sessionId);
            if (path is null) return (null, "No transcript for this session yet.");
            return (TranscriptReader.ReadFile(path).FilesWritten ?? [], null);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            return (null, $"The transcript cannot be read: {ex.Message}");
        }
    }
}

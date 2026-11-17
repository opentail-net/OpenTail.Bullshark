using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The files a session's transcript says it wrote, for the Changes tab (round 5 item 11). A transcript that is missing is a note, not an
/// error. A transcript that cannot be read (a folder the user cannot open, or a file another program holds) is a note with the reason, so
/// the tab shows it instead of staying on "Reading the changes…". The failures are the ones <see cref="StoreErrors"/> lists.
/// </summary>
public static class TranscriptWrites
{
    /// <summary>
    /// What the transcript says, from one read (round 7 item 4): the files the session wrote, the note to show when there are none, and
    /// the folder it edits now (its latest cwd; null when no record names one). The folder is what the Changes tab, the merge check and the
    /// overlap take a session's files from, ahead of the listing's folder.
    /// </summary>
    public sealed record TranscriptFacts(IReadOnlyList<string>? Files, string? Note, string? Folder);

    /// <summary>The transcript's facts of one session. A missing or unreadable transcript is a note, with no folder.</summary>
    public static TranscriptFacts Facts(string projectsRoot, string sessionId)
    {
        try
        {
            var path = TranscriptReader.FindTranscript(projectsRoot, sessionId);
            if (path is null) return new TranscriptFacts(null, "No transcript for this session yet.", null);
            var summary = TranscriptReader.ReadFile(path);
            return new TranscriptFacts(summary.FilesWritten ?? [], null, summary.LatestCwd);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            return new TranscriptFacts(null, $"The transcript cannot be read: {ex.Message}", null);
        }
    }

    /// <summary>The files the session wrote, or null with the note to show instead.</summary>
    public static (IReadOnlyList<string>? Files, string? Note) Of(string projectsRoot, string sessionId)
    {
        var facts = Facts(projectsRoot, sessionId);
        return (facts.Files, facts.Note);
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// A provider's history: the sessions it has on disk. Implemented by every provider (Claude and Codex in stage C). A provider implements
/// only the interfaces it has (R-8); the UI asks which ones are present.
/// </summary>
public interface IHistorySource
{
    /// <summary>Lists the provider's sessions. An unreadable file is skipped, never fatal (round 12).</summary>
    Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct);
}

/// <summary>A page of a transcript. <see cref="TotalLines"/> is null when the provider cannot count without reading everything.</summary>
public sealed record TranscriptPage(IReadOnlyList<string> Lines, int FirstLine, int? TotalLines);

/// <summary>Reads a session's transcript a page at a time. A compressed transcript may need a full read to page (B1b).</summary>
public interface ITranscriptReader
{
    Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct);
}

/// <summary>The command lines a terminal tab runs. The window owns the tab; the provider only gives the command.</summary>
public interface ITerminalOpener
{
    /// <summary>The command that starts a new session in <paramref name="folder"/>.</summary>
    string NewCommandLine(string folder);

    /// <summary>The command that resumes a saved session by its id.</summary>
    string ResumeCommandLine(string sessionId);
}

/// <summary>
/// The supervisor's launch and status calls. Claude only in stage A. Its members move here when the supervisor is written against the
/// interface; Codex does not implement it (it has no background sessions, B5).
/// </summary>
public interface ISupervisedLauncher
{
}

/// <summary>
/// The provider's live list (the sessions it reports as running). Claude only in stage A. Codex has no live list in stage C (R-1):
/// its live rows are the window's own tabs.
/// </summary>
public interface ILiveSource
{
}

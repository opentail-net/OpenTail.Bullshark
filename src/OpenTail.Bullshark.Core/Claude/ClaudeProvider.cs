using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>
/// Claude Code as a provider (stage A3). It wraps the existing classes and changes none of their logic: the history is
/// <see cref="TranscriptIndex"/>, the transcript is <see cref="TranscriptFile"/> and <see cref="TranscriptReader.FindTranscript"/>, and the
/// terminal commands are the ones <see cref="ClaudeCli"/> already builds. The supervisor and live-list members are markers only: the
/// supervisor loop still calls <see cref="ClaudeCli"/> directly.
/// </summary>
public sealed class ClaudeProvider(ProviderPaths? paths = null)
    : IProvider, IHistorySource, ITranscriptReader, ITerminalOpener, ISupervisedLauncher, ILiveSource
{
    /// <summary>The window's own paths when it passes them; otherwise Claude's paths from the environment (<c>CLAUDE_CONFIG_DIR</c> honoured).</summary>
    public ProviderPaths Paths { get; } = paths ?? ProviderPaths.ForClaude();

    public ProviderId Id => ProviderId.Claude;

    public string DisplayName => "Claude Code";

    /// <summary>
    /// The full history with the digests the rail and the views use (<see cref="SessionDigest"/> is Claude-shaped, so the window reads it
    /// from here rather than from <see cref="ListAsync"/>). The cache is the same file the window has always used.
    /// </summary>
    public TranscriptIndex BuildIndex(string cachePath, Action<string>? log = null) =>
        TranscriptIndex.Build(Paths.HistoryRoot, cachePath, log);

    /// <summary>The history without a cache file: every transcript is read.</summary>
    public TranscriptIndex BuildIndex() => TranscriptIndex.Build(Paths.HistoryRoot);

    /// <summary>
    /// The full history, as <see cref="BuildIndex(string, Action{string}?)"/> gives it, with the memo that <see cref="RefreshIndex"/> needs (R9).
    /// </summary>
    public TranscriptRefresh BuildIndexWithMemo(string cachePath, Action<string>? log = null) =>
        TranscriptIndex.BuildFull(Paths.HistoryRoot, cachePath, log);

    /// <summary>
    /// The history brought up to date from the memo of an earlier read, reading only the folders that changed and the live files (R9). The
    /// accepted blind spot is documented on <see cref="TranscriptIndex.RefreshScoped"/>.
    /// </summary>
    public TranscriptRefresh RefreshIndex(TranscriptIndexMemo memo) => TranscriptIndex.RefreshScoped(Paths.HistoryRoot, memo);

    /// <summary>
    /// The sessions as the neutral summary (R-2). Each digest keeps its meaning: a count is known for Claude, and the working folder is
    /// null when the transcript names none. Unreadable transcripts are skipped by <see cref="TranscriptIndex"/>, never fatal.
    /// </summary>
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var index = await Task.Run(() => BuildIndex(), ct);
        return index.Sessions.Select(SummaryOf).ToList();
    }

    /// <summary>
    /// The neutral summary of one Claude digest. Public so the App's Continue picks from the same mapping as this list (it is not copied).
    /// </summary>
    public static SessionSummary SummaryOf(SessionDigest digest) => new(
        new SessionKey(ProviderId.Claude, digest.SessionId),
        digest.Title,
        digest.Cwd,
        digest.FirstActivity,
        digest.LastActivity,
        digest.IsSubagent,
        digest.UserMessages,
        digest.AssistantMessages,
        digest.Compactions,
        digest.LastModel);

    /// <summary>
    /// Lines of one transcript, from <paramref name="firstLine"/> (zero-based), read with the existing line reader. The count is not
    /// taken (null): Claude's pager keeps its own count, and this read does not share its scan. A session with no transcript yet throws
    /// <see cref="FileNotFoundException"/>.
    /// </summary>
    public Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct)
    {
        // A wildcard or a separator would let the search match another file, so an id with one is refused.
        if (string.IsNullOrEmpty(sessionId) || sessionId.IndexOfAny(['*', '?', '/', '\\']) >= 0)
            throw new ArgumentException("The session id is empty or has a path or wildcard character.", nameof(sessionId));

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var path = TranscriptReader.FindTranscript(Paths.HistoryRoot, sessionId)
                ?? throw new FileNotFoundException($"No transcript for session {sessionId}.", sessionId + ".jsonl");
            var start = Math.Max(0, firstLine);
            var lines = TranscriptFile.ReadLines(path).Skip(start).Take(Math.Max(0, count)).ToList();
            return new TranscriptPage(lines, start, null);
        }, ct);
    }

    /// <summary>
    /// The command a new Claude tab runs. The folder is not on the command line: the window sets the tab's working directory to it, as
    /// the trust step does.
    /// </summary>
    public string NewCommandLine(string folder) => "claude";

    /// <summary>The command that attaches a tab to a session by its short id. Throws when the id has no valid short id (F4).</summary>
    public string ResumeCommandLine(string sessionId) =>
        ClaudeCli.AttachCommandLineOf(sessionId)
        ?? throw new ArgumentException("The session id has no 8-character hexadecimal short id, so no command is built.", nameof(sessionId));
}

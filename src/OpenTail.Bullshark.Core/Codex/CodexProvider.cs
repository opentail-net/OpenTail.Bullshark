using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Codex as a provider (stage C). It implements the interfaces it has: its history (<see cref="CodexHistorySource"/>), its transcript
/// lines (<see cref="CodexTranscriptReader"/>) and its terminal commands (<see cref="CodexTerminalOpener"/>). It implements no supervisor
/// and no live list (B5, R-1), so the window hides those features for it through <see cref="ProviderFeatures.Has{T}"/>.
/// </summary>
public sealed class CodexProvider : IProvider, IHistorySource, ITranscriptReader, ITerminalOpener
{
    private readonly CodexHistorySource _history;
    private readonly CodexTranscriptReader _reader;
    private readonly CodexTerminalOpener _opener = new();

    /// <summary>The paths the provider reads: <c>CODEX_HOME</c> or <c>~\.codex</c> when none are given.</summary>
    public CodexProvider(ProviderPaths? paths = null)
    {
        Paths = paths ?? ProviderPaths.ForCodex();
        _history = new CodexHistorySource(Paths);
        _reader = new CodexTranscriptReader(Paths);
    }

    public ProviderPaths Paths { get; }

    /// <summary>
    /// True when this build can read Codex history. Both targets can: net11.0 decodes the compressed rollouts with the BCL, and net10.0 with
    /// ZstdSharp.Port (Codex-on-net10 plan, Z1 and Z2). The window offers Codex when this is true.
    /// </summary>
    public static bool Available => true;

    public ProviderId Id => ProviderId.Codex;

    public string DisplayName => "Codex";

    /// <summary>The sessions as neutral summaries (C1). Empty when <see cref="Available"/> is false.</summary>
    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) => _history.ListAsync(ct);

    /// <summary>The lines of one rollout, a page at a time (C2).</summary>
    public Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct) =>
        _reader.ReadPageAsync(sessionId, firstLine, count, ct);

    /// <summary>The command of a new Codex tab, in <paramref name="folder"/> (C3).</summary>
    public string NewCommandLine(string folder) => _opener.NewCommandLine(folder);

    /// <summary>The command of a new Codex tab with <c>-m &lt;model&gt;</c> when <paramref name="model"/> is set (P9); null keeps Codex's default.</summary>
    public string NewCommandLine(string folder, string? model) => _opener.NewCommandLine(folder, model);

    /// <summary>The command that resumes a saved Codex session by its UUID (C3).</summary>
    public string ResumeCommandLine(string sessionId) => _opener.ResumeCommandLine(sessionId);

    /// <summary>The command of the Codex picker, <c>codex resume</c> with no id: the owner chooses the session (R7).</summary>
    public string PickerCommandLine() => _opener.PickerCommandLine();

    /// <summary>
    /// True when a rollout is found for the saved id: the first check of the resume ladder (R7). It searches the history folders, so the caller runs it
    /// off the UI thread. An id with a wildcard, a path separator, a colon or <c>..</c> throws <see cref="ArgumentException"/>, as the reader does.
    /// </summary>
    public bool HasRollout(string sessionId)
    {
        CodexTranscriptReader.RequireSafeId(sessionId);
        return _reader.FindRollout(sessionId) is not null;
    }
}

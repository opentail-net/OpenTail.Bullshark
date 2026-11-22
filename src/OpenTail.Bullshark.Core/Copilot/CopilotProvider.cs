using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// GitHub Copilot CLI as a provider (stage G1). It implements the interfaces it has: its history (<see cref="CopilotHistorySource"/>), its transcript
/// messages (<see cref="CopilotTranscriptReader"/>) and its terminal commands (<see cref="CopilotTerminalOpener"/>). It implements no supervisor and
/// no live list (G-D7), so the window hides those features for it through <see cref="ProviderFeatures.Has{T}"/>. The summary call is a separate
/// class (<see cref="CopilotSummarizer"/>), not a provider interface.
/// </summary>
public sealed class CopilotProvider : IProvider, IHistorySource, ITranscriptReader, ITerminalOpener
{
    private readonly CopilotHistorySource _history;
    private readonly CopilotTranscriptReader _reader;
    private readonly CopilotTerminalOpener _opener = new();

    /// <summary>The paths the provider reads: <c>COPILOT_HOME</c> or <c>~\.copilot</c> when none are given.</summary>
    public CopilotProvider(ProviderPaths? paths = null)
    {
        Paths = paths ?? ProviderPaths.ForCopilot();
        _history = new CopilotHistorySource(Paths);
        _reader = new CopilotTranscriptReader(Paths);
    }

    public ProviderPaths Paths { get; }

    /// <summary>The history source, with its counts of skipped lines, damaged folders and empty sessions from the last listing.</summary>
    public CopilotHistorySource History => _history;

    public ProviderId Id => ProviderId.Copilot;

    public string DisplayName => "Copilot";

    /// <summary>The sessions as neutral summaries (C1).</summary>
    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) => _history.ListAsync(ct);

    /// <summary>A page of the session's messages (at most 5,000 in all).</summary>
    public Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct) =>
        _reader.ReadPageAsync(sessionId, firstLine, count, ct);

    /// <summary>The command of a new Copilot tab, in <paramref name="folder"/> (G1.5).</summary>
    public string NewCommandLine(string folder) => _opener.NewCommandLine(folder);

    /// <summary>The command that resumes a saved Copilot session by its id (no folder; the window sets it, G-D12).</summary>
    public string ResumeCommandLine(string sessionId) => _opener.ResumeCommandLine(sessionId);
}

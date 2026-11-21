using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>
/// A second provider, in memory (ProviderId "fake"), so the seam is tested without Codex. It has history and terminal commands, and no
/// supervisor or live list. Its sessions are whatever the test puts in <see cref="Sessions"/>.
/// </summary>
internal sealed class FakeProvider : IProvider, IHistorySource, ITerminalOpener
{
    public ProviderId Id { get; } = new("fake");

    public string DisplayName => "Fake";

    public ProviderPaths Paths { get; } = new(HomeRoot: "unused", HistoryRoot: "unused", ArchiveRoot: null);

    public IReadOnlyList<SessionSummary> Sessions { get; set; } = [];

    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) => Task.FromResult(Sessions);

    public string NewCommandLine(string folder) => "fake-new " + folder;

    public string ResumeCommandLine(string sessionId) => "fake-resume " + sessionId;
}

/// <summary>A provider with history only. It does not implement <see cref="ITerminalOpener"/>, so the UI must hide the terminal features.</summary>
internal sealed class HistoryOnlyProvider : IProvider, IHistorySource
{
    public ProviderId Id { get; } = new("history");

    public string DisplayName => "History only";

    public ProviderPaths Paths { get; } = new(HomeRoot: "unused", HistoryRoot: "unused", ArchiveRoot: null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionSummary>>([]);
}

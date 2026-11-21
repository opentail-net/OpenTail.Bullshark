using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Activity;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>Stage A3: the Claude provider wraps the existing classes, so its answers are theirs. Synthetic transcripts only.</summary>
public sealed class ClaudeProviderTests : IDisposable
{
    private const string SessionId = "3f2a9c1e-7b4d-4e8a-9c2f-1a2b3c4d5e6f";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-claude-provider-" + Guid.NewGuid().ToString("N"));

    private string Projects => Path.Combine(_root, "projects");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ClaudeProvider Provider() => new(new ProviderPaths(HomeRoot: _root, HistoryRoot: Projects, ArchiveRoot: Path.Combine(_root, "archive")));

    /// <summary>Writes one synthetic transcript under the projects folder, in a folder named as Claude names one.</summary>
    private string WriteTranscript(string id, params string[] lines)
    {
        var folder = Path.Combine(Projects, "C--work-demo");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, id + ".jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string UserLine(string text) =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-20T10:00:00Z\",\"cwd\":\"C:\\\\work\\\\demo\",\"message\":{\"role\":\"user\",\"content\":\"" + text + "\"}}";

    [Fact]
    public void ItIsClaudeCodeWithItsOwnPaths()
    {
        var provider = Provider();

        Assert.Equal(ProviderId.Claude, provider.Id);
        Assert.Equal("Claude Code", provider.DisplayName);
        Assert.Equal(Projects, provider.Paths.HistoryRoot);
        Assert.Equal(Path.Combine(_root, "archive"), provider.Paths.ArchiveRoot);
    }

    [Fact]
    public void ItHasTheTerminalAndMarkerInterfaces()
    {
        IProvider provider = Provider();

        Assert.True(ProviderFeatures.Has<ITerminalOpener>(provider));
        Assert.True(ProviderFeatures.Has<ITranscriptReader>(provider));
        Assert.True(ProviderFeatures.Has<ISupervisedLauncher>(provider));
        Assert.True(ProviderFeatures.Has<ILiveSource>(provider));
    }

    [Fact]
    public void TheNewAndResumeCommandsAreTheExistingClaudeCommands()
    {
        var provider = Provider();

        Assert.Equal("claude", provider.NewCommandLine("C:\\work\\demo"));
        Assert.Equal("claude attach 3f2a9c1e", provider.ResumeCommandLine(SessionId));
    }

    [Fact]
    public void AResumeWithoutAValidShortIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Provider().ResumeCommandLine("zz"));
    }

    [Fact]
    public async Task TheHistoryIsTheDigestsMappedToTheNeutralSummary()
    {
        WriteTranscript(SessionId, UserLine("Fix the login"), IndexFixture.Assistant("msg_1", "req_1", 20, "2026-11-20T10:00:05Z", "claude-test"));

        var summaries = await Provider().ListAsync(CancellationToken.None);

        var summary = Assert.Single(summaries);
        Assert.Equal(new SessionKey(ProviderId.Claude, SessionId), summary.Key);
        Assert.Equal("Fix the login", summary.Title);
        Assert.Equal("C:\\work\\demo", summary.Folder);
        Assert.Equal(false, summary.IsSubagent);
        Assert.Equal(1, summary.UserMessages);
        Assert.Equal(1, summary.AssistantMessages);
        Assert.Equal(0, summary.Compactions);
        Assert.Equal("claude-test", summary.Model);
    }

    [Fact]
    public void TheDigestsOfTheWindowAreStillReadThroughTheProvider()
    {
        WriteTranscript(SessionId, UserLine("Fix the login"));

        var index = Provider().BuildIndex();

        var digest = Assert.Single(index.Sessions);
        Assert.Equal(SessionId, digest.SessionId);
    }

    [Fact]
    public async Task ASessionWithNoTranscriptHasNoHistoryRow()
    {
        Assert.Empty(await Provider().ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ThePageStartsAtTheLineAskedFor()
    {
        WriteTranscript(SessionId, UserLine("one"), UserLine("two"), UserLine("three"));

        var page = await Provider().ReadPageAsync(SessionId, firstLine: 1, count: 5, CancellationToken.None);

        Assert.Equal(1, page.FirstLine);
        Assert.Equal(2, page.Lines.Count);
        Assert.Contains("\"two\"", page.Lines[0]);
        Assert.Contains("\"three\"", page.Lines[1]);
        Assert.Null(page.TotalLines);
    }

    [Fact]
    public async Task APageForASessionWithNoTranscriptThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => Provider().ReadPageAsync(SessionId, 0, 5, CancellationToken.None));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("..\\other")]
    [InlineData("")]
    public async Task AnIdWithAWildcardOrSeparatorIsRefused(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Provider().ReadPageAsync(id, 0, 5, CancellationToken.None));
    }
}

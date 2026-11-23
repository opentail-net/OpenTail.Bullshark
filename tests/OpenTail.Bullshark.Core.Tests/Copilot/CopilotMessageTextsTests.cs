using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// PP-D4 (2026-11-23): the message texts of a Copilot session for the message-text search. The texts are the user and assistant messages in file order,
/// without roles, and tool or other events are left out. Only a fixture folder is written.
/// </summary>
public sealed class CopilotMessageTextsTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-copilot-texts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task TheUserAndAssistantTextsAreReturnedInOrderWithoutTheOtherEvents()
    {
        var folder = Path.Combine(_root, "session-state", SessionId);
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "events.jsonl"), new[]
        {
            "{\"type\":\"user.message\",\"data\":{\"content\":\"first question\"},\"timestamp\":\"2026-11-23T10:00:00Z\"}",
            "{\"type\":\"tool.execution_start\",\"data\":{\"toolName\":\"view\"},\"timestamp\":\"2026-11-23T10:00:01Z\"}",
            "{\"type\":\"assistant.message\",\"data\":{\"content\":\"an answer\"},\"timestamp\":\"2026-11-23T10:00:02Z\"}",
            "{\"type\":\"user.message\",\"data\":{\"content\":\"second\"},\"timestamp\":\"2026-11-23T10:01:00Z\"}",
        });
        var paths = new ProviderPaths(HomeRoot: _root, HistoryRoot: Path.Combine(_root, "session-state"), ArchiveRoot: null);

        var texts = await new CopilotProvider(paths).MessageTextsAsync(SessionId, CancellationToken.None);

        Assert.Equal(new[] { "first question", "an answer", "second" }, texts);
    }
}

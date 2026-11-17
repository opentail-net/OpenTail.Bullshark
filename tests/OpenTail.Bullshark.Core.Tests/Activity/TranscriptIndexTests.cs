using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

public sealed class TranscriptIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-index-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Assistant(string messageId, string requestId, long output, string timestamp, string model = "claude-opus-5-5",
        string? contentExtra = null, long input = 5, long cacheRead = 1000, long cacheWrite = 10) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\",\"requestId\":\"" + requestId +
        "\",\"message\":{\"id\":\"" + messageId + "\",\"model\":\"" + model + "\",\"role\":\"assistant\",\"content\":[" +
        "{\"type\":\"text\",\"text\":\"answer " + messageId + "\"}" + (contentExtra ?? string.Empty) + "]," +
        "\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":" + cacheWrite + ",\"cache_read_input_tokens\":" + cacheRead + "}}}";

    private static string WriteTool(string toolId, string path) =>
        ",{\"type\":\"tool_use\",\"id\":\"" + toolId + "\",\"name\":\"Write\",\"input\":{\"file_path\":\"" + path + "\",\"content\":\"x\"}}";

    private string Project(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Create(string path, DateTime created, IEnumerable<string> lines)
    {
        File.WriteAllLines(path, lines);
        File.SetCreationTimeUtc(path, created);
    }

    [Fact]
    public void AMessageInTwoFilesIsCountedOnceInTheTotalsAndOnceInItsFirstFile()
    {
        var projects = Project("C--Git-demo");
        var first = Path.Combine(projects, "aaaaaaaa-0000-0000-0000-000000000001.jsonl");
        var second = Path.Combine(projects, "bbbbbbbb-0000-0000-0000-000000000002.jsonl");
        Create(first, new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), new[]
        {
            Assistant("m1", "r1", 100, "2026-11-01T10:00:00.000Z"),
            Assistant("m2", "r2", 50, "2026-11-01T10:01:00.000Z"),
        });
        // A resumed session copies m2 into its own file, and adds m3.
        Create(second, new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc), new[]
        {
            Assistant("m2", "r2", 50, "2026-11-01T10:01:00.000Z"),
            Assistant("m3", "r3", 30, "2026-11-02T10:00:00.000Z"),
        });

        var index = TranscriptIndex.Build(_root);

        Assert.Equal(3, index.Totals.CountedMessages);
        Assert.Equal(180, index.Totals.Total.Output);
        var a = index.Sessions.Single(s => s.SessionId.StartsWith("aaaaaaaa"));
        var b = index.Sessions.Single(s => s.SessionId.StartsWith("bbbbbbbb"));
        Assert.Equal(150, a.Usage.Output);
        Assert.Equal(30, b.Usage.Output);
    }

    [Fact]
    public void ThePlaceholderRuleKeepsTheLargestCountAcrossFiles()
    {
        var projects = Project("C--Git-streaming");
        var file = Path.Combine(projects, "cccccccc-0000-0000-0000-000000000003.jsonl");
        Create(file, DateTime.UtcNow, new[]
        {
            Assistant("m7", "r7", 8, "2026-11-03T10:00:00.000Z"),
            Assistant("m7", "r7", 435, "2026-11-03T10:00:01.000Z"),
        });

        Assert.Equal(435, TranscriptIndex.Build(_root).Totals.Total.Output);
    }

    [Fact]
    public void TheDigestHasTurnsToolsFilesAndTheContextAtTheLastTurn()
    {
        var projects = Project("C--Git-digest");
        var file = Path.Combine(projects, "dddddddd-0000-0000-0000-000000000004.jsonl");
        Create(file, DateTime.UtcNow, new[]
        {
            "{\"type\":\"user\",\"timestamp\":\"2026-11-04T09:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"hi\"}}",
            Assistant("m1", "r1", 10, "2026-11-04T09:00:05.000Z", contentExtra: WriteTool("t1", "C:/work/hello.txt"), input: 3, cacheRead: 5000, cacheWrite: 200),
            "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":4000,\"timestamp\":\"2026-11-04T09:00:09.000Z\"}",
            Assistant("m2", "r2", 20, "2026-11-04T09:10:00.000Z", input: 7, cacheRead: 9000, cacheWrite: 300),
        });

        var session = TranscriptIndex.Build(_root).Sessions.Single();

        Assert.Equal("dddddddd-0000-0000-0000-000000000004", session.SessionId);
        Assert.Equal(1, session.UserMessages);
        Assert.Equal(2, session.AssistantMessages);
        Assert.Equal(1, session.Turns);
        Assert.Equal(1, session.ToolCalls);
        Assert.Equal(1, session.ToolNames["Write"]);
        Assert.Equal(new[] { "C:/work/hello.txt" }, session.FilesWritten);
        Assert.Equal(7 + 9000 + 300, session.ContextTokensAtLastTurn);
        Assert.Equal(TimeSpan.FromMinutes(10), session.Span);
        Assert.Equal("answer m2", session.LastAssistantText);
    }

    [Fact]
    public void SubagentFilesAreMarkedAndKeepTheirOwnDigest()
    {
        var projects = Project("C--Git-sub");
        var sub = Path.Combine(projects, "subagents");
        Directory.CreateDirectory(sub);
        Create(Path.Combine(sub, "agent-1.jsonl"), DateTime.UtcNow, new[] { Assistant("s1", "rs", 4, "2026-11-05T10:00:00.000Z") });

        var session = TranscriptIndex.Build(_root).Sessions.Single();

        Assert.True(session.IsSubagent);
        Assert.Equal(4, session.Usage.Output);
    }

    [Fact]
    public void AMissingFolderGivesAnEmptyIndex()
    {
        var index = TranscriptIndex.Build(Path.Combine(_root, "absent"));

        Assert.Empty(index.Sessions);
        Assert.Equal(0, index.Totals.CountedMessages);
    }

    [Fact]
    public void InvalidLinesAreSkippedAndDoNotStopTheIndex()
    {
        var projects = Project("C--Git-broken");
        Create(Path.Combine(projects, "eeeeeeee-0000-0000-0000-000000000005.jsonl"), DateTime.UtcNow, new[]
        {
            "not json at all",
            "",
            Assistant("m9", "r9", 9, "2026-11-06T10:00:00.000Z"),
        });

        var index = TranscriptIndex.Build(_root);

        Assert.Equal(9, index.Totals.Total.Output);
        Assert.Equal(1, index.Sessions.Single().AssistantMessages);
    }

    [Fact]
    public void AFileRemovedAfterItWasListedIsSkippedAndTheOthersStillCome()
    {
        var projects = Project("C--Git-vanished");
        var kept = Path.Combine(projects, "f1f1f1f1-0000-0000-0000-000000000006.jsonl");
        var vanished = Path.Combine(projects, "f2f2f2f2-0000-0000-0000-000000000007.jsonl");
        Create(kept, DateTime.UtcNow.AddHours(-2), [Assistant("v1", "rv1", 11, "2026-11-07T10:00:00.000Z")]);
        Create(vanished, DateTime.UtcNow.AddHours(-1), [Assistant("v2", "rv2", 22, "2026-11-07T10:01:00.000Z")]);

        // The file is listed, then removed before it is read: the way Claude's own cleanup can remove a transcript.
        var index = TranscriptIndex.Build(_root, (path, ledger) =>
        {
            if (Path.GetFileName(path) == Path.GetFileName(vanished)) File.Delete(path);
            return TranscriptReader.ReadFile(path, ledger);
        });

        Assert.Equal(Path.GetFileName(kept), Path.GetFileName(Assert.Single(index.Sessions).FilePath));
        Assert.Equal(11, index.Totals.Total.Output);
        var skipped = Assert.Single(index.Skipped);
        Assert.Equal(Path.GetFileName(vanished), Path.GetFileName(skipped.FilePath));
        Assert.False(string.IsNullOrEmpty(skipped.Reason));
    }

    [Fact]
    public void AFileThatCannotBeOpenedIsSkippedAndTheOthersStillCome()
    {
        var projects = Project("C--Git-locked");
        var readable = Path.Combine(projects, "f3f3f3f3-0000-0000-0000-000000000008.jsonl");
        var locked = Path.Combine(projects, "f4f4f4f4-0000-0000-0000-000000000009.jsonl");
        Create(readable, DateTime.UtcNow.AddHours(-2), [Assistant("l1", "rl1", 33, "2026-11-08T10:00:00.000Z")]);
        Create(locked, DateTime.UtcNow.AddHours(-1), [Assistant("l2", "rl2", 44, "2026-11-08T10:01:00.000Z")]);

        // Another process holds the file with no sharing at all, so it cannot be opened for reading now.
        using var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var index = TranscriptIndex.Build(_root);

        Assert.Equal(Path.GetFileName(readable), Path.GetFileName(Assert.Single(index.Sessions).FilePath));
        Assert.Equal(33, index.Totals.Total.Output);
        Assert.Equal(Path.GetFileName(locked), Path.GetFileName(Assert.Single(index.Skipped).FilePath));
    }

    [Fact]
    public void AFileWithNoAccessIsSkippedToo()
    {
        var projects = Project("C--Git-denied");
        var denied = Path.Combine(projects, "f5f5f5f5-0000-0000-0000-000000000010.jsonl");
        var allowed = Path.Combine(projects, "f6f6f6f6-0000-0000-0000-000000000011.jsonl");
        Create(denied, DateTime.UtcNow.AddHours(-2), [Assistant("d1", "rd1", 1, "2026-11-09T10:00:00.000Z")]);
        Create(allowed, DateTime.UtcNow.AddHours(-1), [Assistant("d2", "rd2", 2, "2026-11-09T10:01:00.000Z")]);

        var index = TranscriptIndex.Build(_root, (path, ledger) =>
            Path.GetFileName(path) == Path.GetFileName(denied)
                ? throw new UnauthorizedAccessException("access denied")
                : TranscriptReader.ReadFile(path, ledger));

        Assert.Equal(2, index.Totals.Total.Output);
        Assert.Equal("access denied", Assert.Single(index.Skipped).Reason);
    }
}

using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Search;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Round 12 item A1: a line one of whose values cannot be read (here a lone surrogate escape, which <see cref="JsonElement.GetString"/>
/// refuses) is skipped by every transcript reader, and the lines around it read as they do without it. Each test compares a file that holds
/// the unreadable line with the same file where that line is "not JSON", which every reader already skips: the line numbers match, so the
/// comparison is exact.
/// </summary>
public sealed class UnreadableLineTests : IDisposable
{
    /// <summary>The six characters <c>\ud800</c> as they stand in a transcript: a lone high surrogate escape.</summary>
    private const string LoneSurrogate = "\\ud800";

    /// <summary>What an unreadable line becomes in the reference file: a line that is not JSON.</summary>
    private const string NotJson = "{ not json";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-unreadable-" + Guid.NewGuid().ToString("N")[..8]);

    public UnreadableLineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string User(string text, string timestamp = "2026-11-17T10:00:00Z") =>
        "{\"type\":\"user\",\"timestamp\":\"" + timestamp + "\",\"cwd\":\"C:\\\\W\\\\alpha\",\"message\":{\"role\":\"user\",\"content\":\"" + text + "\"}}";

    private static string Assistant(string id, string text, string timestamp = "2026-11-17T10:00:05Z", long output = 5) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\",\"requestId\":\"req-" + id + "\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]," +
        "\"usage\":{\"input_tokens\":3,\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":2,\"cache_read_input_tokens\":100," +
        "\"cache_creation\":{\"ephemeral_1h_input_tokens\":2,\"ephemeral_5m_input_tokens\":0}}}}";

    private static string ToolUse(string messageId, string toolId, string name, string path, string timestamp = "2026-11-17T10:00:10Z") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\",\"requestId\":\"req-" + messageId + "\",\"message\":{\"id\":\"" + messageId +
        "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + toolId + "\",\"name\":\"" + name +
        "\",\"input\":{\"file_path\":\"" + path + "\"}}],\"usage\":{\"input_tokens\":1,\"output_tokens\":2," +
        "\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    private static string ToolResult(string toolUseId, bool failed, string timestamp = "2026-11-17T10:00:12Z") =>
        "{\"type\":\"user\",\"timestamp\":\"" + timestamp + "\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" +
        toolUseId + "\",\"is_error\":" + (failed ? "true" : "false") + ",\"content\":\"output\"}]}}";

    private static string TurnEnd(long ms, string timestamp = "2026-11-17T10:00:20Z") =>
        "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":" + ms + ",\"timestamp\":\"" + timestamp + "\"}";

    private static string Compaction(string timestamp = "2026-11-17T10:00:25Z") =>
        "{\"type\":\"system\",\"subtype\":\"compact_boundary\",\"timestamp\":\"" + timestamp +
        "\",\"compactMetadata\":{\"trigger\":\"auto\",\"preTokens\":100,\"postTokens\":50}}";

    private string Write(string name, IEnumerable<string> lines)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    private string Project(string name, IEnumerable<string> lines)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        Write(Path.Combine(name, "session.jsonl"), lines);
        return folder;
    }

    /// <summary>The fixture premise: the escape really is refused by the reader, so the tests exercise what they name.</summary>
    [Fact]
    public void TheLoneSurrogateIsRefusedWhenItsStringIsRead()
    {
        using var document = JsonDocument.Parse("{\"x\":\"a" + LoneSurrogate + "b\"}");

        Assert.Throws<InvalidOperationException>(() => document.RootElement.GetProperty("x").GetString());
    }

    private static string Describe(TranscriptSummary s) => string.Join("|",
        s.Records, s.UserMessages, s.AssistantMessages, s.ToolCalls, s.CompletedTurns, s.LastAssistantText, s.LastTimestamp,
        s.FirstTimestamp, s.LastModel, s.FirstUserText, s.AiTitle, s.CustomTitle, s.LastRecap, s.Compactions, s.ContinuesSessionId,
        s.FailedToolCalls, s.ContextTokensAtLastTurn, string.Join(",", s.FilesWritten ?? []), s.TotalUsage,
        string.Join(",", (s.ToolNames ?? new Dictionary<string, int>()).Select(p => p.Key + "=" + p.Value)),
        string.Join(",", (s.FileLastWrites ?? new Dictionary<string, DateTimeOffset>()).Select(p => p.Key + "@" + p.Value)),
        s.LatestCwd, string.Join(",", (s.FileFolders ?? new Dictionary<string, string>()).Select(p => p.Key + ">" + p.Value)));

    [Fact]
    public void TheSummaryCountsTheLinesAroundAnUnreadableLineAsItWouldWithoutIt()
    {
        // Positions match line for line: each unreadable line is "not JSON" in the reference.
        var reference = new[]
        {
            NotJson,
            Assistant("m1", "answer one"),
            NotJson,
            ToolUse("m2", "t2", "Write", "src/a.cs"),
            ToolResult("t2", failed: true),
            Assistant("m3", "answer two", output: 9),
            Compaction(),
            TurnEnd(4000),
        };
        var dirty = new[]
        {
            User("first " + LoneSurrogate + " question"),
            Assistant("m1", "answer one"),
            Assistant("bad", "broken " + LoneSurrogate + " text"),
            ToolUse("m2", "t2", "Write", "src/a.cs"),
            ToolResult("t2", failed: true),
            Assistant("m3", "answer two", output: 9),
            Compaction(),
            TurnEnd(4000),
        };

        var expected = TranscriptReader.Summarize(reference.Select(l => l), fileName: "s.jsonl");
        var actual = TranscriptReader.ReadFile(Write("summary.jsonl", dirty));

        Assert.Equal(Describe(expected), Describe(actual));
        Assert.Null(actual.FirstUserText);
        // The reply, the tool call (an assistant record too) and the last reply; the two unreadable lines count for nothing.
        Assert.Equal(3, actual.AssistantMessages);
        Assert.Equal(6, actual.Records);
    }

    [Fact]
    public void AnUnreadableUserLineCountsNothing()
    {
        // The first user line cannot be read, so the first message the summary names is the next readable one.
        var dirty = Write("user.jsonl", [User("first " + LoneSurrogate), User("second question"), Assistant("m1", "reply")]);
        var reference = Write("user-ref.jsonl", [NotJson, User("second question"), Assistant("m1", "reply")]);

        var actual = TranscriptReader.ReadFile(dirty);
        var expected = TranscriptReader.ReadFile(reference);

        Assert.Equal(Describe(expected), Describe(actual));
        Assert.Equal("second question", actual.FirstUserText);
        Assert.Equal(2, actual.Records);
    }

    [Fact]
    public void ATranscriptTailSkipsAnUnreadableLastLine()
    {
        var clean = Write("tail-clean.jsonl", [Assistant("m1", "one", output: 5)]);
        var dirty = Write("tail-dirty.jsonl", [Assistant("m1", "one", output: 5), Assistant("bad", "two", timestamp: "2026-11-17T10:00:0" + LoneSurrogate)]);

        var actual = TranscriptTail.Read(dirty);

        Assert.NotNull(actual);
        Assert.Equal(TranscriptTail.Read(clean), actual);
        Assert.Equal(DateTimeOffset.Parse("2026-11-17T10:00:05Z"), actual.LastRequest);
    }

    [Fact]
    public void TheTurnFactsCountTheLinesAroundAnUnreadableLineAsItWouldWithoutIt()
    {
        var reference = new[]
        {
            User("ask"),
            ToolUse("m1", "t1", "Edit", "src/a.cs"),
            NotJson,
            ToolResult("t1", failed: true),
            TurnEnd(3000),
            NotJson,
            User("ask again"),
            Assistant("m2", "done"),
            NotJson,
            TurnEnd(1000),
        };
        var dirty = new[]
        {
            User("ask"),
            ToolUse("m1", "t1", "Edit", "src/a.cs"),
            ToolUse("bad", "t" + LoneSurrogate, "Write", "src/b.cs"),
            ToolResult("t1", failed: true),
            TurnEnd(3000),
            User("bad " + LoneSurrogate),
            User("ask again"),
            Assistant("m2", "done"),
            Assistant("bad2", "text " + LoneSurrogate),
            TurnEnd(1000),
        };

        Assert.Equal(DescribeTurns(TurnFacts.Read(reference.Select(l => l))), DescribeTurns(TurnFacts.Read(dirty.Select(l => l))));
    }

    private static string DescribeTurns(IEnumerable<TurnFact> turns) => string.Join("\n", turns.Select(t => string.Join("|",
        t.StartLine, t.EndLine, t.Start, t.End, t.Prompt, t.ToolCalls, t.FailedCalls,
        string.Join(",", t.ToolNames.Select(p => p.Key + "=" + p.Value)), string.Join(",", t.FilesWritten), t.DurationSeconds, t.Compactions)));

    private static string DescribeEffort(EffortReport r) => string.Join("\n",
        "turns " + r.Turns,
        "failed " + string.Join(",", r.FailedByTool.Select(i => i.Label + "=" + i.Count + "@" + i.Line)),
        "reread " + string.Join(",", r.RereadFiles.Select(i => i.Label + "=" + i.Count + "@" + i.Line)),
        "commands " + string.Join(",", r.RepeatedCommands.Select(i => i.Label + "=" + i.Count + "@" + i.Line)),
        "without " + string.Join(",", r.TurnsWithoutOutput.Select(t => t.StartLine + ":" + t.Prompt + ":" + t.Tokens)));

    [Fact]
    public void TheEffortReportCountsTheLinesAroundAnUnreadableLineAsItWouldWithoutIt()
    {
        var reference = new[]
        {
            User("look at it"),
            ToolUse("m1", "r1", "Read", "src/a.cs"),
            ToolUse("m2", "r2", "Read", "src/a.cs"),
            NotJson,
            ToolResult("r2", failed: true),
            ToolUse("m3", "b1", "Bash", "src/a.cs"),
            NotJson,
            TurnEnd(2000),
        };
        var dirty = new[]
        {
            User("look at it"),
            ToolUse("m1", "r1", "Read", "src/a.cs"),
            ToolUse("m2", "r2", "Read", "src/a.cs"),
            User("bad " + LoneSurrogate),
            ToolResult("r2", failed: true),
            ToolUse("m3", "b1", "Bash", "src/a.cs"),
            Assistant("bad2", "broken " + LoneSurrogate),
            TurnEnd(2000),
        };

        Assert.Equal(DescribeEffort(EffortReport.From(reference.Select(l => l))), DescribeEffort(EffortReport.From(dirty.Select(l => l))));
    }

    [Fact]
    public void TheCacheReportCountsTheLinesAroundAnUnreadableLineAsItWouldWithoutIt()
    {
        var reference = Project("cache-ref", [
            Assistant("c1", "one", timestamp: "2026-11-17T09:00:00Z"),
            NotJson,
            Assistant("c2", "two", timestamp: "2026-11-17T09:02:00Z"),
            Assistant("c3", "three", timestamp: "2026-11-17T09:03:00Z"),
        ]);
        var dirty = Project("cache-dirty", [
            Assistant("c1", "one", timestamp: "2026-11-17T09:00:00Z"),
            Assistant("bad", "broken", timestamp: "2026-11-17T09:01:0" + LoneSurrogate),
            Assistant("c2", "two", timestamp: "2026-11-17T09:02:00Z"),
            Assistant("c3", "three", timestamp: "2026-11-17T09:03:00Z"),
        ]);

        var expected = CacheReport.Compute(reference);
        var actual = CacheReport.Compute(dirty);

        Assert.Equal(expected.Files, actual.Files);
        Assert.Equal(expected.CountedRequests, actual.CountedRequests);
        Assert.Equal(expected.Pairs, actual.Pairs);
        Assert.Equal(expected.Rows, actual.Rows);
    }

    [Fact]
    public void ThePagerShowsTheLinesAroundAnUnreadableLine()
    {
        var reference = Write("pager-ref.jsonl", [User("first"), NotJson, Assistant("m1", "two")]);
        var dirty = Write("pager.jsonl", [User("first"), Assistant("bad", "broken " + LoneSurrogate), Assistant("m1", "two")]);

        var expected = TranscriptPager.Open(reference);
        var actual = TranscriptPager.Open(dirty);

        Assert.Equal(expected.LineCount, actual.LineCount);
        Assert.Equal(Shown(expected.ReadPage(0, 10)), Shown(actual.ReadPage(0, 10)));
        // The numbers are the file's own line numbers, so the unreadable line still takes its place.
        Assert.Equal(["1:you:first", "3:claude:two"], Shown(actual.ReadPage(0, 10)));
    }

    private static IEnumerable<string> Shown(IEnumerable<TranscriptLine> lines) => lines.Select(l => $"{l.Number}:{l.Kind}:{l.Text}");

    [Fact]
    public void ThePagerReadsAFirstLineThatStartsWithAByteOrderMark()
    {
        // The pager reads without detecting the mark, so the first line's text starts with it; the record is still shown.
        var path = Path.Combine(_root, "bom.jsonl");
        File.WriteAllText(path, User("first with a mark") + "\n" + Assistant("m1", "reply") + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var page = TranscriptPager.Open(path).ReadPage(0, 10);

        Assert.Equal(["you:first with a mark", "claude:reply"], page.Select(l => $"{l.Kind}:{l.Text}"));
    }

    [Fact]
    public void TheWritesCacheSkipsAnUnreadableLineAsItWouldWithoutIt()
    {
        var reference = Write("writes-ref.jsonl", [ToolUse("m1", "w1", "Write", "src/a.cs"), NotJson, ToolUse("m2", "w2", "Edit", "docs/b.md")]);
        var dirty = Write("writes.jsonl", [ToolUse("m1", "w1", "Write", "src/a.cs"), ToolUse("bad", "wb", "Write", "src/" + LoneSurrogate + ".cs"), ToolUse("m2", "w2", "Edit", "docs/b.md")]);

        var expected = new SessionWritesCache().Read("writes-ref", reference);
        var actual = new SessionWritesCache().Read("writes", dirty);

        Assert.Equal(expected.Files, actual.Files);
        Assert.Equal(["src/a.cs", "docs/b.md"], actual.Files);
        Assert.Equal(RunSpend.OutputOfTranscript("writes-ref", reference), RunSpend.OutputOfTranscript("writes", dirty));
    }

    [Fact]
    public void ASearchDoesNotCountAnUnreadableLineAsAnUnreadableSession()
    {
        Project("search", [User("needle in the first message"), Assistant("bad", "needle " + LoneSurrogate), Assistant("m2", "another")]);

        var outcome = HistorySearch.RunCounted(TranscriptIndex.Build(_root).Sessions, "needle");

        Assert.Equal(0, outcome.Unreadable);
        var match = Assert.Single(outcome.Matches);
        Assert.Equal(1, match.MatchingMessages);
        Assert.Equal(["you"], match.Hits.Select(h => h.Kind));
    }

    [Fact]
    public void TheLineForARecordThatCannotBeReadIsNull()
    {
        Assert.Null(TranscriptLine.From(1, Assistant("bad", "x " + LoneSurrogate)));
        Assert.NotNull(TranscriptLine.From(1, Assistant("ok", "x")));
    }

    [Fact]
    public void TheDefaultIndexSkipsAnUnreadableLineAsItWouldWithoutIt()
    {
        var reference = Project("index-ref", [User("ask"), NotJson, Assistant("m1", "reply", output: 11), TurnEnd(900)]);
        var dirty = Project("index-dirty", [User("ask"), Assistant("bad", "broken " + LoneSurrogate), Assistant("m1", "reply", output: 11), TurnEnd(900)]);

        var expected = IndexFixture.Describe(TranscriptIndex.Build(reference));
        var actual = IndexFixture.Describe(TranscriptIndex.Build(dirty));

        Assert.Equal(expected.Replace("index-ref", "index-dirty"), actual.Replace("index-ref", "index-dirty"));
        Assert.Empty(TranscriptIndex.Build(dirty).Skipped);
    }
}

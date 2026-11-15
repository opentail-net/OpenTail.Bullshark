using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

public sealed class TranscriptReaderTests : IDisposable
{
    // Synthetic records with the same shape as a real transcript (finding F10). No real content is used.
    private static readonly string[] Sample =
    [
        """{"type":"permission-mode","permissionMode":"auto","sessionId":"s1"}""",
        """{"type":"user","timestamp":"2026-11-14T10:00:00.000Z","message":{"role":"user","content":"Write the plan"}}""",
        """{"type":"assistant","timestamp":"2026-11-14T10:00:05.000Z","message":{"model":"claude-opus-5-5","role":"assistant","content":[{"type":"thinking","thinking":""},{"type":"tool_use","name":"Read","input":{"file_path":"README.md"}}],"usage":{"input_tokens":10,"output_tokens":100,"cache_creation_input_tokens":500,"cache_read_input_tokens":2000}}}""",
        """{"type":"assistant","timestamp":"2026-11-14T10:01:00.000Z","message":{"model":"claude-haiku-5-5","role":"assistant","content":[{"type":"text","text":"Batch 1 is written."}],"usage":{"input_tokens":3,"output_tokens":40,"cache_creation_input_tokens":0,"cache_read_input_tokens":300}}}""",
        """{"type":"system","subtype":"turn_duration","durationMs":45225,"timestamp":"2026-11-14T10:01:01.000Z"}""",
        "this line is not json and must be skipped",
        "",
    ];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-transcript-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void CountsRecordsMessagesToolCallsAndTurns()
    {
        var summary = TranscriptReader.Summarize(Sample);

        // Five valid records. The invalid line and the blank line are skipped, not counted.
        Assert.Equal(5, summary.Records);
        Assert.Equal(1, summary.UserMessages);
        Assert.Equal(2, summary.AssistantMessages);
        Assert.Equal(1, summary.ToolCalls);
        Assert.Equal(1, summary.CompletedTurns);
    }

    [Fact]
    public void KeepsTheLastAssistantTextAndModel()
    {
        var summary = TranscriptReader.Summarize(Sample);

        Assert.Equal("Batch 1 is written.", summary.LastAssistantText);
        Assert.Equal("claude-haiku-5-5", summary.LastModel);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 10, 1, 1, TimeSpan.Zero), summary.LastTimestamp);
    }

    [Fact]
    public void SumsTokenUsageByModel()
    {
        var summary = TranscriptReader.Summarize(Sample);

        Assert.Equal(new TokenUsage(10, 100, 500, 2000), summary.UsageByModel["claude-opus-5-5"]);
        Assert.Equal(new TokenUsage(3, 40, 0, 300), summary.UsageByModel["claude-haiku-5-5"]);
        Assert.Equal(new TokenUsage(13, 140, 500, 2300), summary.TotalUsage);
    }

    [Fact]
    public void AnEmptyTranscriptGivesZeros()
    {
        var summary = TranscriptReader.Summarize([]);

        Assert.Equal(0, summary.Records);
        Assert.Null(summary.LastAssistantText);
        Assert.Equal(TokenUsage.Zero, summary.TotalUsage);
    }

    [Fact]
    public void FindsTheTranscriptBySessionIdWhateverTheProjectFolderIsCalled()
    {
        var project = Path.Combine(_dir, "C--Some-Other-Encoding");
        Directory.CreateDirectory(project);
        var file = Path.Combine(project, "abc123.jsonl");
        File.WriteAllLines(file, Sample);

        Assert.Equal(file, TranscriptReader.FindTranscript(_dir, "abc123"));
        Assert.Null(TranscriptReader.FindTranscript(_dir, "missing"));
        Assert.Null(TranscriptReader.FindTranscript(Path.Combine(_dir, "no-such-root"), "abc123"));
    }

    [Fact]
    public void ReadsAFileFromDisk()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "s.jsonl");
        File.WriteAllLines(file, Sample);

        Assert.Equal(2, TranscriptReader.ReadFile(file).AssistantMessages);
    }
}

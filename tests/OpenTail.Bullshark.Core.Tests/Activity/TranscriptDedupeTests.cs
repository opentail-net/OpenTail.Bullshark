using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Counting rules (F34, F35, F36). One API response is written as several records with the same usage, the first
/// record can carry a placeholder output count, and the same message can appear in more than one transcript file.
/// Each message is counted once, keyed by message.id:requestId, with its largest usage.
/// </summary>
public sealed class TranscriptDedupeTests
{
    private static string Record(string messageId, string requestId, string block, long output = 100, string model = "claude-haiku-5-5") =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-14T10:00:00.000Z\",\"requestId\":\"" + requestId +
        "\",\"message\":{\"id\":\"" + messageId + "\",\"model\":\"" + model + "\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"" + block + "\",\"id\":\"toolu_" + messageId + "\",\"name\":\"Read\",\"text\":\"x\"}]," +
        "\"usage\":{\"input_tokens\":5,\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":10,\"cache_read_input_tokens\":1000}}}";

    private static IEnumerable<string> OneResponseThreeBlocks() =>
    [
        Record("msg_1", "req_1", "thinking"),
        Record("msg_1", "req_1", "tool_use"),
        Record("msg_1", "req_1", "text"),
    ];

    [Fact]
    public void ARepeatedResponseIsCountedOnce()
    {
        var summary = TranscriptReader.Summarize(OneResponseThreeBlocks());

        Assert.Equal(1, summary.AssistantMessages);
        Assert.Equal(1, summary.ToolCalls);
        Assert.Equal(new TokenUsage(5, 100, 10, 1000), summary.TotalUsage);
    }

    [Fact]
    public void ThePlaceholderFirstRecordIsReplacedByTheFinalCount()
    {
        // Streaming writes an early record with a placeholder output count (F36); the later record has the final count.
        var lines = new[]
        {
            Record("msg_5", "req_5", "thinking", output: 8),
            Record("msg_5", "req_5", "text", output: 435),
        };

        var summary = TranscriptReader.Summarize(lines);

        Assert.Equal(1, summary.AssistantMessages);
        Assert.Equal(435, summary.TotalUsage.Output);
    }

    [Fact]
    public void DistinctMessagesAreAllCounted()
    {
        var lines = new[]
        {
            Record("msg_1", "req_1", "text", output: 100),
            Record("msg_2", "req_2", "text", output: 200),
        };

        var summary = TranscriptReader.Summarize(lines);

        Assert.Equal(2, summary.AssistantMessages);
        Assert.Equal(300, summary.TotalUsage.Output);
    }

    [Fact]
    public void ASharedLedgerCountsAMessageOnceAcrossFiles()
    {
        var ledger = new UsageLedger();
        var a = TranscriptReader.Summarize(new[] { Record("msg_9", "req_9", "text", output: 500) }, ledger, "a.jsonl");
        var b = TranscriptReader.Summarize(new[] { Record("msg_9", "req_9", "text", output: 500) }, ledger, "b.jsonl");

        Assert.Equal(500, a.TotalUsage.Output);
        // The message is present in file b (so it is listed there), but its usage was counted in file a.
        Assert.Equal(1, b.AssistantMessages);
        Assert.Equal(0, b.TotalUsage.Output);
        Assert.Equal(500, ledger.Total.Output);
    }

    [Fact]
    public void WithoutASharedLedgerEachFileCountsItsOwnWay()
    {
        // The per-file view counts a message in each file it appears in; only the shared ledger counts it once (F35).
        var line = Record("msg_9", "req_9", "text", output: 500);

        Assert.Equal(500, TranscriptReader.Summarize(new[] { line }).TotalUsage.Output);
        Assert.Equal(500, TranscriptReader.Summarize(new[] { line }).TotalUsage.Output);
    }

    [Fact]
    public void RecordsWithoutAnIdAreCountedEachTime()
    {
        // No identity means no safe de-duplication, so every record counts (the same as ccusage).
        var noId = "{\"type\":\"assistant\",\"message\":{\"model\":\"claude-haiku-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"a\"}],\"usage\":{\"input_tokens\":1,\"output_tokens\":7,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

        var summary = TranscriptReader.Summarize(new[] { noId, noId });

        Assert.Equal(2, summary.AssistantMessages);
        Assert.Equal(14, summary.TotalUsage.Output);
    }

    [Fact]
    public void EachMessageIsAttributedToItsOwnModel()
    {
        // Regression: usage used to follow the last model seen, not the model of its own record.
        var lines = new[]
        {
            Record("msg_h", "req_h", "text", output: 100, model: "claude-haiku-5-5"),
            Record("msg_o", "req_o", "text", output: 200, model: "claude-opus-5-5"),
        };

        var summary = TranscriptReader.Summarize(lines);

        Assert.Equal(100, summary.UsageByModel["claude-haiku-5-5"].Output);
        Assert.Equal(200, summary.UsageByModel["claude-opus-5-5"].Output);
    }

    [Fact]
    public void TheLastTextStillComesFromTheRepeatedResponse()
    {
        var lines = new[]
        {
            Record("msg_1", "req_1", "text", output: 1),
            Record("msg_1", "req_1", "text", output: 1),
        };

        Assert.Equal("x", TranscriptReader.Summarize(lines).LastAssistantText);
    }
}

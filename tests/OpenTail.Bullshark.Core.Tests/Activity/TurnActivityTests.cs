using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Failed tool calls, the failed-result flag and the compaction line (transcript-activity.md, TA1 and TA2). The records are
/// written in the shape observed on 2026-11-16 (see the plan's vendor facts).
/// </summary>
public sealed class TurnActivityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-activity-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Prompt(string text) =>
        """{"type":"user","timestamp":"@AT@","message":{"role":"user","content":"@TEXT@"}}""".Fill(("@TEXT@", text));

    private static string ToolUse(string id, string name, string command = "ls", string messageId = "m1") =>
        """{"type":"assistant","timestamp":"@AT@","requestId":"r1","message":{"id":"@MSG@","model":"claude-opus-5-5","content":[{"type":"tool_use","id":"@ID@","name":"@NAME@","input":{"command":"@CMD@"}}],"usage":{"input_tokens":10,"output_tokens":5,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}}"""
            .Fill(("@ID@", id), ("@NAME@", name), ("@CMD@", command), ("@MSG@", messageId));

    private static string Result(string id, bool isError, string text = "boom") =>
        """{"type":"user","timestamp":"@AT@","message":{"role":"user","content":[{"type":"tool_result","content":"@TEXT@","is_error":@FLAG@,"tool_use_id":"@ID@"}]}}"""
            .Fill(("@TEXT@", text), ("@FLAG@", isError ? "true" : "false"), ("@ID@", id));

    private static string Text(string text) =>
        """{"type":"assistant","timestamp":"@AT@","requestId":"r1","message":{"id":"m2","model":"claude-opus-5-5","content":[{"type":"text","text":"@TEXT@"}]}}"""
            .Fill(("@TEXT@", text));

    private static string TurnDuration(int ms) =>
        """{"type":"system","subtype":"turn_duration","durationMs":@MS@,"timestamp":"@AT@"}""".Fill(("@MS@", ms.ToString()));

    private const string Compaction =
        """{"type":"system","subtype":"compact_boundary","content":"Conversation compacted","compactMetadata":{"trigger":"auto","preTokens":967530,"postTokens":14951}}""";

    // ---- TA1: the count of failed tool calls

    [Fact]
    public void AFailedCallIsCounted()
    {
        var summary = TranscriptReader.Summarize([Prompt("run it"), ToolUse("a", "Bash"), Result("a", isError: true)]);

        Assert.Equal(1, summary.FailedToolCalls);
    }

    [Fact]
    public void AFailureRepeatedInTwoRecordsIsCountedOnce()
    {
        var summary = TranscriptReader.Summarize([Prompt("run it"), ToolUse("a", "Bash"), Result("a", isError: true), Result("a", isError: true)]);

        Assert.Equal(1, summary.FailedToolCalls);
    }

    [Fact]
    public void AResultWithoutErrorIsNotCounted()
    {
        var summary = TranscriptReader.Summarize([Prompt("run it"), ToolUse("a", "Bash"), Result("a", isError: false)]);

        Assert.Equal(0, summary.FailedToolCalls);
    }

    [Fact]
    public void AFailureWhoseCallIsNotInTheFileStillCounts()
    {
        var summary = TranscriptReader.Summarize([Prompt("run it"), Result("zz", isError: true)]);

        Assert.Equal(1, summary.FailedToolCalls);
    }

    [Fact]
    public void TheIndexCarriesTheCountOnTheSessionDigest()
    {
        var folder = Path.Combine(_dir, "projects", "C--demo");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "17a94483-99fc-4005-9fa9-dbd63958653d.jsonl");
        File.WriteAllLines(file, [Prompt("run it"), ToolUse("a", "Bash"), Result("a", isError: true), Result("b", isError: false)]);

        var index = TranscriptIndex.Build(Path.Combine(_dir, "projects"));

        Assert.Equal(1, Assert.Single(index.Sessions).FailedToolCalls);
    }

    // ---- TA2: the failed-result flag and the compaction line

    [Fact]
    public void AFailedResultIsFlaggedAndKeepsItsText()
    {
        var line = TranscriptLine.From(4, Result("a", isError: true, text: "exit 1"));

        Assert.NotNull(line);
        Assert.Equal("result", line.Kind);
        Assert.True(line.Failed);
        Assert.Equal("result: exit 1", line.Text);
    }

    [Fact]
    public void ASuccessfulResultIsNotFlagged()
    {
        var line = TranscriptLine.From(4, Result("a", isError: false));

        Assert.NotNull(line);
        Assert.False(line.Failed);
    }

    [Fact]
    public void ACompactionIsALineWithItsTokenCounts()
    {
        var line = TranscriptLine.From(9, Compaction);

        Assert.NotNull(line);
        Assert.Equal("compact", line.Kind);
        Assert.Equal("conversation compacted (auto): 967,530 → 14,951 tokens", line.Text);
    }

    [Fact]
    public void ACompactionWithoutItsCountsSaysOnlyThatItHappened()
    {
        var line = TranscriptLine.From(9, """{"type":"system","subtype":"compact_boundary"}""");

        Assert.NotNull(line);
        Assert.Equal("conversation compacted", line.Text);
    }

    [Fact]
    public void TheResumeTextIsUnchangedByTheNewLines()
    {
        // Lines 1 to 6: a prompt, a call, a failed result, a compaction, an answer and the end of the turn.
        var json = new[] { Prompt("hello"), ToolUse("a", "Bash"), Result("a", isError: true, text: "boom"), Compaction, Text("done"), TurnDuration(1500) };
        var lines = json.Select((text, i) => TranscriptLine.From(i + 1, text)).OfType<TranscriptLine>().ToList();

        var tail = ResumeText.Tail(lines, turns: 1);

        // The compaction and the turn end are not part of the text; the failed result keeps its text, and no flag shows.
        Assert.Equal("you: hello\n▸ Bash ls\nresult: boom\nclaude: done", tail);
    }
}

/// <summary>Records for the tests: a template with <c>@NAME@</c> placeholders, filled by <see cref="Fill"/>.</summary>
internal static class RecordTemplate
{
    private const string At = "2026-11-16T10:00:00.000Z";

    public static string Fill(this string template, params (string Key, string Value)[] values)
    {
        var text = template.Replace("@AT@", At);
        foreach (var (key, value) in values) text = text.Replace(key, value);
        return text;
    }
}

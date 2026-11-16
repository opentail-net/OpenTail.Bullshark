using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>Turn facts (transcript-activity.md, TA3). Records are written in the shape observed on 2026-11-16.</summary>
public sealed class TurnFactsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-turns-" + Guid.NewGuid().ToString("N")[..8]);

    public TurnFactsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Prompt(string text) =>
        """{"type":"user","timestamp":"@AT@","message":{"role":"user","content":"@TEXT@"}}""".Fill(("@TEXT@", text));

    private static string Call(string id, string name, string input = """{"command":"ls"}""", string messageId = "m1") =>
        """{"type":"assistant","timestamp":"@AT@","requestId":"r1","message":{"id":"@MSG@","model":"claude-opus-5-5","content":[{"type":"tool_use","id":"@ID@","name":"@NAME@","input":@INPUT@}]}}"""
            .Fill(("@MSG@", messageId), ("@ID@", id), ("@NAME@", name), ("@INPUT@", input));

    private static string Result(string id, bool isError) =>
        """{"type":"user","timestamp":"@AT@","message":{"role":"user","content":[{"type":"tool_result","content":"out","is_error":@FLAG@,"tool_use_id":"@ID@"}]}}"""
            .Fill(("@FLAG@", isError ? "true" : "false"), ("@ID@", id));

    private static string Text(string text) =>
        """{"type":"assistant","timestamp":"@AT@","requestId":"r1","message":{"id":"m2","model":"claude-opus-5-5","content":[{"type":"text","text":"@TEXT@"}]}}"""
            .Fill(("@TEXT@", text));

    private static string TurnDuration(int ms) =>
        """{"type":"system","subtype":"turn_duration","durationMs":@MS@,"timestamp":"@AT@"}""".Fill(("@MS@", ms.ToString()));

    private const string Compaction =
        """{"type":"system","subtype":"compact_boundary","content":"Conversation compacted","compactMetadata":{"trigger":"auto","preTokens":967530,"postTokens":14951}}""";

    [Fact]
    public void OneTurnHasItsToolsFailuresFilesAndDuration()
    {
        var turns = TurnFacts.Read([
            Prompt("fix it"),
            Call("a", "Bash"),
            Result("a", isError: true),
            Call("b", "Edit", """{"file_path":"C:/x/a.cs"}"""),
            Result("b", isError: false),
            Text("done"),
            TurnDuration(2500),
        ]);

        var turn = Assert.Single(turns);
        Assert.Equal(1, turn.StartLine);
        Assert.Equal(7, turn.EndLine);
        Assert.Equal("fix it", turn.Prompt);
        Assert.Equal(2, turn.ToolCalls);
        Assert.Equal(1, turn.FailedCalls);
        Assert.Equal(1, turn.ToolNames["Bash"]);
        Assert.Equal(1, turn.ToolNames["Edit"]);
        Assert.Equal(new[] { "C:/x/a.cs" }, turn.FilesWritten);
        Assert.Equal(2.5, turn.DurationSeconds);
        Assert.Equal(0, turn.Compactions);
    }

    [Fact]
    public void ATurnWithNoDurationEndsAtItsLastRecordBeforeTheNextPrompt()
    {
        var turns = TurnFacts.Read([Prompt("one"), Text("a"), Prompt("two"), Text("b")]);

        Assert.Equal(2, turns.Count);
        Assert.Equal((1, 2), (turns[0].StartLine, turns[0].EndLine));
        Assert.Null(turns[0].DurationSeconds);
        Assert.Equal((3, 4), (turns[1].StartLine, turns[1].EndLine));
        Assert.Null(turns[1].DurationSeconds);
    }

    [Fact]
    public void ACompactionInsideATurnIsCounted()
    {
        var turn = Assert.Single(TurnFacts.Read([Prompt("long one"), Compaction, TurnDuration(900)]));

        Assert.Equal(1, turn.Compactions);
        Assert.Equal(3, turn.EndLine);
    }

    [Fact]
    public void ACallWrittenInTwoRecordsIsCountedOnce()
    {
        var turn = Assert.Single(TurnFacts.Read([Prompt("x"), Call("a", "Bash"), Call("a", "Bash"), TurnDuration(1000)]));

        Assert.Equal(1, turn.ToolCalls);
        Assert.Equal(1, turn.ToolNames["Bash"]);
    }

    [Fact]
    public void ToolCallsBeforeTheFirstPromptFormATurnWithNoPrompt()
    {
        var turn = Assert.Single(TurnFacts.Read([Call("a", "Bash"), Result("a", isError: false), TurnDuration(1000)]));

        Assert.Equal(string.Empty, turn.Prompt);
        Assert.Equal((1, 3), (turn.StartLine, turn.EndLine));
        Assert.Equal(1, turn.ToolCalls);
    }

    [Fact]
    public void ALongPromptIsCutToOneLine()
    {
        var turn = Assert.Single(TurnFacts.Read([Prompt(new string('x', 200)), TurnDuration(1000)]));

        Assert.EndsWith("…", turn.Prompt);
        Assert.Equal(121, turn.Prompt.Length);
    }

    [Fact]
    public void BlankAndBrokenLinesKeepTheLineNumbersOfThePager()
    {
        // Line 1 is blank and line 3 is not JSON: both count, as they do in the pager.
        var file = Path.Combine(_dir, "numbers.jsonl");
        File.WriteAllLines(file, ["", Prompt("hello"), "{ broken", Text("done"), TurnDuration(1500)]);

        var turn = Assert.Single(TurnFacts.ReadFile(file));
        Assert.Equal((2, 5), (turn.StartLine, turn.EndLine));

        // The view shows the duration on the line the pager numbers as this turn's end.
        var pager = TranscriptPager.Open(file);
        var shown = Assert.Single(pager.ReadPage(0, pager.LineCount), line => line.Kind == "turn");
        Assert.Equal(turn.EndLine, shown.Number);
    }
}

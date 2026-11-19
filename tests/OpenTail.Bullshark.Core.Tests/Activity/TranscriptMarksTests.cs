using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The jump marks (T5 batch): the lines a jump key goes to, read in the same pass as the turn facts, and the next and previous line of a list.
/// </summary>
public sealed class TranscriptMarksTests
{
    private const string Turn = "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":9000,\"timestamp\":\"2026-11-19T09:00:10.000Z\"}";
    private const string Compact = "{\"type\":\"system\",\"subtype\":\"compact_boundary\",\"compactMetadata\":{\"trigger\":\"auto\",\"preTokens\":900,\"postTokens\":300}}";

    private static string Prompt(string text) => "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"" + text + "\"}}";

    private static string Call(string id, string name, string input) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"m" + id + "\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"" + name +
        "\",\"input\":" + input + "}]}}";

    private static string Result(string id, bool failed) =>
        "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id + "\",\"content\":\"out\"" +
        (failed ? ",\"is_error\":true" : string.Empty) + "}]}}";

    [Fact]
    public void NextIsTheFirstLineAfterAndPreviousIsTheLastLineBefore()
    {
        IReadOnlyList<int> lines = [3, 7, 12];

        Assert.Equal(3, TranscriptMarks.Next(lines, 0));
        Assert.Equal(7, TranscriptMarks.Next(lines, 3));
        Assert.Null(TranscriptMarks.Next(lines, 12));
        Assert.Null(TranscriptMarks.Previous(lines, 3));
        Assert.Equal(7, TranscriptMarks.Previous(lines, 12));
        Assert.Equal(12, TranscriptMarks.Previous(lines, 100));
        Assert.Equal(3, TranscriptMarks.Next(lines, -5));
    }

    [Fact]
    public void AnEmptyListHasNoNextAndNoPrevious()
    {
        Assert.Null(TranscriptMarks.Next([], 5));
        Assert.Null(TranscriptMarks.Previous([], 5));
        Assert.Empty(TranscriptMarks.None.Failures);
    }

    [Fact]
    public void ReadWithMarksFindsEachKindOnItsLine()
    {
        var lines = new[]
        {
            Prompt("make it"),                              // 1: a prompt
            Call("t1", "Write", "{\"file_path\":\"C:/w/a.txt\"}"), // 2: a file write
            Result("t1", failed: true),                     // 3: a failed result
            Call("t1", "Write", "{\"file_path\":\"C:/w/a.txt\"}"), // 4: the same call again: not a second write
            Compact,                                        // 5: a compaction
            Prompt("again"),                                // 6: a prompt
            Call("t3", "Bash", "{\"command\":\"dotnet build\"}"), // 7: not a write
            Result("t3", failed: true),                     // 8: a failed result
            Turn,                                           // 9
        };

        var (turns, marks) = TurnFacts.ReadWithMarks(lines);

        Assert.Equal(2, turns.Count);
        Assert.Equal([1, 6], marks.Prompts);
        Assert.Equal([3, 8], marks.Failures);
        Assert.Equal([2], marks.Writes);
        Assert.Equal([5], marks.Compactions);
    }

    [Fact]
    public void AResultWhoseFirstBlockSucceededIsNotAFailureMark()
    {
        // The view shows the first tool result of a record, so a failure in a later block of the same record is not on its line.
        var line = "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"a\",\"content\":\"ok\"},"
                   + "{\"type\":\"tool_result\",\"tool_use_id\":\"b\",\"content\":\"boom\",\"is_error\":true}]}}";

        var (_, marks) = TurnFacts.ReadWithMarks([line]);

        Assert.Empty(marks.Failures);
    }

    [Fact]
    public void AWriteBeforeAnyPromptIsStillMarked()
    {
        var (turns, marks) = TurnFacts.ReadWithMarks([Call("w1", "Edit", "{\"file_path\":\"b.md\"}")]);

        Assert.Single(turns);
        Assert.Equal([1], marks.Writes);
    }

    [Fact]
    public void LinesThatAreNotJsonAreNotMarksButStillCountInTheNumbers()
    {
        var (_, marks) = TurnFacts.ReadWithMarks(["not json", string.Empty, Prompt("third line")]);

        Assert.Equal([3], marks.Prompts);
    }

    [Fact]
    public void ReadFileWithMarksReadsTheFileInOnePass()
    {
        var path = Path.Combine(Path.GetTempPath(), "bullshark-marks-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        try
        {
            File.WriteAllLines(path, [Prompt("one"), Result("x", failed: true)]);

            var (_, marks) = TurnFacts.ReadFileWithMarks(path);

            Assert.Equal([1], marks.Prompts);
            Assert.Equal([2], marks.Failures);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

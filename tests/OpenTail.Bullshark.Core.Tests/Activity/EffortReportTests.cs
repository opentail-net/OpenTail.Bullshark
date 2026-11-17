using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The "Where the effort went" rules (gems item 4), each on a small transcript written in the shape the reader already reads.
/// </summary>
public sealed class EffortReportTests
{
    private const string At = "2026-11-17T09:00:00Z";

    private static string Json(object record) => JsonSerializer.Serialize(record);

    private static object Usage(long input, long output) =>
        new { input_tokens = input, output_tokens = output, cache_creation_input_tokens = 0, cache_read_input_tokens = 100 };

    private static string Prompt(string text) =>
        Json(new { type = "user", timestamp = At, message = new { role = "user", content = text } });

    /// <summary>An assistant record with one tool call. <paramref name="messageId"/> is unique per call unless a test repeats it on purpose.</summary>
    private static string Call(string toolId, string name, object input, string messageId) =>
        Json(new
        {
            type = "assistant",
            timestamp = At,
            requestId = "r-" + messageId,
            message = new
            {
                id = messageId,
                model = "claude-opus-5-5",
                content = new object[] { new { type = "tool_use", id = toolId, name, input } },
                usage = Usage(10, 5),
            },
        });

    private static string Text(string text, string messageId) =>
        Json(new
        {
            type = "assistant",
            timestamp = At,
            requestId = "r-" + messageId,
            message = new { id = messageId, model = "claude-opus-5-5", content = new object[] { new { type = "text", text } }, usage = Usage(3, 7) },
        });

    private static string Failed(string toolId) =>
        Json(new
        {
            type = "user",
            timestamp = At,
            message = new { role = "user", content = new object[] { new { type = "tool_result", content = "boom", is_error = true, tool_use_id = toolId } } },
        });

    private static string Done() => Json(new { type = "system", subtype = "turn_duration", durationMs = 1000, timestamp = At });

    private static object Bash(string command) => new { command };

    private static object Read(string path) => new { file_path = path };

    private static object Write(string path) => new { file_path = path, content = "x" };

    [Fact]
    public void FailedCallsAreCountedPerToolOnceEachWithTheFirstLine()
    {
        var report = EffortReport.From([
            Prompt("fix it"),                       // 1
            Call("a1", "Bash", Bash("make"), "m1"), // 2
            Failed("a1"),                           // 3
            Failed("a1"),                           // 4: the same result written again
            Call("a2", "Read", Read("x.cs"), "m2"), // 5
            Failed("a2"),                           // 6
            Call("a3", "Bash", Bash("make 2"), "m3"), // 7
            Failed("a3"),                           // 8
            Done(),                                 // 9
        ]);

        Assert.Equal(3, report.FailedCalls);
        Assert.Equal(["Bash", "Read"], report.FailedByTool.Select(i => i.Label));
        Assert.Equal(2, report.FailedByTool[0].Count);
        Assert.Equal(3, report.FailedByTool[0].Line);
        Assert.Equal(1, report.FailedByTool[1].Count);
        Assert.Equal(6, report.FailedByTool[1].Line);
    }

    [Fact]
    public void AFileReadMoreThanThreeTimesIsListedAndAFileReadThreeTimesIsNot()
    {
        var lines = new List<string> { Prompt("look") };
        for (var i = 0; i < 4; i++) lines.Add(Call("r" + i, "Read", Read("hot.cs"), "hot" + i));
        for (var i = 0; i < 3; i++) lines.Add(Call("q" + i, "Read", Read("cool.cs"), "cool" + i));
        lines.Add(Done());

        var report = EffortReport.From(lines);

        var item = Assert.Single(report.RereadFiles);
        Assert.Equal("hot.cs", item.Label);
        Assert.Equal(4, item.Count);
        Assert.Equal(2, item.Line);
    }

    [Fact]
    public void AFilesPathIsKeptWholeSoThePanelCanShowItsFileName()
    {
        // A path longer than the panel's label length: it is kept whole, so its file name is not cut off (round 6 item 10).
        var longPath = @"C:\" + new string('a', 140) + @"\folder\README.md";
        var lines = new List<string> { Prompt("look") };
        for (var i = 0; i < 4; i++) lines.Add(Call("r" + i, "Read", Read(longPath), "long" + i));
        lines.Add(Done());

        var item = Assert.Single(EffortReport.From(lines).RereadFiles);

        Assert.Equal(longPath, item.Label);
        Assert.EndsWith(@"\README.md", item.Label);
    }

    [Fact]
    public void AFolderTooLongForTheWidthIsTrimmedFromTheStartAndTheFileNameKept()
    {
        var path = @"C:\work\repo\src\README.md";

        // The first folder boundary that fits is the longest suffix that does: "…\repo\src\README.md" is 20 characters.
        Assert.Equal(@"…\repo\src\README.md", EffortReport.FitPath(path, text => text.Length <= 20));
        Assert.Equal(@"…\src\README.md", EffortReport.FitPath(path, text => text.Length <= 16));
    }

    [Fact]
    public void APathThatFitsIsShownWholeAndAFileNameThatDoesNotFitIsStillShown()
    {
        var path = @"C:\work\repo\src\README.md";

        Assert.Equal(path, EffortReport.FitPath(path, _ => true));
        // Nothing with an ellipsis fits: the file name with its ellipsis is what is left, and the view wraps it.
        Assert.Equal(@"…\README.md", EffortReport.FitPath(path, _ => false));
        Assert.Equal("README.md", EffortReport.FitPath("README.md", _ => false));
    }

    [Fact]
    public void ACommandRunMoreThanTwiceIsListedByItsExactText()
    {
        var lines = new List<string> { Prompt("test") };
        for (var i = 0; i < 3; i++) lines.Add(Call("t" + i, "Bash", Bash("npm test"), "t" + i));
        for (var i = 0; i < 2; i++) lines.Add(Call("l" + i, "Bash", Bash("ls"), "l" + i));
        lines.Add(Done());

        var report = EffortReport.From(lines);

        var item = Assert.Single(report.RepeatedCommands);
        Assert.Equal("npm test", item.Label);
        Assert.Equal(3, item.Count);
        Assert.Equal(2, item.Line);
    }

    [Fact]
    public void ATurnThatWroteNoFileAndGaveNoTextIsListedWithItsTokens()
    {
        var report = EffortReport.From([
            Prompt("check the build"),                       // 1
            Call("b1", "Bash", Bash("dotnet build"), "m1"),  // 2: usage in 10, out 5, cache read 100
            Done(),                                          // 3
            Prompt("write the file"),                        // 4
            Call("w1", "Write", Write("a.cs"), "m2"),        // 5: wrote a file, so it is output
            Done(),                                          // 6
            Prompt("explain"),                               // 7
            Text("Here is the answer.", "m3"),               // 8
            Done(),                                          // 9
        ]);

        Assert.Equal(3, report.Turns);
        var turn = Assert.Single(report.TurnsWithoutOutput);
        Assert.Equal(1, turn.StartLine);
        Assert.Equal("check the build", turn.Prompt);
        Assert.Equal(10, turn.Tokens.Input);
        Assert.Equal(5, turn.Tokens.Output);
        Assert.Equal(100, turn.Tokens.CacheRead);
        Assert.Equal(10, report.TokensWithoutOutput.Input);
    }

    [Fact]
    public void AMessageInTwoRecordsCountsItsTokensOnce()
    {
        // The same assistant message (same id and request id) is written twice, as Claude Code does for a message with several blocks.
        var report = EffortReport.From([
            Prompt("go"),
            Call("d1", "Bash", Bash("a"), "same"),
            Call("d1", "Bash", Bash("a"), "same"),
            Done(),
        ]);

        var turn = Assert.Single(report.TurnsWithoutOutput);
        Assert.Equal(10, turn.Tokens.Input);
        Assert.Equal(5, turn.Tokens.Output);
    }

    [Fact]
    public void TheTurnCountIsTheSameAsTheTranscriptViewsOnTheSameLines()
    {
        var lines = new[]
        {
            Prompt("one"),
            Call("c1", "Bash", Bash("x"), "p1"),
            Done(),
            Prompt("two"),
            Json(new { type = "system", subtype = "compact_boundary", content = "Conversation compacted" }),
            Text("answer", "p2"),
            Done(),
            "not json at all",
            "",
            Prompt("three"),   // no turn_duration: it ends at the end of the file
        };

        Assert.Equal(TurnFacts.Read(lines).Count, EffortReport.From(lines).Turns);
        Assert.Equal(3, EffortReport.From(lines).Turns);
    }

    [Fact]
    public void LineNumbersCountBlankLinesToo()
    {
        var report = EffortReport.From(["", Prompt("x"), "", Failed("nope")]);

        var failed = Assert.Single(report.FailedByTool);
        Assert.Equal("unknown", failed.Label);
        Assert.Equal(4, failed.Line);
    }

    [Fact]
    public void AnEmptyTranscriptGivesAnEmptyReport()
    {
        var report = EffortReport.From([]);

        Assert.Equal(0, report.Turns);
        Assert.Equal(0, report.FailedCalls);
        Assert.Empty(report.RereadFiles);
        Assert.Empty(report.RepeatedCommands);
        Assert.Empty(report.TurnsWithoutOutput);
        Assert.Equal(TokenUsage.Zero, report.TokensWithoutOutput);
    }
}

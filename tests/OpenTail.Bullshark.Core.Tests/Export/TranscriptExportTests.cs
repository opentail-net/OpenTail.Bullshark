using System.Text.Json;
using OpenTail.Bullshark.Core.Export;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Export;

/// <summary>The Markdown export of a past session (additions item 7), on a small sample transcript.</summary>
public sealed class TranscriptExportTests
{
    private const string At = "2026-11-17T09:00:00Z";
    private static readonly DateTimeOffset Date = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static string Json(object record) => JsonSerializer.Serialize(record);

    private static string Prompt(string text) => Json(new { type = "user", timestamp = At, message = new { role = "user", content = text } });

    private static string Answer(string text, string messageId) =>
        Json(new
        {
            type = "assistant",
            timestamp = At,
            requestId = "r-" + messageId,
            message = new { id = messageId, model = "claude-opus-5-5", content = new object[] { new { type = "text", text } } },
        });

    private static string Thinking(string text) =>
        Json(new
        {
            type = "assistant",
            timestamp = At,
            requestId = "r-think",
            message = new { id = "think", model = "claude-opus-5-5", content = new object[] { new { type = "thinking", thinking = text } } },
        });

    private static string Call(string id, string name, object input) =>
        Json(new
        {
            type = "assistant",
            timestamp = At,
            requestId = "r-" + id,
            message = new { id = "m-" + id, model = "claude-opus-5-5", content = new object[] { new { type = "tool_use", id, name, input } } },
        });

    private static string Result(string id, string output) =>
        Json(new
        {
            type = "user",
            timestamp = At,
            message = new { role = "user", content = new object[] { new { type = "tool_result", content = output, tool_use_id = id } } },
        });

    private static string Done() => Json(new { type = "system", subtype = "turn_duration", durationMs = 900, timestamp = At });

    private static string Sample(string? recap = null) => Markdown(
    [
        Prompt("fix the build"),
        Thinking("a private thought that must not be exported"),
        Call("c1", "Bash", new { command = "dotnet build" }),
        Result("c1", "error CS0001 long output that is not exported"),
        Answer("It is fixed now.", "a1"),
        Done(),
    ], recap);

    private static string Markdown(IEnumerable<string> lines, string? recap = null) =>
        TranscriptExport.Markdown(lines, "Fix the build", Date, @"C:\work\repo", "claude-opus-5-5", recap);

    [Fact]
    public void TheHeaderGivesTheTitleDateFolderAndModel()
    {
        var text = Sample();

        Assert.StartsWith("# Fix the build\n\n", text);
        Assert.Contains("- Date: 2026-11-17", text);
        Assert.Contains(@"- Project folder: C:\work\repo", text);
        Assert.Contains("- Model: claude-opus-5-5", text);
    }

    [Fact]
    public void TurnsAreYouAndClaudeSectionsWithAToolSummaryUnderTheAnswer()
    {
        var text = Sample();

        var you = text.IndexOf("## You\n\nfix the build", StringComparison.Ordinal);
        var claude = text.IndexOf("## Claude\n\n- ▸ Bash", StringComparison.Ordinal);
        var answer = text.IndexOf("It is fixed now.", StringComparison.Ordinal);
        Assert.True(you >= 0);
        Assert.True(claude > you);
        Assert.True(answer > claude);
    }

    [Fact]
    public void ThinkingResultsAndTurnFinishedLinesAreLeftOut()
    {
        var text = Sample();

        Assert.DoesNotContain("private thought", text);
        Assert.DoesNotContain("not exported", text);
        Assert.DoesNotContain("turn finished", text);
    }

    [Fact]
    public void AResumeRecapComesFirstWhenThereIsOne()
    {
        var text = Sample(recap: "Working on the build; one error left.");

        var recap = text.IndexOf("## Recap", StringComparison.Ordinal);
        var you = text.IndexOf("## You", StringComparison.Ordinal);
        Assert.True(recap >= 0);
        Assert.True(recap < you);
        Assert.Contains("Working on the build; one error left.", text);
    }

    [Fact]
    public void AnEmptyTranscriptGivesTheHeaderOnly()
    {
        var text = Markdown([]);

        Assert.StartsWith("# Fix the build", text);
        Assert.DoesNotContain("## You", text);
        Assert.DoesNotContain("## Claude", text);
    }
}

using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// What the transcript view copies and shows in full (T3 batch): a message as written, a Bash command, and a tool result whole and capped at
/// <see cref="TranscriptCopy.FullResultCap"/> characters, with the note that it was cut.
/// </summary>
public sealed class TranscriptCopyTests
{
    private static string Result(string content, string id = "t1") =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-19T09:00:06.000Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\""
        + id + "\",\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}]}}";

    private static string Bash(string command) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Bash\",\"input\":{\"command\":"
        + System.Text.Json.JsonSerializer.Serialize(command) + "}}]}}";

    [Fact]
    public void TheTextOfAMessageIsTheWholeMessage()
    {
        var message = new string('w', 500) + " café 😀";
        var line = TranscriptLine.From(1, "{\"type\":\"user\",\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(message) + "}}");

        Assert.Equal(message, TranscriptCopy.Text(line!));
    }

    [Fact]
    public void AResultLongerThanTheCapIsCutAndSaysHowLongItWas()
    {
        var text = new string('x', 250_000);

        var full = TranscriptCopy.FullResult(Result(text));

        Assert.Equal(TranscriptCopy.FullResultCap, full.Text.Length);
        Assert.Equal(250_000, full.Length);
        Assert.True(full.Truncated);
    }

    [Fact]
    public void AResultUnderTheCapIsShownWhole()
    {
        var text = new string('y', 1_000) + " the end";

        var full = TranscriptCopy.FullResult(Result(text));

        Assert.Equal(text, full.Text);
        Assert.Equal(text.Length, full.Length);
        Assert.False(full.Truncated);
    }

    [Fact]
    public void TheCutDoesNotSplitASurrogatePair()
    {
        // The emoji is at the cap: its high surrogate would be the last character kept, so the cut steps back before it.
        var text = new string('a', TranscriptCopy.FullResultCap - 1) + "😀";

        var full = TranscriptCopy.FullResult(Result(text));

        Assert.True(full.Truncated);
        Assert.False(char.IsHighSurrogate(full.Text[^1]));
        Assert.Equal(TranscriptCopy.FullResultCap - 1, full.Text.Length);
        Assert.Equal(text.Length, full.Length);
    }

    [Fact]
    public void AResultWithSeveralTextPartsIsJoinedWithNewLines()
    {
        const string raw = "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":"
                         + "[{\"type\":\"text\",\"text\":\"first part\"},{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"second part\"}]}]}}";

        Assert.Equal("first part\nsecond part", TranscriptCopy.FullResult(raw).Text);
    }

    [Fact]
    public void AnEmptyLineGivesAnEmptyTextWithNoNote()
    {
        var full = TranscriptCopy.FullResult(string.Empty);

        Assert.Equal(string.Empty, full.Text);
        Assert.Equal(0, full.Length);
        Assert.False(full.Truncated);
    }

    [Fact]
    public void AMalformedLineShowsItsRawText()
    {
        var full = TranscriptCopy.FullResult("{not json at all");

        Assert.Equal("{not json at all", full.Text);
        Assert.False(full.Truncated);
    }

    [Fact]
    public void ARecordWithoutAResultShowsItsRawLine()
    {
        const string prompt = "{\"type\":\"user\",\"message\":{\"content\":\"make hello.txt\"}}";

        Assert.Equal(prompt, TranscriptCopy.FullResult(prompt).Text);
    }

    [Fact]
    public void TheCommandOfABashCallIsItsFullText()
    {
        var command = "dotnet build -c Release && dotnet test --no-build " + new string('z', 300);

        Assert.Equal(command, TranscriptCopy.Command(Bash(command)));
    }

    [Fact]
    public void NoCommandWhenTheCallIsNotBashOrTheTextIsEmptyOrNotJson()
    {
        const string write = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Write\",\"input\":{\"file_path\":\"a.txt\"}}]}}";

        Assert.Null(TranscriptCopy.Command(write));
        Assert.Null(TranscriptCopy.Command(Bash(string.Empty)));
        Assert.Null(TranscriptCopy.Command("{broken"));
        Assert.Null(TranscriptCopy.Command(string.Empty));
    }

    [Fact]
    public void CapKeepsAShortTextAsItIs()
    {
        var kept = TranscriptCopy.Cap("short");

        Assert.Equal(new TranscriptCopy.FullText("short", 5, false), kept);
    }
}

using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

public sealed class TranscriptPreviewTests
{
    [Fact]
    public void APreviewCutDoesNotSplitAnEmoji()
    {
        // 119 letters, then an emoji (two UTF-16 units) across the 120-character cut.
        var text = new string('a', 119) + "😀" + "tail";
        var json = "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"content\":\"" + text + "\"}]}}";

        var line = TranscriptLine.From(1, json);

        Assert.NotNull(line);
        Assert.EndsWith(new string('a', 119) + "…", line.Text);
    }

    [Fact]
    public void AShortenedPathDoesNotStartInsideAnEmoji()
    {
        var path = "C:/" + "😀" + new string('b', 98);
        var json = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{\"file_path\":\"" + path + "\"}}]}}";

        var line = TranscriptLine.From(1, json);

        Assert.NotNull(line);
        Assert.False(line.Text.Contains("…") && char.IsLowSurrogate(line.Text[line.Text.IndexOf('…') + 1]));
    }
}

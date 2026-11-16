using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>D-L8: the start text names the session it continues, on its first line, and the reader reads it back.</summary>
public sealed class ResumeContinuesLineTests
{
    private const string Source = "17a94483-99fc-4005-9fa9-dbd63958653d";

    [Fact]
    public void TheStartTextNamesTheSourceSessionOnItsFirstLine()
    {
        var text = ResumeText.Compose("Goal: X.", SummaryLevel.Normal, "you: hi", turns: 1, sourceSessionId: Source);

        // AppendLine writes the platform newline, so the first line is compared without it.
        Assert.Equal("Continues session: " + Source, text.Split(Environment.NewLine)[0]);
    }

    [Fact]
    public void NoLineIsWrittenWithoutASource()
    {
        var text = ResumeText.Compose(null, null, "you: hi", turns: 1);

        Assert.DoesNotContain("Continues session:", text);
    }

    [Fact]
    public void TheReaderReadsTheLineBack()
    {
        var text = ResumeText.Compose("Goal: X.", SummaryLevel.Normal, "you: hi", turns: 1, sourceSessionId: Source);
        using var document = JsonDocument.Parse("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(text) + "}}");

        Assert.Equal(Source, SessionTitle.ContinuesIdOf(document.RootElement));
    }
}

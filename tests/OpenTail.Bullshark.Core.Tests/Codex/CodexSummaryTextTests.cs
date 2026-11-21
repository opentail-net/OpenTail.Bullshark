using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>Stage S2: the instruction of each summary level (S-D4). Pure text checks.</summary>
public sealed class CodexSummaryTextTests
{
    [Theory]
    [InlineData(SummaryLevel.Minimal, "at most 120 words")]
    [InlineData(SummaryLevel.Normal, "at most 400 words")]
    [InlineData(SummaryLevel.Complete, "at most 1,200 words")]
    public void EachLevelHasItsWordLimitAndNamesACodexCliSession(SummaryLevel level, string limit)
    {
        var text = CodexSummaryText.Instructions(level);

        Assert.Contains(limit, text);
        Assert.Contains("Codex CLI session", text);
        Assert.DoesNotContain("Claude", text);
    }

    [Fact]
    public void TheThreeInstructionsAreDifferent()
    {
        var texts = new[]
        {
            CodexSummaryText.Instructions(SummaryLevel.Minimal),
            CodexSummaryText.Instructions(SummaryLevel.Normal),
            CodexSummaryText.Instructions(SummaryLevel.Complete),
        };

        Assert.Equal(3, texts.Distinct().Count());
    }

    [Fact]
    public void AnUnknownLevelIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexSummaryText.Instructions((SummaryLevel)99));
    }
}

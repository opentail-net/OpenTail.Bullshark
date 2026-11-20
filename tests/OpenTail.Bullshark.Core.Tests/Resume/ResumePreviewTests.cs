using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>The resume prompt's preview: the size line, and the summary that a kept summary matches (resume preview, 2026-11-20).</summary>
public sealed class ResumePreviewTests
{
    [Fact]
    public void TheSizeLineSaysTokensAndCharactersWithNoCut()
    {
        Assert.Equal(
            "Prompt the new session starts with: about 100 tokens (400 characters).",
            ResumePreviewText.Header(400, 0, true));
    }

    [Theory]
    [InlineData(401, 100)]
    [InlineData(402, 101)]
    [InlineData(0, 0)]
    public void TheTokensAreTheCharactersOverFourRounded(int characters, int tokens)
    {
        Assert.Contains($"about {tokens:N0} tokens ({characters:N0} characters).", ResumePreviewText.Header(characters, 0, true));
    }

    [Fact]
    public void ACutIsSaidWithItsCount()
    {
        var header = ResumePreviewText.Header(1_000, 250, true);

        Assert.Contains("250 characters were cut to fit the command line.", header);
        Assert.Contains("(1,000 characters)", header);
        Assert.DoesNotContain("too long", header);
    }

    [Fact]
    public void ATextThatDoesNotFitSaysSoAndThatResumeWouldNotStartIt()
    {
        var header = ResumePreviewText.Header(9_000, 0, false);

        Assert.Contains("too long for the command line", header);
        Assert.Contains("Resume would not start it", header);
    }

    [Fact]
    public void AKeptSummaryMatchesOnlyItsOwnLevel()
    {
        var kept = new KeptSummary(SummaryLevel.Normal, "The summary.", Saved: true);

        Assert.True(kept.MatchesLevel(SummaryLevel.Normal));
        Assert.False(kept.MatchesLevel(SummaryLevel.Minimal));
        Assert.False(kept.MatchesLevel(SummaryLevel.Complete));
    }

    [Fact]
    public void AKeptSummaryNeverMatchesNoLevel()
    {
        var kept = new KeptSummary(SummaryLevel.Normal, "The summary.", Saved: true);

        Assert.False(kept.MatchesLevel(null));
    }
}

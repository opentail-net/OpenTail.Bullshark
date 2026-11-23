using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The Summary mode's decisions (RS-D19 to RS-D26): the Zero level, the tail, a summary tied to its tail (RS-D21), the refusal of a Zero start with no tail,
/// and Make summary when the tail covers the whole session. Pure: no window.
/// </summary>
public sealed class ResumeSummaryStateTests
{
    private static KeptSummary Kept(SummaryLevel level, int turns) => new(level, "A summary.", Saved: true, Turns: turns);

    [Fact]
    public void TheTurnsAreZeroWhenTheTailIsOff()
    {
        Assert.Equal(0, ResumeSummaryState.TurnsOf(tailOn: false, turns: 10));
        Assert.Equal(10, ResumeSummaryState.TurnsOf(tailOn: true, turns: 10));
    }

    [Fact]
    public void AZeroStartWithTheTailOffIsRefusedWithItsStatusLine()
    {
        Assert.Equal(ResumeSummaryState.NothingToStartLine, ResumeSummaryState.ZeroRefusal(null, tailOn: false, turns: 0));
        Assert.Equal("Nothing to start from: tick the tail or choose a summary level.", ResumeSummaryState.NothingToStartLine);
    }

    [Fact]
    public void AZeroStartWithAnEmptyTailIsRefused()
    {
        Assert.Equal(ResumeSummaryState.NothingToStartLine, ResumeSummaryState.ZeroRefusal(null, tailOn: true, turns: 0));
    }

    [Fact]
    public void AZeroStartWithTheTailOnIsAllowed()
    {
        Assert.Null(ResumeSummaryState.ZeroRefusal(null, tailOn: true, turns: 10));
    }

    [Theory]
    [InlineData(SummaryLevel.Minimal)]
    [InlineData(SummaryLevel.Normal)]
    [InlineData(SummaryLevel.Complete)]
    public void ASummaryLevelIsNeverRefusedAsZero(SummaryLevel level)
    {
        Assert.Null(ResumeSummaryState.ZeroRefusal(level, tailOn: false, turns: 0));
    }

    [Fact]
    public void MakeSummaryNeedsALevelValidTurnsAndLinesToRead()
    {
        Assert.False(ResumeSummaryState.MakeSummaryAllowed(null, turnsValid: true, summaryHasLines: true));
        Assert.False(ResumeSummaryState.MakeSummaryAllowed(SummaryLevel.Normal, turnsValid: false, summaryHasLines: true));
        Assert.False(ResumeSummaryState.MakeSummaryAllowed(SummaryLevel.Normal, turnsValid: true, summaryHasLines: false));
        Assert.True(ResumeSummaryState.MakeSummaryAllowed(SummaryLevel.Normal, turnsValid: true, summaryHasLines: true));
    }

    [Fact]
    public void TheNothingToSummariseLineIsTheOwnersWords()
    {
        Assert.Equal("The tail covers the whole session, so there is nothing to summarise.", ResumeSummaryState.NothingToSummariseLine);
    }

    [Fact]
    public void ZeroHasNoSummaryToKeep()
    {
        Assert.True(ResumeSummaryState.KeptMatches(null, 10, kept: null));
    }

    [Fact]
    public void ASummaryMatchesOnlyItsLevelAndItsTail()
    {
        var kept = Kept(SummaryLevel.Normal, turns: 10);

        Assert.True(ResumeSummaryState.KeptMatches(SummaryLevel.Normal, 10, kept));
        Assert.False(ResumeSummaryState.KeptMatches(SummaryLevel.Minimal, 10, kept));
        Assert.False(ResumeSummaryState.KeptMatches(SummaryLevel.Normal, 5, kept));
        Assert.False(ResumeSummaryState.KeptMatches(SummaryLevel.Normal, 0, kept));
        Assert.False(ResumeSummaryState.KeptMatches(SummaryLevel.Normal, 10, kept: null));
    }

    [Fact]
    public void ASummaryMadeWithTheTailOffIsOutOfDateWhenTheTailIsTicked()
    {
        var kept = Kept(SummaryLevel.Normal, turns: 0);

        Assert.True(ResumeSummaryState.IsOutOfDate(SummaryLevel.Normal, 10, kept));
        Assert.False(ResumeSummaryState.IsOutOfDate(SummaryLevel.Normal, 0, kept));
    }

    [Fact]
    public void NothingIsOutOfDateWithoutASummaryOrAtZero()
    {
        Assert.False(ResumeSummaryState.IsOutOfDate(SummaryLevel.Normal, 10, kept: null));
        Assert.False(ResumeSummaryState.IsOutOfDate(null, 10, Kept(SummaryLevel.Normal, turns: 10)));
    }

    [Fact]
    public void AChangedTailMakesTheSummaryOutOfDateAndStartWaits()
    {
        var kept = Kept(SummaryLevel.Normal, turns: 10);

        // The owner changes N to 5: the preview is hidden (previewReady false), the summary is out of date, and Start is off.
        Assert.True(ResumeSummaryState.IsOutOfDate(SummaryLevel.Normal, 5, kept));
        Assert.False(ResumeSummaryState.StartAllowed(SummaryLevel.Normal, tailOn: true, turns: 5, turnsValid: true, previewReady: false, kept));
    }

    [Fact]
    public void StartNeedsTheTextShownAndAMatchingSummary()
    {
        var kept = Kept(SummaryLevel.Normal, turns: 10);

        Assert.True(ResumeSummaryState.StartAllowed(SummaryLevel.Normal, tailOn: true, turns: 10, turnsValid: true, previewReady: true, kept));
        Assert.False(ResumeSummaryState.StartAllowed(SummaryLevel.Normal, tailOn: true, turns: 10, turnsValid: true, previewReady: true, kept: null));
        Assert.False(ResumeSummaryState.StartAllowed(SummaryLevel.Normal, tailOn: true, turns: 10, turnsValid: false, previewReady: true, kept));
    }

    [Fact]
    public void AZeroStartNeedsItsTailAndNoSummary()
    {
        Assert.True(ResumeSummaryState.StartAllowed(null, tailOn: true, turns: 10, turnsValid: true, previewReady: true, kept: null));
        Assert.False(ResumeSummaryState.StartAllowed(null, tailOn: false, turns: 0, turnsValid: true, previewReady: true, kept: null));
        Assert.False(ResumeSummaryState.StartAllowed(null, tailOn: true, turns: 10, turnsValid: true, previewReady: false, kept: null));
    }

    [Fact]
    public void AZeroTextIsBuiltAtOnceAndASummaryOnlyWhenKeptForTheSetting()
    {
        Assert.True(ResumeSummaryState.RunsAtOnce(null, 10, kept: null));
        Assert.False(ResumeSummaryState.RunsAtOnce(SummaryLevel.Normal, 10, kept: null));
        Assert.True(ResumeSummaryState.RunsAtOnce(SummaryLevel.Normal, 10, Kept(SummaryLevel.Normal, turns: 10)));
        Assert.False(ResumeSummaryState.RunsAtOnce(SummaryLevel.Normal, 5, Kept(SummaryLevel.Normal, turns: 10)));
    }
}

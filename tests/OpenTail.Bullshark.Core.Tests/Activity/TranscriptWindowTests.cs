using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The window arithmetic of the transcript view (perf and testability batch, Q3): where a view opens, the earlier page, the release past the cap, and
/// whether a line is drawn. The pager's own page reads are in TranscriptPagerTests; this file is the pure arithmetic only.
/// </summary>
public sealed class TranscriptWindowTests
{
    [Fact]
    public void WithNoFocusTheViewOpensOnTheLastPage()
    {
        // 16,519 records and a page of 200: the last page starts at record 16,319 (zero-based).
        Assert.Equal(16_319, TranscriptWindow.StartFor(16_519, 200, null));
    }

    [Fact]
    public void AFocusLineIsCentredInThePage()
    {
        // Line 1,000 (one-based) is record 999; the page of 200 starts 100 records before it.
        Assert.Equal(899, TranscriptWindow.StartFor(16_519, 200, 1_000));
    }

    [Fact]
    public void AFocusNearTheStartOrTheEndStaysInsideTheFile()
    {
        Assert.Equal(0, TranscriptWindow.StartFor(16_519, 200, 1));
        Assert.Equal(0, TranscriptWindow.StartFor(16_519, 200, 50));
        // A focus past the end (a stale line number) is clamped to the last record, and the page is centred on that record.
        Assert.Equal(16_418, TranscriptWindow.StartFor(16_519, 200, 99_999));
    }

    [Fact]
    public void AFileWithNoRecordsOpensAtZero()
    {
        Assert.Equal(0, TranscriptWindow.StartFor(0, 200, null));
        Assert.Equal(0, TranscriptWindow.StartFor(0, 200, 5));
    }

    [Fact]
    public void TheEarlierPageStartsOnePageBeforeAndNotBelowZero()
    {
        Assert.Equal(16_119, TranscriptWindow.EarlierStart(16_319, 200));
        Assert.Equal(0, TranscriptWindow.EarlierStart(120, 200));
        Assert.Equal(0, TranscriptWindow.EarlierStart(0, 200));
    }

    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(999, 1000, 0)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1001, 1000, 1)]
    [InlineData(1200, 1000, 200)]
    public void OnlyTheLinesPastTheCapAreReleased(int drawn, int cap, int released) =>
        Assert.Equal(released, TranscriptWindow.LinesToRelease(drawn, cap));

    [Fact]
    public void TheDefaultCapIsAThousandLines()
    {
        Assert.Equal(1000, TranscriptWindow.DefaultCap);
        Assert.Equal(0, TranscriptWindow.LinesToRelease(TranscriptWindow.DefaultCap, TranscriptWindow.DefaultCap));
    }

    [Fact]
    public void ALineInsideTheDrawnRangeIsHeldAndOneOutsideIsNot()
    {
        // A jump target below the released range is not drawn: the view reads a window around it instead.
        Assert.True(TranscriptWindow.Holds(900, 1800, 900));
        Assert.True(TranscriptWindow.Holds(900, 1800, 1800));
        Assert.False(TranscriptWindow.Holds(900, 1800, 1801));
        Assert.False(TranscriptWindow.Holds(900, 1800, 899));
    }

    [Fact]
    public void TheNoteSaysWhatIsShownAndHowToGetTheRestBack()
    {
        var note = TranscriptWindow.ReleasedNote(250, 1, 1000);

        Assert.NotNull(note);
        Assert.Contains("Showing lines 1–1,000.", note);
        Assert.Contains("250 lines at the end were released", note);
        Assert.Contains("Find or a jump", note);
    }

    [Fact]
    public void NoNoteWhenNothingWasReleased()
    {
        Assert.Null(TranscriptWindow.ReleasedNote(0, 1, 200));
    }
}

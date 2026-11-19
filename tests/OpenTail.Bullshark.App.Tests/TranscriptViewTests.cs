using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The transcript view, built without a window (perf and testability batch, Q7). The tests check structure only: the lines drawn, the focus line, the
/// cap on the lines kept, the note about released lines, and the find count. No picture is compared.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TranscriptViewTests
{
    [Fact]
    public void AShownPageHoldsTwoHundredLinesAndTheFocusLine()
    {
        using var fixture = new TranscriptFixture(3000, n => "message " + n);
        var view = UiThread.Run(() => new TranscriptView());

        UiThread.Run(() => view.ShowAsync(fixture.Digest, focusLine: 1500));

        Assert.True(UiThread.WaitUntil(() => view.BlockCount == 200));
        Assert.Equal(1500, UiThread.Run(() => view.FocusLineNumber));
        Assert.Null(UiThread.Run(() => view.WindowNoteText));
    }

    [Fact]
    public void LoadingEarlierPastTheCapReleasesTheFarLinesAndSaysSo()
    {
        using var fixture = new TranscriptFixture(3000, n => "message " + n);
        var view = UiThread.Run(() => new TranscriptView());
        UiThread.Run(() => view.ShowAsync(fixture.Digest, focusLine: 2900));
        Assert.True(UiThread.WaitUntil(() => view.BlockCount == 200));

        // Each earlier page adds 200 lines. The view holds at most the cap of 1,000, and the note appears once lines are released.
        for (var loads = 1; loads <= 6; loads++)
        {
            UiThread.Run(() => view.LoadEarlierForTest());
            var expected = Math.Min(200 * (loads + 1), TranscriptWindow.DefaultCap);
            // The page is read on a pool thread: wait until the button is enabled again and the count has its value.
            var settled = UiThread.WaitUntil(() => view.EarlierEnabled && view.BlockCount == expected);
            Assert.True(settled, $"after {loads} loads the view holds {UiThread.Run(() => view.BlockCount)} lines, not {expected}");
        }

        Assert.Equal(TranscriptWindow.DefaultCap, UiThread.Run(() => view.BlockCount));
        var note = UiThread.Run(() => view.WindowNoteText);
        Assert.NotNull(note);
        Assert.Contains("released", note);
    }

    [Fact]
    public void AJumpToALineOutsideTheWindowShowsThatLineAgain()
    {
        using var fixture = new TranscriptFixture(3000, n => "message " + n);
        var view = UiThread.Run(() => new TranscriptView());
        UiThread.Run(() => view.ShowAsync(fixture.Digest, focusLine: 2900));
        Assert.True(UiThread.WaitUntil(() => view.BlockCount == 200));
        for (var loads = 1; loads <= 6; loads++)
        {
            UiThread.Run(() => view.LoadEarlierForTest());
            var expected = Math.Min(200 * (loads + 1), TranscriptWindow.DefaultCap);
            Assert.True(UiThread.WaitUntil(() => view.EarlierEnabled && view.BlockCount == expected));
        }

        // A search result or a jump to line 100, which the released window does not hold, shows the page around it again.
        UiThread.Run(() => view.ShowAsync(fixture.Digest, focusLine: 100));

        Assert.True(UiThread.WaitUntil(() => view.BlockCount == 200));
        Assert.Equal(100, UiThread.Run(() => view.FocusLineNumber));
        Assert.Null(UiThread.Run(() => view.WindowNoteText));
    }

    [Fact]
    public void TheFindCountReadsNOfMForASmallFixture()
    {
        using var fixture = new TranscriptFixture(50, n => n % 10 == 0 ? "needle " + n : "plain " + n);
        var view = UiThread.Run(() => new TranscriptView());
        UiThread.Run(() => view.ShowAsync(fixture.Digest));
        Assert.True(UiThread.WaitUntil(() => view.BlockCount > 0));

        UiThread.Run(() => view.OpenFind("needle"));

        var reached = UiThread.WaitUntil(() => UiThread.Run(() => view.FindCountText).EndsWith(" of 5", StringComparison.Ordinal));
        Assert.True(reached, $"the find count stayed at \"{UiThread.Run(() => view.FindCountText)}\"");
        Assert.Matches(@"^\d+ of 5$", UiThread.Run(() => view.FindCountText));
    }
}

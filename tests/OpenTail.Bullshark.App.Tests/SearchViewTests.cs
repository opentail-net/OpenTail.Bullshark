using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The search results, built without a window (perf and testability batch, Q7): one block per session, the newest matching lines within the cap
/// (HistorySearch.HitsPerSession), and one keyboard row per line. Structure only.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SearchViewTests
{
    [Fact]
    public void ThePerSessionCapIsThree()
    {
        Assert.Equal(3, HistorySearch.HitsPerSession);
    }

    [Fact]
    public void EachSessionIsOneBlockWithAtMostTheCapOfHitLines()
    {
        using var fixture = new TranscriptFixture(20, n => "needle " + n);
        var matches = HistorySearch.Run(new[] { fixture.Digest }, "needle");
        Assert.Single(matches);
        Assert.True(matches[0].Hits.Count <= HistorySearch.HitsPerSession);
        Assert.Equal(HistorySearch.HitsPerSession, matches[0].Hits.Count);

        var view = UiThread.Run(() => new SearchView());
        UiThread.Run(() => view.ShowResults("needle", matches));

        var names = UiThread.Run(() => LogicalTree.Walk(view)
            .Select(element => AutomationProperties.GetName(element))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList());
        Assert.Equal(1, names.Count(name => name.EndsWith("Open at the newest matching message.", StringComparison.Ordinal)));
        Assert.Equal(matches[0].Hits.Count, names.Count(name => name.StartsWith("Line ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnEmptyResultSaysSoInTheHeading()
    {
        var view = UiThread.Run(() => new SearchView());
        UiThread.Run(() => view.ShowResults("nothing here", []));

        var heading = UiThread.Run(() => LogicalTree.Walk(view).OfType<TextBlock>().First().Text);
        Assert.StartsWith("No session says every word", heading, StringComparison.Ordinal);
    }
}

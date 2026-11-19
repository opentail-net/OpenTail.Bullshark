using System.Globalization;
using OpenTail.Bullshark.Core.Search;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Search;

/// <summary>The "searched N sessions" line of an empty search result (firstrun batch B8).</summary>
public sealed class SearchedSentenceTests
{
    [Fact]
    public void ZeroSessionsSaysThereIsNothingToSearchYet()
    {
        Assert.Equal("No transcripts to search yet.", SearchedSentence.Of(0));
    }

    [Fact]
    public void OneSessionIsSingular()
    {
        Assert.Equal("Searched 1 session.", SearchedSentence.Of(1));
    }

    [Fact]
    public void ManySessionsGiveTheCountInTheCurrentCulture()
    {
        Assert.Equal($"Searched {195.ToString("N0", CultureInfo.CurrentCulture)} sessions.", SearchedSentence.Of(195));
        Assert.Equal($"Searched {1234.ToString("N0", CultureInfo.CurrentCulture)} sessions.", SearchedSentence.Of(1234));
    }

    [Fact]
    public void AnUnknownCountSaysNothing()
    {
        Assert.Equal(string.Empty, SearchedSentence.Of(null));
    }
}

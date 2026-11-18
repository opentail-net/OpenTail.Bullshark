using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>The tags in use and their counts, and the tag filter's test (tags plan, Part A1). Pure: no file, no window.</summary>
public sealed class TagIndexTests
{
    [Fact]
    public void ACountIsOfSessionsNotOfOccurrences()
    {
        var entries = new[]
        {
            new LibraryEntry(Tags: ["auth", "auth", "login"]),
            new LibraryEntry(Tags: ["login"]),
        };

        var counts = TagIndex.Counts(entries);

        Assert.Equal([new TagCount("login", 2), new TagCount("auth", 1)], counts);
    }

    [Fact]
    public void TagsAreCountedWithoutCase()
    {
        var entries = new[]
        {
            new LibraryEntry(Tags: ["Auth"]),
            new LibraryEntry(Tags: ["auth", "AUTH"]),
        };

        var counts = TagIndex.Counts(entries);

        Assert.Equal([new TagCount("auth", 2)], counts);
    }

    [Fact]
    public void EmptyNullAndBlankTagsAreLeftOut()
    {
        var entries = new[]
        {
            new LibraryEntry(),
            new LibraryEntry(Tags: []),
            new LibraryEntry(Tags: [null!, "", "   ", "cache"]),
            new LibraryEntry(Pinned: true),
        };

        Assert.Equal([new TagCount("cache", 1)], TagIndex.Counts(entries));
    }

    [Fact]
    public void NoEntriesGiveNoTags()
    {
        Assert.Empty(TagIndex.Counts([]));
    }

    [Fact]
    public void TheMostUsedComeFirstThenByName()
    {
        var entries = new[]
        {
            new LibraryEntry(Tags: ["zeta", "beta"]),
            new LibraryEntry(Tags: ["alpha", "beta"]),
            new LibraryEntry(Tags: ["gamma"]),
            new LibraryEntry(Tags: ["alpha"]),
        };

        var names = TagIndex.Counts(entries).Select(tag => tag.Tag);

        // beta and alpha have two each (alpha first by name), then the two with one each by name.
        Assert.Equal(["alpha", "beta", "gamma", "zeta"], names);
    }

    [Fact]
    public void AMarkHoldsItsTagWithoutCase()
    {
        var mark = new LibraryEntry(Tags: ["opentail"]);

        Assert.True(TagIndex.Has(mark, "OpenTail"));
        Assert.True(TagIndex.Has(mark, "opentail"));
    }

    [Fact]
    public void AMarkWithoutTheTagIsNotShown()
    {
        var mark = new LibraryEntry(Tags: ["opentail"]);

        Assert.False(TagIndex.Has(mark, "billing"));
        Assert.False(TagIndex.Has(new LibraryEntry(Name: "No tags"), "billing"));
    }

    [Fact]
    public void ANullOrBlankTagIsNoFilter()
    {
        Assert.True(TagIndex.Has(null, null));
        Assert.True(TagIndex.Has(null, ""));
        Assert.True(TagIndex.Has(null, "   "));
        Assert.True(TagIndex.Has(new LibraryEntry(), null));
    }

    [Fact]
    public void ASessionWithNoMarkHasNoTag()
    {
        Assert.False(TagIndex.Has(null, "billing"));
    }
}

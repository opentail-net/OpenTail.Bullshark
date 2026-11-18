using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>The command palette's matching (ideas D3): order, cap, scores, words and text that is not plain ASCII.</summary>
public sealed class CommandPaletteTests
{
    private static PaletteEntry Entry(string title, int order = 0, string? hint = null, string? id = null) =>
        new(id ?? title, title, hint, order);

    private static IReadOnlyList<string> IdsOf(IEnumerable<PaletteEntry> entries) => entries.Select(e => e.Id).ToList();

    [Fact]
    public void AnEmptyQueryListsTheEntriesInOrderThenInListOrder()
    {
        var entries = Enumerable.Range(0, 15).Select(i => new PaletteEntry($"e{i}", $"Entry {i}", null, i % 3)).ToList();

        var result = CommandPalette.Filter(entries, "");

        Assert.Equal(
            new[] { "e0", "e3", "e6", "e9", "e12", "e1", "e4", "e7", "e10", "e13", "e2", "e5" },
            IdsOf(result));
    }

    [Fact]
    public void AnEmptyQueryIsCappedAtTwelveByDefault()
    {
        var entries = Enumerable.Range(0, 30).Select(i => Entry($"Item {i}", id: $"i{i}")).ToList();

        Assert.Equal(12, CommandPalette.Filter(entries, null).Count);
        Assert.Equal(12, CommandPalette.Filter(entries, "item").Count);
    }

    [Fact]
    public void MaxIsRespectedAndAZeroOrNegativeMaxListsNothing()
    {
        var entries = new[] { Entry("Timeline"), Entry("Storage"), Entry("Limits") };

        Assert.Equal(2, CommandPalette.Filter(entries, "", max: 2).Count);
        Assert.Empty(CommandPalette.Filter(entries, "", max: 0));
        Assert.Empty(CommandPalette.Filter(entries, "t", max: -1));
    }

    [Fact]
    public void APrefixOfTheTitleBeatsAWordStartWhichBeatsASubsequence()
    {
        // Listed worst first, so the order alone would not put them right.
        var entries = new[]
        {
            Entry("Atomic lime", id: "subsequence"),
            Entry("Open timeline", id: "word start"),
            Entry("Timeline", id: "prefix"),
        };

        var result = CommandPalette.Filter(entries, "tim");

        Assert.Equal(new[] { "prefix", "word start", "subsequence" }, IdsOf(result));
    }

    [Fact]
    public void AScoreBeatsAnOrderWhenTheMatchIsBetter()
    {
        var entries = new[]
        {
            Entry("Open timeline", order: 2, id: "word start"),
            Entry("Timeline", order: 3, id: "prefix"),
        };

        Assert.Equal(new[] { "prefix", "word start" }, IdsOf(CommandPalette.Filter(entries, "tim")));
    }

    [Fact]
    public void EveryWordOfTheQueryMustMatch()
    {
        var entries = new[]
        {
            Entry("Go to session planner", id: "session"),
            Entry("Go to limits", id: "limits"),
        };

        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "go ses")));
        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "planner go")));
        Assert.Empty(CommandPalette.Filter(entries, "go zzz"));
    }

    [Fact]
    public void TheMatchIsCaseInsensitive()
    {
        var entries = new[] { Entry("Timeline") };

        Assert.Single(CommandPalette.Filter(entries, "TIMELINE"));
        Assert.Single(CommandPalette.Filter(entries, "tImElInE"));
    }

    [Fact]
    public void ANoMatchGivesAnEmptyList()
    {
        var entries = new[] { Entry("Timeline"), Entry("Storage") };

        Assert.Empty(CommandPalette.Filter(entries, "zzz"));
    }

    [Fact]
    public void LettersOfASubsequenceMustKeepTheirOrder()
    {
        var entries = new[] { Entry("Timeline") };

        Assert.Single(CommandPalette.Filter(entries, "tml"));
        Assert.Empty(CommandPalette.Filter(entries, "mt"));
    }

    [Fact]
    public void TiesKeepTheOrderTheyWereGivenIn()
    {
        var sameOrder = new[] { Entry("Open Storage", id: "storage"), Entry("Open Limits", id: "limits") };
        Assert.Equal(new[] { "storage", "limits" }, IdsOf(CommandPalette.Filter(sameOrder, "open")));

        var mixedOrder = new[] { Entry("Open Away", order: 3, id: "away"), Entry("Open Storage", order: 2, id: "storage") };
        Assert.Equal(new[] { "storage", "away" }, IdsOf(CommandPalette.Filter(mixedOrder, "open")));
    }

    [Fact]
    public void AnEntryWithNoHintIsListedAsItIs()
    {
        var entries = new[] { Entry("Timeline", hint: null, id: "timeline") };

        var result = CommandPalette.Filter(entries, "time");

        var only = Assert.Single(result);
        Assert.Equal("timeline", only.Id);
        Assert.Null(only.Hint);
    }

    [Fact]
    public void AWordStartMatchesAfterPunctuationAndDigits()
    {
        var entries = new[] { Entry("Go to session: planner-2") };

        Assert.Single(CommandPalette.Filter(entries, "planner"));
        Assert.Single(CommandPalette.Filter(entries, "2"));
        Assert.Single(CommandPalette.Filter(entries, "to ses"));
    }

    [Fact]
    public void AQueryOfOnlySpacesIsTheEmptyQuery()
    {
        var entries = Enumerable.Range(0, 5).Select(i => Entry($"Entry {i}", order: 4 - i, id: $"e{i}")).ToList();

        var empty = IdsOf(CommandPalette.Filter(entries, ""));

        Assert.Equal(empty, IdsOf(CommandPalette.Filter(entries, "   ")));
        Assert.Equal(empty, IdsOf(CommandPalette.Filter(entries, "\t ")));
        Assert.Equal(empty, IdsOf(CommandPalette.Filter(entries, null)));
    }

    [Fact]
    public void UnicodeAndAccentedTextDoNotThrow()
    {
        var entries = new[]
        {
            Entry("Café", id: "cafe-accent"),
            Entry("🚀 run", id: "rocket"),
            Entry("Ünïcode title", id: "unicode"),
            Entry("日本語 title", id: "japanese"),
            Entry("broken \uD800 text", id: "surrogate"),
        };

        Assert.Equal(new[] { "cafe-accent" }, IdsOf(CommandPalette.Filter(entries, "café")));
        Assert.Equal(new[] { "rocket" }, IdsOf(CommandPalette.Filter(entries, "🚀")));
        Assert.Equal(new[] { "unicode" }, IdsOf(CommandPalette.Filter(entries, "ünï")));
        Assert.Equal(new[] { "japanese" }, IdsOf(CommandPalette.Filter(entries, "日本")));
        // A lone surrogate is one character: it matches by subsequence where it sits, and nothing throws.
        Assert.Equal(new[] { "surrogate" }, IdsOf(CommandPalette.Filter(entries, "\uD800")));
        // Accents are not folded (the rule in the class summary): a plain "cafe" does not find "Café".
        Assert.Empty(CommandPalette.Filter(entries, "cafe"));
    }

    [Fact]
    public void AKeywordFindsAnEntryTheTitleDoesNot()
    {
        var entries = new[] { new PaletteEntry("session", "Go to session coder-1", null, 0, Keywords: "billing-api tagged") };

        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "billing")));
        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "api")));
        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "tagg")));
    }

    [Fact]
    public void ATitleMatchOutranksAKeywordOnlyMatchEvenWhenTheKeywordEntryIsFirst()
    {
        var entries = new[]
        {
            new PaletteEntry("keyword", "Open limits", null, 0, Keywords: "planner"),
            new PaletteEntry("title", "Planner notes", null, 5),
        };

        Assert.Equal(new[] { "title", "keyword" }, IdsOf(CommandPalette.Filter(entries, "planner")));
    }

    [Fact]
    public void AKeywordIsNeverMatchedAsASubsequence()
    {
        var entries = new[] { new PaletteEntry("session", "Go to session", null, 0, Keywords: "planner") };

        Assert.Empty(CommandPalette.Filter(entries, "pnr"));
        Assert.Empty(CommandPalette.Filter(entries, "lnr"));
        Assert.Single(CommandPalette.Filter(entries, "plan"));
    }

    [Fact]
    public void NullOrEmptyKeywordsGiveTheSameMatchesAsNoKeywords()
    {
        var plain = new[] { Entry("Open Storage", id: "storage"), Entry("Timeline", id: "timeline") };
        var empty = plain.Select(entry => entry with { Keywords = string.Empty }).ToList();

        foreach (var query in new[] { "sto", "tml", "open", "zzz", "" })
        {
            Assert.Equal(IdsOf(CommandPalette.Filter(plain, query)), IdsOf(CommandPalette.Filter(empty, query)));
        }
        Assert.Empty(CommandPalette.Filter(plain, "planner"));
    }

    [Fact]
    public void EveryWordMustMatchTheTitleOrTheKeywordsTogether()
    {
        var entries = new[]
        {
            new PaletteEntry("session", "Go to session", null, 0, Keywords: "billing"),
            new PaletteEntry("plain", "Go to session", null, 1),
        };

        // One word in the title and one in the keywords: the entry matches. The entry without keywords does not.
        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "session billing")));
        Assert.Equal(new[] { "session" }, IdsOf(CommandPalette.Filter(entries, "billing ses")));
        // A word in neither the title nor the keywords leaves the entry out.
        Assert.Empty(CommandPalette.Filter(entries, "session zzz"));
    }

    [Fact]
    public void ANullListIsAnArgumentError()
    {
        Assert.Throws<ArgumentNullException>(() => CommandPalette.Filter(null!, "x"));
    }
}

using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Find in one transcript (T4 batch): one substring without case, the text a line shows with a tool result whole, one hit per line, the cap, and
/// cancellation. The lines are raw records, as the file holds them.
/// </summary>
public sealed class TranscriptFindTests
{
    private static string Prompt(string text) => "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}}";

    private static string Reply(string text) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"m" + Guid.NewGuid().ToString("N")[..6] + "\",\"content\":[{\"type\":\"text\",\"text\":" +
        System.Text.Json.JsonSerializer.Serialize(text) + "}]}}";

    private static string Result(string content) =>
        "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":" +
        System.Text.Json.JsonSerializer.Serialize(content) + "}]}}";

    private static int[] Hits(IEnumerable<string> lines, string query) => TranscriptFind.Run(lines, query).Lines.ToArray();

    [Fact]
    public void TheQueryMatchesWithoutCase()
    {
        var lines = new[] { Prompt("Build FAILED on CI"), Prompt("nothing here") };

        Assert.Equal([1], Hits(lines, "build failed"));
        Assert.Equal([1], Hits(lines, "BUILD"));
        Assert.Equal([1], Hits(lines, "ci"));
    }

    [Fact]
    public void TheQueryIsOneSubstringNotEachWordAlone()
    {
        var lines = new[] { Prompt("the failed build") };

        Assert.Empty(Hits(lines, "build failed"));
        Assert.Equal([1], Hits(lines, "failed build"));
    }

    [Fact]
    public void ALineIsAHitOnceHoweverOftenTheQueryIsInIt()
    {
        var lines = new[] { Prompt("x x x x"), Prompt("no") };

        Assert.Equal([1], Hits(lines, "x"));
    }

    [Fact]
    public void RangesDoNotOverlapAndAreFoundWithoutCase()
    {
        Assert.Equal([(0, 2), (2, 2)], TranscriptFind.Ranges("aaaa", "aa"));
        Assert.Equal([(0, 2)], TranscriptFind.Ranges("aaa", "aa"));
        Assert.Equal([(0, 2), (2, 2)], TranscriptFind.Ranges("abAB", "ab"));
        Assert.Empty(TranscriptFind.Ranges("", "a"));
        Assert.Empty(TranscriptFind.Ranges("abc", ""));
        Assert.Empty(TranscriptFind.Ranges("ab", "abc"));
    }

    [Fact]
    public void AHitInTheHiddenPartOfACutResultCounts()
    {
        // The line shows the first 120 characters of a result; the needle is after them, so only the whole result has it.
        var lines = new[] { Result(new string('x', 300) + " NEEDLE at the end") };

        Assert.Equal([1], Hits(lines, "needle"));
    }

    [Fact]
    public void ToolCallsCountTheirNameFileAndCommandWhole()
    {
        var command = "npm run lint -- --max-warnings 0 " + new string('q', 200) + " LASTFLAG";
        var bash = "{\"type\":\"assistant\",\"message\":{\"id\":\"m9\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t9\",\"name\":\"Bash\",\"input\":{\"command\":"
                   + System.Text.Json.JsonSerializer.Serialize(command) + "}}]}}";
        var write = "{\"type\":\"assistant\",\"message\":{\"id\":\"m8\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t8\",\"name\":\"Write\",\"input\":{\"file_path\":\"C:/work/hello.txt\"}}]}}";

        Assert.Equal([1], Hits([bash], "LASTFLAG"));
        Assert.Equal([1], Hits([write], "hello.txt"));
        Assert.Equal([1], Hits([write], "Write"));
    }

    [Fact]
    public void MetadataAndLinesThatAreNotJsonAreNotSearched()
    {
        var lines = new[]
        {
            "{\"type\":\"file-history-snapshot\",\"snapshot\":{\"text\":\"needle\"}}",
            "needle in a line that is not json",
            Prompt("needle"),
        };

        Assert.Equal([3], Hits(lines, "needle"));
    }

    [Fact]
    public void AQueryWithAQuoteOrABackslashFindsTheEscapedText()
    {
        // Claude writes these escaped in the file: the pre-filter looks for the part that is not escaped, then the text is checked.
        var lines = new[] { Prompt("say \"hi\" to C:\\Windows\\System32"), Prompt("plain") };

        Assert.Equal([1], Hits(lines, "say \"hi\""));
        Assert.Equal([1], Hits(lines, "C:\\Windows"));
    }

    [Fact]
    public void AQueryWithAnAccentFindsTheAccentedText()
    {
        Assert.Equal([1], Hits([Prompt("le caf\u00e9 du coin")], "caf\u00e9"));
    }

    [Fact]
    public void TheCapKeepsTheFirstHitsAndSaysThereIsMore()
    {
        var lines = Enumerable.Range(1, 5).Select(i => Prompt("hit " + i)).ToList();

        var capped = TranscriptFind.Run(lines, "hit", maxHits: 3);
        var exact = TranscriptFind.Run(lines, "hit", maxHits: 5);

        Assert.Equal([1, 2, 3], capped.Lines);
        Assert.True(capped.Capped);
        Assert.Equal([1, 2, 3, 4, 5], exact.Lines);
        Assert.False(exact.Capped);
    }

    [Fact]
    public void TheDefaultCapIsTwoThousandHits()
    {
        Assert.Equal(2000, TranscriptFind.MaxHits);
    }

    [Fact]
    public void ABlankQueryHasNoHits()
    {
        var result = TranscriptFind.Run([Prompt("anything")], "   ");

        Assert.Empty(result.Lines);
        Assert.False(result.Capped);
    }

    [Fact]
    public void ACancelledSearchStops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => TranscriptFind.Run([Prompt("needle")], "needle", cancel.Token));
    }

    [Fact]
    public void AnAssistantTextIsSearchedAsItsMessage()
    {
        Assert.Equal([2], Hits([Prompt("first"), Reply("The build is green now.")], "build is green"));
    }
}

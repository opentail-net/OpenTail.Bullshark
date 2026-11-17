using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.Core.Tests.Search;

public sealed class HistorySearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-search-" + Guid.NewGuid().ToString("N")[..8]);

    public HistorySearchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string You(string text) =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-16T10:00:00Z\",\"message\":{\"role\":\"user\",\"content\":" +
        JsonSerializer.Serialize(text) + "}}";

    private static string Claude(string text) =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-16T10:01:00Z\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":" +
        JsonSerializer.Serialize(text) + "}]}}";

    private static string ToolResult(string text) =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-16T10:02:00Z\",\"message\":{\"role\":\"user\",\"content\":" +
        "[{\"type\":\"tool_result\",\"content\":" + JsonSerializer.Serialize(text) + "}]}}";

    /// <summary>A session whose transcript is written to a file, with the digest the rail would hold for it.</summary>
    private SessionDigest Session(string id, string[] lines, DateTimeOffset last, bool subagent = false)
    {
        var path = Path.Combine(_dir, id + ".jsonl");
        File.WriteAllLines(path, lines);
        return new SessionDigest(id, path, "C--Git", subagent, null, last, 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);
    }

    private static readonly DateTimeOffset Day = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryWordIsNeeded()
    {
        var onlyAlpha = Session("a", [You("alpha only here")], Day);
        var both = Session("b", [You("alpha and beta together")], Day.AddMinutes(-1));

        var results = HistorySearch.Run([onlyAlpha, both], "alpha beta");

        Assert.Equal(["b"], results.Select(r => r.Session.SessionId));
    }

    [Fact]
    public void TheWordsMayBeInDifferentMessages()
    {
        var session = Session("a", [You("we discussed the alpha plan"), Claude("the beta part is next")], Day);

        var results = HistorySearch.Run([session], "alpha beta");

        Assert.Single(results);
        Assert.Equal(2, results[0].MatchingMessages);
    }

    [Fact]
    public void ToolResultsAreNotSearched()
    {
        var session = Session("a", [ToolResult("zeta appears only in a tool result"), You("nothing here")], Day);

        Assert.Empty(HistorySearch.Run([session], "zeta"));
    }

    [Fact]
    public void SubagentsAreLeftOutUnlessAsked()
    {
        var subagent = Session("sub", [You("gamma inside a subagent")], Day, subagent: true);

        Assert.Empty(HistorySearch.Run([subagent], "gamma"));
        Assert.Single(HistorySearch.Run([subagent], "gamma", includeSubagents: true));
    }

    [Fact]
    public void TheNewestSessionComesFirst()
    {
        var older = Session("older", [You("delta once")], Day.AddDays(-9));
        var newer = Session("newer", [You("delta again")], Day);

        var results = HistorySearch.Run([older, newer], "delta");

        Assert.Equal(["newer", "older"], results.Select(r => r.Session.SessionId));
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        var session = Session("a", [You("Epsilon in capitals")], Day);

        Assert.Single(HistorySearch.Run([session], "EPSILON"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("ab", false)]
    [InlineData("ui bug", false)]
    [InlineData("C#", false)]
    [InlineData("abc", true)]
    [InlineData("abc def", true)]
    [InlineData("A2A", true)]
    public void EveryWordNeedsThreeLetters(string query, bool usable) =>
        Assert.Equal(usable, HistorySearch.IsUsable(query));

    [Fact]
    public void AWindowsPathIsFound()
    {
        // The file holds the path with each backslash doubled, so the raw line does not contain it as typed.
        var session = Session("a", [You(@"the code is in C:\Git\OpenTail.Bullshark\src\Core")], Day);

        var match = Assert.Single(HistorySearch.Run([session], @"OpenTail.Bullshark\src"));

        Assert.Equal(1, match.MatchingMessages);
    }

    [Fact]
    public void AQuotedPhraseIsFound()
    {
        var session = Session("a", [You("he said \"hello there\" to the team")], Day);

        Assert.Single(HistorySearch.Run([session], "\"hello there\""));
    }

    [Fact]
    public void AWordWithAQuoteOrBackslashIsNotPreFiltered()
    {
        Assert.False(HistorySearch.SameInRawLine(@"src\Core"));
        Assert.False(HistorySearch.SameInRawLine("\"hello"));
        Assert.False(HistorySearch.SameInRawLine("don't"));
    }

    [Fact]
    public void APlainWordKeepsTheFastPath()
    {
        Assert.True(HistorySearch.SameInRawLine("alpha"));
        Assert.True(HistorySearch.SameInRawLine("A2A"));
        Assert.True(HistorySearch.SameInRawLine("file_name.cs"));
    }

    [Fact]
    public void ASnippetShowsTheWordWithItsContext()
    {
        var text = new string('x', 100) + " target " + new string('y', 100);

        var snippet = HistorySearch.Snippet(text, "target");

        Assert.Contains("target", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.True(snippet.Length < 140, $"snippet is {snippet.Length} characters");
    }

    [Fact]
    public void ResultsAreCapped()
    {
        var sessions = Enumerable.Range(0, HistorySearch.MaxResults + 5)
            .Select(i => Session($"s{i:000}", [You("omega here")], Day.AddMinutes(-i)))
            .ToList();

        Assert.Equal(HistorySearch.MaxResults, HistorySearch.Run(sessions, "omega").Count);
    }

    [Fact]
    public void TheNewestMatchingMessagesAreKept()
    {
        var session = Session("a", [You("theta 1"), You("theta 2"), You("theta 3"), You("theta 4"), You("theta 5")], Day);

        var match = Assert.Single(HistorySearch.Run([session], "theta"));

        Assert.Equal(5, match.MatchingMessages);
        Assert.Equal([3, 4, 5], match.Hits.Select(h => h.LineNumber));
    }

    [Fact]
    public void ALineNumberIsTheNumberTheTranscriptShows()
    {
        // The first record is not shown in the transcript, so the matching message is on line 2.
        var session = Session("a", ["{\"type\":\"system\",\"subtype\":\"other\"}", You("kappa on line two")], Day);

        var hit = Assert.Single(HistorySearch.Run([session], "kappa")).Hits.Single();

        Assert.Equal(2, hit.LineNumber);
        var page = TranscriptPager.Open(session.FilePath).ReadPage(hit.LineNumber - 1, 1);
        Assert.Contains("kappa", page.Single().Text);
    }

    [Fact]
    public void ATranscriptThatCannotBeReadIsLeftOut()
    {
        var missing = new SessionDigest("gone", Path.Combine(_dir, "missing.jsonl"), "C--Git", false, null, Day, 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);

        Assert.Empty(HistorySearch.Run([missing], "anything"));
    }

    [Fact]
    public void ACancelledSearchStops()
    {
        var session = Session("a", [You("lambda here")], Day);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => HistorySearch.Run([session], "lambda", cancellationToken: cancel.Token));
    }

    [Fact]
    public void ASessionThatFailsUnexpectedlyIsLeftOutAndCountedAndTheSearchGoesOn()
    {
        var good = Session("good", [You("sigma here")], Day);
        var bad = Session("bad", [You("sigma too")], Day.AddMinutes(-1));

        var outcome = HistorySearch.RunCounted([bad, good], "sigma",
            extraText: digest => digest.SessionId == "bad" ? throw new InvalidOperationException("unexpected") : null);

        Assert.Equal(["good"], outcome.Matches.Select(m => m.Session.SessionId));
        Assert.Equal(1, outcome.Unreadable);
    }

    [Fact]
    public void AMissingTranscriptIsCounted()
    {
        var good = Session("good", [You("tau here")], Day);
        var missing = new SessionDigest("gone", Path.Combine(_dir, "missing.jsonl"), "C--Git", false, null, Day.AddMinutes(-1), 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);

        var outcome = HistorySearch.RunCounted([missing, good], "tau");

        Assert.Single(outcome.Matches);
        Assert.Equal(1, outcome.Unreadable);
    }
}

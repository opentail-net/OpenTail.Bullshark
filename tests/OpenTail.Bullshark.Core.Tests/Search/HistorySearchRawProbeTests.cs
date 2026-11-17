using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Search;

/// <summary>
/// A word with an escaped character (a backslash, a quote) is pre-filtered by its longest run of plain characters (round 10 item 2).
/// The pre-filter must never rule out a line that holds the word, so the search gives the same matches as the rule before it, which parsed
/// every line of such a word.
/// </summary>
public sealed class HistorySearchRawProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-probe-" + Guid.NewGuid().ToString("N")[..8]);

    public HistorySearchRawProbeTests() => Directory.CreateDirectory(_dir);

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

    private SessionDigest Session(string id, string[] lines, DateTimeOffset last)
    {
        var path = Path.Combine(_dir, id + ".jsonl");
        File.WriteAllLines(path, lines);
        return new SessionDigest(id, path, "C--Git", false, null, last, 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);
    }

    private static readonly DateTimeOffset Day = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("alpha", "alpha")]
    [InlineData(@"C:\Windows\System32", "System32")]
    [InlineData(@"src\Core", "Core")]
    [InlineData("\"hello", "hello")]
    [InlineData("don't", "don")]
    [InlineData("a-b_c.d", "a-b_c.d")]
    [InlineData("::", "")]
    [InlineData(@"\\", "")]
    public void TheProbeIsTheLongestPlainRun(string word, string probe) =>
        Assert.Equal(probe, HistorySearch.RawProbe(word));

    [Fact]
    public void AWindowsPathIsFoundByItsMessagesOnly()
    {
        var session = Session("a",
        [
            You(@"the code is in C:\Windows\System32 now"),
            Claude(@"see C:\Windows\System32\drivers too"),
            You("Windows and System32 with no backslash"),
            ToolResult(@"C:\Windows\System32 in a tool result"),
        ], Day);
        var other = Session("b", [You("Windows and System32 only")], Day.AddMinutes(-1));

        var match = Assert.Single(HistorySearch.Run([session, other], @"C:\Windows\System32"));

        Assert.Equal("a", match.Session.SessionId);
        Assert.Equal(2, match.MatchingMessages);
        Assert.Equal([1, 2], match.Hits.Select(h => h.LineNumber));
    }

    [Fact]
    public void TheSearchGivesWhatTheParseEveryLineRuleGives()
    {
        var sessions = new[]
        {
            Session("s1", [
                You(@"the code is in C:\Windows\System32 now"),
                Claude("alpha is here"),
                ToolResult(@"result with C:\Windows\System32"),
                You("don't stop \"hello there\" friend"),
            ], Day),
            Session("s2", [
                You(@"path C:\Git\OpenTail.Bullshark\src\Core and alpha"),
                Claude("ok"),
            ], Day.AddMinutes(-1)),
            Session("s3", [
                You("Windows and System32 but no backslash"),
                Claude(@"back\slash alone"),
            ], Day.AddMinutes(-2)),
            Session("s4", [
                You(@"C:\Windows\System32\drivers"),
                You("ALPHA beta"),
                Claude("line with a tab\tand a newline\nsecond line"),
            ], Day.AddMinutes(-3)),
            Session("s5", [
                You("unicode caf\u00e9 and an emoji \U0001F600 alpha"),
                Claude("quote \" and backslash \\ end"),
            ], Day.AddMinutes(-4)),
            Session("s6", [], Day.AddMinutes(-5)),
        };
        var queries = new[]
        {
            @"C:\Windows\System32", @"src\Core", "\"hello there\"", "don't", @"OpenTail.Bullshark\src", "alpha",
            "ALPHA", @"alpha C:\Windows", "<b>", "backslash \\", "caf\u00e9", "tab\tand",
        };

        foreach (var query in queries)
        {
            var words = HistorySearch.WordsOf(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var expected = sessions.Select(s => Reference(s, words)).Where(m => m is not null).Select(m => m!).ToList();
            var actual = HistorySearch.Run(sessions, query);

            Assert.Equal(expected.Select(m => m.Session.SessionId), actual.Select(m => m.Session.SessionId));
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].MatchingMessages, actual[i].MatchingMessages);
                Assert.Equal(expected[i].Hits, actual[i].Hits);
            }
        }
    }

    /// <summary>The rule before round 10 item 2: every line is parsed, and a message counts when its text holds a word.</summary>
    private static SessionMatch? Reference(SessionDigest digest, IReadOnlyList<string> words)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<LineHit>();
        var matching = 0;
        var number = 0;
        foreach (var raw in File.ReadLines(digest.FilePath))
        {
            number++;
            var shown = TranscriptLine.From(number, raw);
            if (shown is null || shown.Kind is not ("you" or "claude")) continue;
            var inThisMessage = words.Where(word => shown.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inThisMessage.Count == 0) continue;
            matching++;
            foreach (var word in inThisMessage) found.Add(word);
            if (hits.Count == HistorySearch.HitsPerSession) hits.RemoveAt(0);
            hits.Add(new LineHit(number, shown.Kind, shown.Time, HistorySearch.Snippet(shown.Text, inThisMessage[0])));
        }
        return found.Count == words.Count ? new SessionMatch(digest, matching, hits) : null;
    }
}

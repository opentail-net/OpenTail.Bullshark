using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Search;

/// <summary>D-L7: a word of the query may be found in the owner's name, tags or note, not only in the messages.</summary>
public sealed class HistorySearchExtraTextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-search-extra-" + Guid.NewGuid().ToString("N")[..8]);

    public HistorySearchExtraTextTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string You(string text) =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-16T10:00:00Z\",\"message\":{\"role\":\"user\",\"content\":" +
        JsonSerializer.Serialize(text) + "}}";

    private SessionDigest Session(string id, string[] lines, DateTimeOffset last)
    {
        var path = Path.Combine(_dir, id + ".jsonl");
        File.WriteAllLines(path, lines);
        return new SessionDigest(id, path, "C--Git", false, null, last, 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);
    }

    private static readonly DateTimeOffset Day = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AWordInTheOwnersNameMatches()
    {
        var named = Session("a", [You("nothing relevant here")], Day);

        var match = Assert.Single(HistorySearch.Run([named], "alpha", extraText: _ => "Alpha plan"));

        Assert.Equal(0, match.MatchingMessages);
    }

    [Fact]
    public void AWordInATagMatchesWithNoMatchingMessage()
    {
        var tagged = Session("b", [You("nothing relevant here")], Day);

        var match = Assert.Single(HistorySearch.Run([tagged], "review", extraText: _ => "plan review"));

        Assert.Equal(0, match.MatchingMessages);
    }

    [Fact]
    public void EveryWordIsStillNeeded()
    {
        var named = Session("c", [You("nothing relevant here")], Day);

        // "alpha" is in the name, "missing" is nowhere: the session does not match.
        Assert.Empty(HistorySearch.Run([named], "alpha missing", extraText: _ => "Alpha plan"));
    }
}

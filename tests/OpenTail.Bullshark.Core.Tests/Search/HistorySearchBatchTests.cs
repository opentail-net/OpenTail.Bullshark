using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Search;

/// <summary>
/// The search reads the sessions a batch at a time, on several threads (round 10 item 2). The count of unreadable sessions and the stop at
/// <see cref="HistorySearch.MaxResults"/> must be those of reading them one by one: a session after the stop is not read, so not counted.
/// </summary>
public sealed class HistorySearchBatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-batch-" + Guid.NewGuid().ToString("N")[..8]);

    public HistorySearchBatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly DateTimeOffset Day = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Session i is newer than session i + 1. A session that is not readable has no transcript at its path.</summary>
    private SessionDigest Session(int i, bool readable)
    {
        var id = "s" + i.ToString("D3");
        var path = Path.Combine(_dir, id + ".jsonl");
        if (readable)
        {
            var line = "{\"type\":\"user\",\"timestamp\":\"2026-11-16T10:00:00Z\",\"message\":{\"role\":\"user\",\"content\":" +
                       JsonSerializer.Serialize("gamma in session " + i) + "}}";
            File.WriteAllLines(path, [line]);
        }
        return new SessionDigest(id, path, "C--Git", false, null, Day.AddMinutes(-i), 0, 0, 0, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero);
    }

    [Fact]
    public void TheStopAtTheCapLeavesLaterUnreadableSessionsUncounted()
    {
        // 150 sessions, all matching. Session 10 cannot be read. The 100th match is session 100 (session 10 is not a match), so the search
        // stops there: it counts session 10 and never reaches session 120, which cannot be read either.
        var sessions = Enumerable.Range(0, 150).Select(i => Session(i, readable: i is not (10 or 120))).ToList();

        var outcome = HistorySearch.RunCounted(sessions, "gamma");

        Assert.Equal(HistorySearch.MaxResults, outcome.Matches.Count);
        Assert.Equal("s100", outcome.Matches[^1].Session.SessionId);
        Assert.Equal(1, outcome.Unreadable);
    }

    [Fact]
    public void EveryUnreadableSessionIsCountedWhenTheSearchReadsAllOfThem()
    {
        // 40 sessions, fewer than the cap, so every one is read: sessions 5 and 37 cannot be read, and the other 38 match.
        var sessions = Enumerable.Range(0, 40).Select(i => Session(i, readable: i is not (5 or 37))).ToList();

        var outcome = HistorySearch.RunCounted(sessions, "gamma");

        Assert.Equal(38, outcome.Matches.Count);
        Assert.Equal(2, outcome.Unreadable);
        Assert.Equal(Enumerable.Range(0, 40).Where(i => i is not (5 or 37)).Select(i => "s" + i.ToString("D3")),
            outcome.Matches.Select(m => m.Session.SessionId));
    }
}

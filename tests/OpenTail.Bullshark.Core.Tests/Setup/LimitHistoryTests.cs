using System.Globalization;
using System.Text;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The limit history (A4): one hit per limit, the days, the tokens in the five hours before a hit (each message once), and the window of days.
/// Every test writes its transcripts into a temp folder that it removes.
/// </summary>
public sealed class LimitHistoryTests : IDisposable
{
    /// <summary>Noon on 18 November 2026 (UTC): the day of the Limits page.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-history-" + Guid.NewGuid().ToString("N")[..8]);

    public LimitHistoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A limit entry in the shape of the owner's transcript, written at <paramref name="written"/>, resetting at <paramref name="resetsAt"/>.</summary>
    private static string Limit(DateTimeOffset written, DateTimeOffset resetsAt, string type = "five_hour", string status = "rejected") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(written) + "\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"" + status + "\",\"resetsAt\":" + resetsAt.ToUnixTimeSeconds() +
        ",\"rateLimitType\":\"" + type + "\"}},\"message\":{\"model\":\"<synthetic>\",\"content\":[]}}";

    /// <summary>An assistant message with usage: its id and request id make its key, and <paramref name="output"/> is its output tokens.</summary>
    private static string Usage(DateTimeOffset at, string id, long output, string requestId = "req_1") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(at) + "\",\"requestId\":\"" + requestId + "\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-test\",\"usage\":{\"input_tokens\":0,\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    /// <summary>Writes a transcript under the temp folder, creating its folders, and returns its path.</summary>
    private string Write(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void TheSameLimitWrittenSeveralTimesIsOneHitAtItsEarliestEntry()
    {
        var reset = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Limit(new DateTimeOffset(2026, 11, 18, 10, 5, 0, TimeSpan.Zero), reset)]);
        Write("project-b/session.jsonl", [Limit(new DateTimeOffset(2026, 11, 18, 10, 6, 0, TimeSpan.Zero), reset)]);
        Write("project-b/session-2.jsonl", [Limit(new DateTimeOffset(2026, 11, 18, 10, 7, 0, TimeSpan.Zero), reset)]);

        var (hits, days) = LimitHistory.Read(_root, Now, zone: Utc);

        var hit = Assert.Single(hits);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 5, 0, TimeSpan.Zero), hit.Time);
        Assert.Equal(reset, hit.ResetsAt);
        Assert.Equal("five_hour", hit.Type);
        Assert.Equal(1, Assert.Single(days).Total);
    }

    [Fact]
    public void TwoTypesOnOneDayAreTwoHitsAndOneDayRow()
    {
        Write("project-a/session.jsonl", [Limit(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 18, 13, 0, 0, TimeSpan.Zero))]);
        Write("project-b/session.jsonl", [Limit(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero), type: "weekly")]);

        var (hits, days) = LimitHistory.Read(_root, Now, zone: Utc);

        Assert.Equal(2, hits.Count);
        // Newest first.
        Assert.Equal("weekly", hits[0].Type);
        var day = Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 11, 18), day.Day);
        Assert.Equal(1, day.HitsByType["five_hour"]);
        Assert.Equal(1, day.HitsByType["weekly"]);
        Assert.Equal(2, day.Total);
    }

    [Fact]
    public void AHitOutsideTheDaysIsLeftOut()
    {
        Write("old/session.jsonl", [Limit(Now.AddDays(-40), Now.AddDays(-40).AddHours(3))]);
        Write("recent/session.jsonl", [Limit(Now.AddDays(-2), Now.AddDays(-2).AddHours(3))]);

        var (hits, _) = LimitHistory.Read(_root, Now, zone: Utc);

        Assert.Equal(Now.AddDays(-2), Assert.Single(hits).Time);
    }

    [Fact]
    public void TokensBeforeCountOnlyTheFiveHoursUpToTheHitAndAMessageOnce()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl",
        [
            Usage(hit.AddHours(-5).AddMinutes(-30), "msg_before", 1000),  // before the window: not counted
            Usage(hit.AddHours(-5), "msg_edge", 100),                      // the window's first moment: counted
            Usage(hit.AddHours(-2), "msg_mid", 200),
            Usage(hit, "msg_at", 300),                                     // the hit's own moment: counted
            Usage(hit.AddMinutes(30), "msg_after", 400),                   // after the hit: not counted
            Limit(hit, hit.AddHours(3)),
        ]);
        // The same message in a second transcript (a subagent's copy) and again in a second record: it counts once, at its largest values.
        Write("project-a/subagents/agent.jsonl",
        [
            Usage(hit.AddHours(-2), "msg_mid", 250),
            Usage(hit.AddHours(-2).AddSeconds(1), "msg_mid", 240),
        ]);

        var (hits, _) = LimitHistory.Read(_root, Now, zone: Utc);

        var counted = Assert.Single(hits);
        // 100 + 250 (msg_mid, the larger value) + 300. Not 1000, 400, and not msg_mid twice.
        Assert.Equal(650, counted.TokensBefore.Output);
    }

    [Fact]
    public void GarbageLinesAreSkippedOneByOneAndTheValidOnesAreStillRead()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var loneSurrogate = "{\"apiError\":\"usage_limit_\\ud800\",\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":1794834600}}}";
        var brokenUsage = "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(hit) + "\",\"requestId\":\"req_1\",\"message\":{\"id\":\"msg_bad\",\"model\":\"\\ud800\",\"usage\":{\"output_tokens\":9}}}";
        Write("project-a/session.jsonl",
        [
            "", "not json", Limit(hit, hit.AddHours(3))[..60], "[1,2]", loneSurrogate, brokenUsage,
            Usage(hit.AddHours(-1), "msg_ok", 500),
            Limit(hit, hit.AddHours(3)),
        ]);

        var (hits, _) = LimitHistory.Read(_root, Now, zone: Utc);

        var counted = Assert.Single(hits);
        Assert.Equal(500, counted.TokensBefore.Output);
    }

    [Fact]
    public void ALockedTranscriptIsSkippedAndTheOthersAreRead()
    {
        // The exclusive lock makes the file unreadable here on Windows, which is where the owner's transcripts are read.
        if (!OperatingSystem.IsWindows()) return;
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var locked = Write("locked/session.jsonl", [Limit(hit.AddHours(-1), hit.AddHours(2), type: "weekly")]);
        Write("open/session.jsonl", [Limit(hit, hit.AddHours(3))]);

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var (hits, _) = LimitHistory.Read(_root, Now, zone: Utc);

        Assert.Equal(hit, Assert.Single(hits).Time);
    }

    [Fact]
    public void TheDayIsTheLocalDateOfTheHit()
    {
        // 23:30 UTC on 17 November is 01:30 on 18 November in a zone two hours ahead.
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "test-plus-two", "test-plus-two");
        Write("project-a/session.jsonl", [Limit(new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 18, 2, 0, 0, TimeSpan.Zero))]);

        var (_, inUtc) = LimitHistory.Read(_root, Now, zone: Utc);
        var (_, inPlusTwo) = LimitHistory.Read(_root, Now, zone: plusTwo);

        Assert.Equal(new DateOnly(2026, 11, 17), Assert.Single(inUtc).Day);
        Assert.Equal(new DateOnly(2026, 11, 18), Assert.Single(inPlusTwo).Day);
    }

    [Fact]
    public void AMissingFolderGivesNoHitsAndNoDays()
    {
        var (hits, days) = LimitHistory.Read(Path.Combine(_root, "does-not-exist"), Now, zone: Utc);

        Assert.Empty(hits);
        Assert.Empty(days);
    }
}

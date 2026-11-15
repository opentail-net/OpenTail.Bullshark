using OpenTail.Bullshark.Core.Cache;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Cache;

public sealed class CacheRuleTests
{
    private static readonly DateTimeOffset Last = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(49.99, CacheState.Warm)]
    [InlineData(50.0, CacheState.Cooling)]
    [InlineData(59.99, CacheState.Cooling)]
    [InlineData(60.0, CacheState.Cold)]
    public void OneHourLimitsAreFiftyAndSixtyMinutes(double idleMinutes, CacheState expected)
    {
        var verdict = CacheRule.Classify(Last, Last.AddMinutes(idleMinutes), CacheTtl.OneHour, 1000);

        Assert.Equal(expected, verdict.State);
    }

    [Theory]
    [InlineData(3.99, CacheState.Warm)]
    [InlineData(4.0, CacheState.Cooling)]
    [InlineData(4.99, CacheState.Cooling)]
    [InlineData(5.0, CacheState.Cold)]
    public void FiveMinuteLimitsAreFourAndFiveMinutes(double idleMinutes, CacheState expected)
    {
        var verdict = CacheRule.Classify(Last, Last.AddMinutes(idleMinutes), CacheTtl.FiveMinutes, 1000);

        Assert.Equal(expected, verdict.State);
    }

    [Fact]
    public void NoRequestMeansUnknown()
    {
        Assert.Equal(CacheState.Unknown, CacheRule.Classify(null, Last, CacheTtl.OneHour, 0).State);
    }

    [Fact]
    public void LabelsGiveTokensOnlyAndSayWhenTheLifetimeIsAssumed()
    {
        var warm = CacheRule.Label(CacheRule.Classify(Last, Last.AddMinutes(10), CacheTtl.OneHour, 180_000));
        var assumed = CacheRule.Label(CacheRule.Classify(Last, Last.AddMinutes(70), CacheTtl.Unknown, 5_000));

        Assert.Contains("180,000 cached tokens", warm);
        Assert.DoesNotContain("$", warm);
        Assert.Contains("one hour assumed", assumed);
        Assert.Contains("rewrites about 5,000 tokens", assumed);
    }
}

public sealed class TranscriptTailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-tail-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptTailTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Assistant(DateTimeOffset time, long input, long read, long write, long write1h, long write5m) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + time.ToString("O") + "\",\"message\":{\"usage\":{" +
        $"\"input_tokens\":{input},\"cache_read_input_tokens\":{read},\"cache_creation_input_tokens\":{write}," +
        $"\"cache_creation\":{{\"ephemeral_1h_input_tokens\":{write1h},\"ephemeral_5m_input_tokens\":{write5m}}}}}}}}}";

    private static string User(DateTimeOffset time, string text) =>
        "{\"type\":\"user\",\"timestamp\":\"" + time.ToString("O") + "\",\"message\":{\"content\":\"" + text + "\"}}";

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheLastRequestAndItsContextComeFromTheEnd()
    {
        var path = Write("small.jsonl",
            User(T0, "hello"),
            Assistant(T0.AddMinutes(1), input: 10, read: 0, write: 5000, write1h: 5000, write5m: 0),
            Assistant(T0.AddMinutes(20), input: 12, read: 5000, write: 30, write1h: 30, write5m: 0));

        var facts = TranscriptTail.Read(path);

        Assert.NotNull(facts);
        Assert.Equal(T0.AddMinutes(20), facts!.LastRequest);
        Assert.Equal(CacheTtl.OneHour, facts.Ttl);
        Assert.Equal(12 + 5000 + 30, facts.ContextTokens);
    }

    [Fact]
    public void AFiveMinuteWriteGivesAFiveMinuteLifetime()
    {
        var path = Write("five.jsonl",
            Assistant(T0, input: 5, read: 0, write: 800, write1h: 0, write5m: 800),
            Assistant(T0.AddMinutes(2), input: 5, read: 800, write: 0, write1h: 0, write5m: 0));

        var facts = TranscriptTail.Read(path);

        Assert.Equal(CacheTtl.FiveMinutes, facts!.Ttl);
    }

    [Fact]
    public void AHugeLastLineDoesNotHideTheUsageRecord()
    {
        // The usage record sits before one line larger than the first 64 KB window.
        var huge = "{\"type\":\"user\",\"timestamp\":\"" + T0.ToString("O") + "\",\"message\":{\"content\":\"" + new string('x', 200_000) + "\"}}";
        var path = Write("huge.jsonl", Assistant(T0, input: 7, read: 100, write: 900, write1h: 900, write5m: 0), huge);

        var facts = TranscriptTail.Read(path);

        Assert.NotNull(facts);
        Assert.Equal(T0, facts!.LastRequest);
        Assert.Equal(7 + 100 + 900, facts.ContextTokens);
    }

    [Fact]
    public void ALastLineThatDoesNotParseYetIsSkipped()
    {
        var path = Path.Combine(_dir, "partial.jsonl");
        File.WriteAllText(path, Assistant(T0, input: 3, read: 0, write: 400, write1h: 400, write5m: 0) + "\n{\"type\":\"assist");

        var facts = TranscriptTail.Read(path);

        Assert.Equal(T0, facts!.LastRequest);
        Assert.Equal(CacheTtl.OneHour, facts.Ttl);
    }

    [Fact]
    public void AFileWithoutUsageGivesNothing()
    {
        var path = Write("none.jsonl", User(T0, "one"), User(T0.AddMinutes(1), "two"));

        Assert.Null(TranscriptTail.Read(path));
    }

    [Fact]
    public void AFileOpenedForWritingElsewhereCanStillBeRead()
    {
        var path = Write("open.jsonl", Assistant(T0, input: 1, read: 0, write: 200, write1h: 200, write5m: 0));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.NotNull(TranscriptTail.Read(path));
    }
}

public sealed class CacheReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-report-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    public CacheReportTests() => Directory.CreateDirectory(Path.Combine(_root, "proj"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Request(string id, DateTimeOffset time, long input, long read, long write, long write1h, long write5m) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + time.ToString("O") + "\",\"requestId\":\"req-" + id + "\",\"message\":{\"id\":\"msg-" + id +
        "\",\"usage\":{" +
        $"\"input_tokens\":{input},\"cache_read_input_tokens\":{read},\"cache_creation_input_tokens\":{write}," +
        $"\"cache_creation\":{{\"ephemeral_1h_input_tokens\":{write1h},\"ephemeral_5m_input_tokens\":{write5m}}}}}}}}}";

    [Fact]
    public void PairsAreBucketedByGapAndCountedOncePerMessage()
    {
        // One hour lifetime: r2 comes 10 minutes after r1 (warm, reads), r3 comes 70 minutes later (cold, rewrites).
        File.WriteAllText(Path.Combine(_root, "proj", "s1.jsonl"), string.Join("\n",
            Request("1", T0, 10, 0, 1000, 1000, 0),
            Request("2", T0.AddMinutes(10), 10, 1000, 0, 0, 0),
            Request("3", T0.AddMinutes(80), 10, 0, 1000, 1000, 0)) + "\n");
        // The same message copied into a second file (a resumed session) must not count again.
        File.WriteAllText(Path.Combine(_root, "proj", "s2.jsonl"), Request("2", T0.AddMinutes(10), 10, 1000, 0, 0, 0) + "\n");

        var report = CacheReport.Compute(_root);

        Assert.Equal(3, report.CountedRequests);
        Assert.Equal(2, report.Pairs);
        var warm = report.Rows.Single(r => r.Lifetime == "1h" && r.Bucket == "warm");
        Assert.Equal(1, warm.Pairs);
        Assert.Equal(0.0, warm.RewriteShare);
        Assert.Equal(1.0, warm.ReadShare);
        var cold = report.Rows.Single(r => r.Lifetime == "1h" && r.Bucket == "cold");
        Assert.Equal(1, cold.Pairs);
        Assert.Equal(1.0, cold.RewriteShare);
    }

    [Fact]
    public void AFiveMinuteSessionIsBucketedWithFiveMinuteLimits()
    {
        // Five-minute lifetime: 3 minutes later is warm, 6 minutes later is cold.
        File.WriteAllText(Path.Combine(_root, "proj", "s3.jsonl"), string.Join("\n",
            Request("a", T0, 5, 0, 800, 0, 800),
            Request("b", T0.AddMinutes(3), 5, 800, 0, 0, 0),
            Request("c", T0.AddMinutes(9), 5, 0, 800, 0, 800)) + "\n");

        var report = CacheReport.Compute(_root);

        Assert.Equal(1, report.SessionsFiveMinutes);
        Assert.Equal(1, report.Rows.Single(r => r.Lifetime == "5m" && r.Bucket == "warm").Pairs);
        Assert.Equal(1.0, report.Rows.Single(r => r.Lifetime == "5m" && r.Bucket == "cold").RewriteShare);
    }

    [Fact]
    public void AMissingFolderGivesAnEmptyReport()
    {
        var report = CacheReport.Compute(Path.Combine(_root, "missing"));

        Assert.Equal(0, report.Files);
        Assert.Empty(report.Rows);
    }
}

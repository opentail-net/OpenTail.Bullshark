using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The limit history cache (perf plan 2026-11-18): a cached read gives the uncached read's result; an unchanged transcript is taken from the cache;
/// a changed one is read again; a corrupt, foreign or unwritable cache is no cache and never throws; the cache holds no message text.
/// Every test writes its transcripts and its cache into a temp folder that it removes.
/// </summary>
public sealed class LimitHistoryCacheTests : IDisposable
{
    /// <summary>Noon on 18 November 2026 (UTC): the day of the Limits page.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-history-cache-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _projects;
    private readonly string _cache;
    private readonly List<string> _logged = [];

    public LimitHistoryCacheTests()
    {
        _projects = Path.Combine(_root, "projects");
        _cache = Path.Combine(_root, "cache", "limit-history.json");
        Directory.CreateDirectory(_projects);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A limit entry in the shape of the owner's transcript, written at <paramref name="written"/>, resetting at <paramref name="resetsAt"/>.</summary>
    private static string Limit(DateTimeOffset written, DateTimeOffset resetsAt, string type = "five_hour", string status = "rejected") =>
        LimitRaw(Stamp(written), resetsAt.ToUnixTimeSeconds(), type, status);

    /// <summary>A limit entry with its timestamp text as written (an offset may be given).</summary>
    private static string LimitRaw(string stamp, long resetsAtSeconds, string type = "five_hour", string status = "rejected") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"" + status + "\",\"resetsAt\":" + resetsAtSeconds +
        ",\"rateLimitType\":\"" + type + "\"}},\"message\":{\"model\":\"<synthetic>\",\"content\":[]}}";

    /// <summary>An assistant message with usage: its id and request id make its key, and <paramref name="output"/> is its output tokens.</summary>
    private static string Usage(DateTimeOffset at, string id, long output, string requestId = "req_1") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(at) + "\",\"requestId\":\"" + requestId + "\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-test\",\"usage\":{\"input_tokens\":0,\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    /// <summary>Writes a transcript under the projects folder, creating its folders, and returns its path.</summary>
    private string Write(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_projects, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    private void Log(string note) => _logged.Add(note);

    /// <summary>The whole result as text: every hit and day, with the offsets its times were written with. Equal texts mean equal results.</summary>
    private static string Canonical((IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) result) => string.Join("\n",
        result.Hits.Select(hit => $"hit {hit.Time:O} {hit.Type} {hit.ResetsAt:O} {hit.TokensBefore}")
            .Concat(result.Days.Select(day => $"day {day.Day} " +
                string.Join(",", day.HitsByType.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")))));

    private (IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) Uncached() => LimitHistory.Read(_projects, Now, zone: Utc);

    private (IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) Cached() =>
        LimitHistory.ReadWithCache(_projects, Now, _cache, zone: Utc, log: Log);

    [Fact]
    public void ACachedReadGivesTheSameResultAsAnUncachedReadColdAndWarm()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Usage(hit.AddHours(-4), "msg_a", 100), Limit(hit, hit.AddHours(3))]);
        Write("project-a/subagents/agent.jsonl", [Usage(hit.AddHours(-2), "msg_a", 250), Usage(hit.AddHours(-1), "msg_b", 300)]);
        // A later weekly limit whose time the transcript writes with a +02:00 offset and milliseconds: the cache must give back that offset.
        Write("project-b/session.jsonl",
        [
            LimitRaw("2026-11-18T13:00:00.123+02:00", hit.AddDays(3).ToUnixTimeSeconds(), type: "weekly"),
            Usage(hit.AddHours(1), "msg_c", 40),
        ]);

        var expected = Canonical(Uncached());
        var cold = Cached();
        var warm = Cached();

        Assert.Equal(expected, Canonical(cold));
        Assert.Equal(expected, Canonical(warm));
        Assert.True(File.Exists(_cache));
        Assert.Empty(_logged);
        Assert.Equal(TimeSpan.FromHours(2), warm.Hits[0].Time.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 11, 0, 0, 123, TimeSpan.Zero), warm.Hits[0].Time);
    }

    [Fact]
    public void AnUnchangedTranscriptIsTakenFromTheCacheAndNotRead()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var path = Write("project-a/session.jsonl", [Limit(hit, hit.AddHours(3))]);
        var written = File.GetLastWriteTimeUtc(path);
        Assert.Single(Cached().Hits);

        // The same length and the same write time, but other text: a read of the file would find no rejected limit. The cache is used instead.
        var text = File.ReadAllText(path);
        var changed = text.Replace("\"rejected\"", "\"rejectex\"", StringComparison.Ordinal);
        Assert.Equal(text.Length, changed.Length);
        File.WriteAllText(path, changed, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, written);

        Assert.Single(Cached().Hits);
        Assert.Empty(Uncached().Hits);
    }

    [Fact]
    public void ATranscriptThatGrewIsReadAgain()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var path = Write("project-a/session.jsonl", [Limit(hit, hit.AddHours(3))]);
        Assert.Single(Cached().Hits);

        // A second limit is written later: the length changes, so the entry does not match and the file is read.
        File.AppendAllText(path, Limit(hit.AddHours(1), hit.AddHours(4), type: "weekly") + "\n", new UTF8Encoding(false));

        var after = Cached();
        Assert.Equal(2, after.Hits.Count);
        Assert.Equal(Canonical(Uncached()), Canonical(after));
    }

    [Fact]
    public void ATranscriptChangedWithTheSameLengthIsReadAgain()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var path = Write("project-a/session.jsonl", [Limit(hit, hit.AddHours(3))]);
        Assert.Single(Cached().Hits);

        // The same length, but a new write time (the file was written again): the entry does not match, and the new text is read.
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text.Replace("\"rejected\"", "\"rejectex\"", StringComparison.Ordinal), new UTF8Encoding(false));
        // Two writes inside one file-system clock tick can share a write time (this test failed once in a full run), so the new time is set.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));

        Assert.Empty(Cached().Hits);
        Assert.Empty(Uncached().Hits);
    }

    [Fact]
    public void ACorruptCacheIsIgnoredAndRebuilt()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Usage(hit.AddHours(-1), "msg_a", 500), Limit(hit, hit.AddHours(3))]);
        Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);
        File.WriteAllText(_cache, "{\"reader\":\"not json at all");

        var result = Cached();

        Assert.Equal(Canonical(Uncached()), Canonical(result));
        Assert.Contains(_logged, note => note.Contains("not used", StringComparison.Ordinal));
        // The cache is rebuilt: it parses now and names this build.
        using var rebuilt = JsonDocument.Parse(File.ReadAllText(_cache));
        Assert.True(rebuilt.RootElement.TryGetProperty("reader", out _));
    }

    [Fact]
    public void ACacheFromAnotherBuildIsNotUsed()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Limit(hit, hit.AddHours(3))]);
        Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);
        File.WriteAllText(_cache, "{\"reader\":\"00000000000000000000000000000000\",\"files\":[]}");

        var result = Cached();

        Assert.Equal(Canonical(Uncached()), Canonical(result));
        Assert.Empty(_logged);
    }

    [Fact]
    public void ACacheThatCannotBeWrittenNeverThrowsAndSaysSo()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Limit(hit, hit.AddHours(3))]);
        // The cache path is a folder, so the save cannot take its place.
        Directory.CreateDirectory(_cache);

        var result = Cached();

        Assert.Equal(Canonical(Uncached()), Canonical(result));
        Assert.Contains(_logged, note => note.Contains("not saved", StringComparison.Ordinal));
    }

    [Fact]
    public void ACacheHeldByAnotherWriterNeverThrowsAndSaysSo()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Limit(hit, hit.AddHours(3))]);
        Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);

        // Another thread holds the cache's lock for the length of the read: the save waits its short wait, then gives up.
        using var holding = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holder = new Thread(() =>
        {
            using (FileLock.Hold(_cache))
            {
                holding.Set();
                release.Wait();
            }
        });
        holder.Start();
        try
        {
            holding.Wait();
            var result = Cached();

            Assert.Equal(Canonical(Uncached()), Canonical(result));
            Assert.Contains(_logged, note => note.Contains("not saved", StringComparison.Ordinal));
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }

    [Fact]
    public void TheCacheHoldsNoMessageText()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var reply = "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(hit.AddHours(-1)) + "\",\"requestId\":\"req_1\",\"message\":{\"id\":\"msg_1\"," +
                    "\"model\":\"claude-test\",\"content\":[{\"type\":\"text\",\"text\":\"REPLY-MARKER-9c2b\"}]," +
                    "\"usage\":{\"input_tokens\":1,\"output_tokens\":2,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";
        var prompt = "{\"type\":\"user\",\"timestamp\":\"" + Stamp(hit.AddHours(-2)) + "\",\"message\":{\"role\":\"user\",\"content\":\"PROMPT-MARKER-7f3a\"}}";
        Write("project-a/session.jsonl", [prompt, reply, Limit(hit, hit.AddHours(3))]);

        Cached();

        var text = File.ReadAllText(_cache);
        Assert.DoesNotContain("REPLY-MARKER-9c2b", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PROMPT-MARKER-7f3a", text, StringComparison.Ordinal);
        // The counts are kept: the message key is there, with its output tokens.
        Assert.Contains("msg_1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeletedTranscriptLeavesTheCache()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/kept-session.jsonl", [Limit(hit, hit.AddHours(3))]);
        var gone = Write("project-a/gone-session.jsonl", [Usage(hit.AddHours(-1), "msg_gone", 70)]);
        Cached();
        Assert.Contains("gone-session", File.ReadAllText(_cache), StringComparison.Ordinal);

        File.Delete(gone);

        var result = Cached();
        Assert.Equal(Canonical(Uncached()), Canonical(result));
        Assert.DoesNotContain("gone-session", File.ReadAllText(_cache), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatCannotBeReadIsNotCachedAndIsReadWhenItCan()
    {
        // The exclusive lock makes the file unreadable here on Windows, which is where the owner's transcripts are read.
        if (!OperatingSystem.IsWindows()) return;
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var locked = Write("project-a/locked-session.jsonl", [Limit(hit.AddHours(-1), hit.AddHours(2), type: "weekly")]);
        Write("project-a/open-session.jsonl", [Limit(hit, hit.AddHours(3))]);

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var during = Cached();
            Assert.Equal(hit, Assert.Single(during.Hits).Time);
            Assert.DoesNotContain("locked-session", File.ReadAllText(_cache), StringComparison.Ordinal);
        }

        var after = Cached();
        Assert.Equal(2, after.Hits.Count);
        Assert.Equal(Canonical(Uncached()), Canonical(after));
    }
}

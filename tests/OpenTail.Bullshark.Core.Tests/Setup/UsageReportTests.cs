using System.Globalization;
using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The usage report of the Limits page (usage by day, top projects): per local day and per project, each message once, the midnight and project rules,
/// the "Other" row, the window, and that the report and the limit hits come from one read of the same transcripts.
/// Every test writes its transcripts and its cache into a temp folder that it removes.
/// </summary>
public sealed class UsageReportTests : IDisposable
{
    /// <summary>Noon on 18 November 2026 (UTC): the day of the Limits page.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly DateOnly Today = new(2026, 11, 18);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-usage-report-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _projects;
    private readonly string _cache;
    private readonly List<string> _logged = [];

    public UsageReportTests()
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
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(written) + "\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"" + status + "\",\"resetsAt\":" + resetsAt.ToUnixTimeSeconds() +
        ",\"rateLimitType\":\"" + type + "\"}},\"message\":{\"model\":\"<synthetic>\",\"content\":[]}}";

    /// <summary>
    /// An assistant record with usage. Its message id and request id make its key, so records with the same id and request are one message. The token
    /// values are the four kinds, in the order the report names them.
    /// </summary>
    private static string Record(DateTimeOffset at, string id, long input = 0, long output = 0, long cacheCreation = 0, long cacheRead = 0,
        string requestId = "req_1") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(at) + "\",\"requestId\":\"" + requestId + "\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-test\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":" + cacheCreation + ",\"cache_read_input_tokens\":" + cacheRead + "}}}";

    /// <summary>Writes a transcript under the projects folder, creating its folders, and returns its path.</summary>
    private string Write(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_projects, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    private void Log(string note) => _logged.Add(note);

    /// <summary>
    /// The usage settings of these expectations: attribution to each message's own time (<c>event</c>), which is the rule they were written for (R8).
    /// The default (<c>lastUpdate</c>) is tested in <c>UsageAttributionTests</c>.
    /// </summary>
    private static readonly UsageSettings EventRule = new() { Attribution = UsageAttribution.Event };

    private LimitsReport Report(TimeZoneInfo? zone = null, string? cachePath = null) =>
        LimitHistory.ReadReportWithCache(_projects, Now, cachePath ?? _cache, zone: zone ?? Utc, log: Log, usage: EventRule);

    private static TokenUsage Sum(IEnumerable<TokenUsage> usages) => usages.Aggregate(TokenUsage.Zero, (sum, usage) => sum.Add(usage));

    /// <summary>The day row of a local date in a report: the window lists every day, so it is always there.</summary>
    private static UsageDay DayOf(LimitsReport report, DateOnly day) => report.Usage.Days.Single(row => row.Day == day);

    [Fact]
    public void MessagesOnTwoDaysLandOnTheirOwnDaysAndEveryDayOfTheWindowIsListedNewestFirst()
    {
        Write("project-a/session.jsonl",
        [
            Record(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), "msg_1", input: 10, output: 20, cacheCreation: 30, cacheRead: 400),
            Record(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "msg_2", output: 7, cacheRead: 3),
        ]);

        var report = Report();
        var usage = report.Usage;

        Assert.Equal(30, usage.Days.Count);
        Assert.Equal(Today, usage.Days[0].Day);
        Assert.Equal(Today.AddDays(-29), usage.Days[^1].Day);
        Assert.Equal(Today.AddDays(-29), usage.From);
        Assert.Equal(Today, usage.To);
        // The 16th: input + output + cache written is New; cache read is apart.
        var sixteenth = new DateOnly(2026, 11, 16);
        Assert.Equal(new TokenUsage(10, 20, 30, 400), DayOf(report, sixteenth).Total);
        Assert.Equal(new TokenUsage(0, 7, 0, 3), DayOf(report, Today).Total);
        // Every other day is listed with zeros and no top project.
        foreach (var row in usage.Days.Where(row => row.Day != sixteenth && row.Day != Today))
        {
            Assert.Equal(TokenUsage.Zero, row.Total);
            Assert.Null(row.TopProject);
        }
    }

    [Fact]
    public void AMessageAcrossMidnightCountsOnceOnTheEarlierDayWithTheLargerValue()
    {
        Write("project-a/session.jsonl",
        [
            Record(new DateTimeOffset(2026, 11, 17, 23, 59, 0, TimeSpan.Zero), "msg_x", input: 4, output: 10),
            Record(new DateTimeOffset(2026, 11, 18, 0, 1, 0, TimeSpan.Zero), "msg_x", input: 4, output: 50),
        ]);

        var report = Report();

        Assert.Equal(new TokenUsage(4, 50, 0, 0), DayOf(report, new DateOnly(2026, 11, 17)).Total);
        Assert.Equal(TokenUsage.Zero, DayOf(report, Today).Total);
        Assert.Equal(new TokenUsage(4, 50, 0, 0), report.Usage.Total);
    }

    [Fact]
    public void TheSameMessageInTwoFilesCountsOnceForTheFirstFileInPathOrderWithTheLargestValues()
    {
        var at = new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);
        // proj-a sorts before proj-b, so the message belongs to proj-a, whichever file the directory lists first.
        Write("proj-b/session.jsonl", [Record(at, "msg_dup", input: 2, output: 40, cacheRead: 9)]);
        Write("proj-a/session.jsonl", [Record(at, "msg_dup", input: 5, output: 30)]);

        var report = Report();

        Assert.Equal(new TokenUsage(5, 40, 0, 9), report.Usage.Total);
        var project = Assert.Single(report.Usage.Projects);
        Assert.Equal("proj-a", project.Project);
        Assert.Equal(new TokenUsage(5, 40, 0, 9), project.Total);
    }

    [Fact]
    public void TwoProjectsGiveTheirTotalsTheirActiveDaysAndTheTopProjectOfEachDay()
    {
        Write("proj-a/session.jsonl",
        [
            Record(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero), "msg_a1", input: 100, cacheRead: 1000),
            Record(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), "msg_a2", output: 300),
        ]);
        Write("proj-b/session.jsonl", [Record(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.Zero), "msg_b1", output: 500, cacheRead: 5000)]);

        var report = Report();

        // By New tokens: proj-b (500) before proj-a (400).
        Assert.Equal(["proj-b", "proj-a"], report.Usage.Projects.Select(project => project.Project));
        Assert.Equal(new TokenUsage(0, 500, 0, 5000), report.Usage.Projects[0].Total);
        Assert.Equal(1, report.Usage.Projects[0].ActiveDays);
        Assert.Equal(new TokenUsage(100, 300, 0, 1000), report.Usage.Projects[1].Total);
        Assert.Equal(2, report.Usage.Projects[1].ActiveDays);
        Assert.Equal("proj-b", DayOf(report, Today).TopProject);
        Assert.Equal("proj-a", DayOf(report, new DateOnly(2026, 11, 17)).TopProject);
        Assert.Null(DayOf(report, new DateOnly(2026, 11, 16)).TopProject);
    }

    [Fact]
    public void ASubagentTranscriptBelongsToTheProjectItIsIn()
    {
        Write("proj-c/session/subagents/agent-1.jsonl", [Record(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "msg_sub", output: 60)]);
        // A transcript written directly in the projects folder has no project folder.
        Write("loose.jsonl", [Record(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero), "msg_loose", output: 5)]);

        var report = Report();

        Assert.Equal(["proj-c", "(root)"], report.Usage.Projects.Select(project => project.Project));
        Assert.Equal(60, report.Usage.Projects[0].Total.Output);
        Assert.Equal("proj-c", DayOf(report, Today).TopProject);
    }

    [Fact]
    public void MoreThanTenProjectsGiveTenRowsAndAnOtherRowThatAddsUpToTheTotal()
    {
        // Twelve projects; project i has output (i + 1) * 100 and cache read (i + 1) * 10, so the two smallest are the Other row.
        for (var i = 0; i < 12; i++)
        {
            Write($"proj-{i:00}/session.jsonl",
                [Record(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), $"msg_{i:00}", output: (i + 1) * 100, cacheRead: (i + 1) * 10)]);
        }

        var report = Report();

        Assert.Equal(11, report.Usage.Projects.Count);
        Assert.Equal(2, report.Usage.OtherProjects);
        Assert.Equal("proj-11", report.Usage.Projects[0].Project);
        var other = report.Usage.Projects[^1];
        Assert.Equal(UsageReport.OtherName, other.Project);
        Assert.Equal(new TokenUsage(0, 300, 0, 30), other.Total);
        // Both projects in Other are active on the same single day, so the Other row counts one day, not two.
        Assert.Equal(1, other.ActiveDays);
        Assert.Equal(Sum(report.Usage.Projects.Select(project => project.Total)), report.Usage.Total);
        Assert.Equal(report.Usage.Total.NewTokens(), report.Usage.Projects.Sum(project => project.Total.NewTokens()));
        Assert.Equal(report.Usage.Total.CacheRead, report.Usage.Projects.Sum(project => project.Total.CacheRead));
    }

    [Fact]
    public void AMessageBeforeTheFirstDayOrAfterNowIsLeftOut()
    {
        Write("project-a/session.jsonl",
        [
            Record(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero), "msg_old", output: 1000),
            Record(new DateTimeOffset(2026, 11, 18, 14, 0, 0, TimeSpan.Zero), "msg_future", output: 2000),
            Record(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), "msg_first", output: 30),
        ]);

        var report = Report().Usage;

        // Oct 20 is the first day of the window; Oct 19 is not in it, and Nov 18 14:00 is after noon.
        Assert.Equal(new TokenUsage(0, 30, 0, 0), report.Total);
        Assert.Equal(new TokenUsage(0, 30, 0, 0), report.Days[^1].Total);
        Assert.Equal(new DateOnly(2026, 10, 20), report.Days[^1].Day);
        Assert.Equal(TokenUsage.Zero, report.Days[0].Total);
    }

    [Fact]
    public void TheDaysProjectsAndTotalAddUpForAMixedFixture()
    {
        Write("proj-a/session.jsonl",
        [
            Record(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero), "msg_1", input: 1, output: 2, cacheCreation: 3, cacheRead: 4),
            Record(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero), "msg_2", input: 5, output: 6, cacheCreation: 7, cacheRead: 8),
        ]);
        Write("proj-a/session/subagents/agent.jsonl", [Record(new DateTimeOffset(2026, 11, 15, 9, 5, 0, TimeSpan.Zero), "msg_3", output: 10, cacheRead: 20)]);
        Write("proj-b/session.jsonl",
        [
            Record(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "msg_4", input: 100, cacheRead: 200),
            Record(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), "msg_4", input: 90, output: 300),
        ]);

        var usage = Report().Usage;

        Assert.Equal(usage.Total, Sum(usage.Days.Select(day => day.Total)));
        Assert.Equal(usage.Total, Sum(usage.Projects.Select(project => project.Total)));
        // Input 1+5+100; output 2+6+10 and msg_4 at its larger 300; cache written 3+7; cache read 4+8+20 and msg_4 at its larger 200.
        Assert.Equal(new TokenUsage(106, 318, 10, 232), usage.Total);
    }

    [Fact]
    public void TheTokensBeforeAHitEqualTheReportTotal()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl",
        [
            Record(hit.AddHours(-4), "msg_1", input: 10, output: 20, cacheCreation: 30, cacheRead: 40),
            Record(hit.AddHours(-1), "msg_2", output: 50, cacheRead: 60),
            Limit(hit, hit.AddHours(3)),
        ]);

        var report = Report();

        var counted = Assert.Single(report.Hits);
        Assert.Equal(counted.TokensBefore, report.Usage.Total);
        Assert.Equal(new TokenUsage(10, 70, 30, 100), report.Usage.Total);
    }

    [Fact]
    public void TheReportAndTheHitsAreReadTogetherAndTheCacheIsNotRewritten()
    {
        var hit = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        Write("project-a/session.jsonl", [Record(hit.AddHours(-1), "msg_a", output: 500), Limit(hit, hit.AddHours(3))]);
        Write("project-b/session.jsonl", [Record(hit.AddHours(-2), "msg_b", output: 70, cacheRead: 5)]);

        var cold = LimitHistory.ReadReportWithCache(_projects, Now, Path.Combine(_root, "other-cache.json"), zone: Utc, usage: EventRule);
        var first = Report();
        Assert.True(File.Exists(_cache));
        Assert.Equal(Canonical(cold), Canonical(first));

        // A plain read first, then the report on the same cache: the cache is up to date, so it is not saved again.
        var past = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_cache, past);
        var (hits, days) = LimitHistory.ReadWithCache(_projects, Now, _cache, zone: Utc, log: Log);
        var warm = Report();
        Assert.Equal(past, File.GetLastWriteTimeUtc(_cache));
        Assert.Equal(Canonical(cold), Canonical(warm));
        Assert.Equal(hits.Count, warm.Hits.Count);
        Assert.Equal(days.Count, warm.Days.Count);
        Assert.Empty(_logged);
    }

    [Fact]
    public void WithNoHitTheUsageReportIsStillBuiltAndTheHitsAreEmpty()
    {
        Write("project-a/session.jsonl", [Record(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "msg_1", output: 42)]);

        var report = Report();

        Assert.Empty(report.Hits);
        Assert.Empty(report.Days);
        Assert.Equal(new TokenUsage(0, 42, 0, 0), report.Usage.Total);
        Assert.Equal("project-a", Assert.Single(report.Usage.Projects).Project);
    }

    [Fact]
    public void AGarbageLineIsSkippedAndTheValidOneIsCounted()
    {
        Write("project-a/session.jsonl",
        [
            "", "not json", "[1,2]", "{\"type\":\"assistant\",\"usage\":{\"output_tokens\":9}}",
            Record(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "msg_ok", output: 500),
        ]);

        var report = Report();

        Assert.Equal(new TokenUsage(0, 500, 0, 0), report.Usage.Total);
        Assert.Equal("project-a", Assert.Single(report.Usage.Projects).Project);
    }

    [Fact]
    public void NoUsageAtAllGivesZeroDaysAndNoProjects()
    {
        // The folder is empty: the report still lists the 30 days, with zeros and no top project.
        var report = Report();

        Assert.Empty(report.Hits);
        Assert.Equal(30, report.Usage.Days.Count);
        Assert.All(report.Usage.Days, day =>
        {
            Assert.Equal(TokenUsage.Zero, day.Total);
            Assert.Null(day.TopProject);
        });
        Assert.Empty(report.Usage.Projects);
        Assert.Equal(0, report.Usage.OtherProjects);
        Assert.Equal(TokenUsage.Zero, report.Usage.Total);
    }

    [Fact]
    public void ADayIsTheLocalDateInTheZoneGiven()
    {
        // 23:30 UTC on 17 November is 01:30 on 18 November in a zone two hours ahead.
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "test-plus-two", "test-plus-two");
        Write("project-a/session.jsonl", [Record(new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.Zero), "msg_late", output: 11)]);

        var inUtc = Report(Utc).Usage;
        var inPlusTwo = Report(plusTwo, cachePath: Path.Combine(_root, "plus-two-cache.json")).Usage;

        Assert.Equal(11, inUtc.Days.Single(day => day.Day == new DateOnly(2026, 11, 17)).Total.Output);
        Assert.Equal(11, inPlusTwo.Days.Single(day => day.Day == Today).Total.Output);
        Assert.Equal(Today, inPlusTwo.To);
        Assert.Equal(new DateOnly(2026, 11, 17), inUtc.Days.Single(day => day.Total.Output == 11).Day);
    }

    private static string Canonical(LimitsReport report) => string.Join("\n",
        report.Hits.Select(hit => $"hit {hit.Time:O} {hit.Type} {hit.ResetsAt:O} {hit.TokensBefore}")
            .Concat(report.Usage.Days.Select(day => $"day {day.Day} {day.Total} {day.TopProject}"))
            .Concat(report.Usage.Projects.Select(project => $"project {project.Project} {project.Total} {project.ActiveDays}"))
            .Append($"other {report.Usage.OtherProjects} total {report.Usage.Total}"));
}

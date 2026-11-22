using System.Globalization;
using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The usage attribution of R8: the one time a session's usage counts at (<c>usage.attribution</c>), the default (<c>lastUpdate</c>), the copies of a
/// message across files, and Codex sessions in the same report as Claude's messages. Synthetic transcripts and rollouts in a temp folder, removed
/// afterwards. The day expectations under <c>event</c> are the ones <c>UsageReportTests</c> keeps.
/// </summary>
public sealed class UsageAttributionTests : IDisposable
{
    /// <summary>Noon on 18 November 2026 (UTC): the day of the Limits page.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly DateOnly Today = new(2026, 11, 18);

    private static readonly DateOnly Yesterday = new(2026, 11, 17);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-usage-attribution-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _projects;
    private readonly string _codex;
    private readonly string _cache;

    public UsageAttributionTests()
    {
        _projects = Path.Combine(_root, "projects");
        _codex = Path.Combine(_root, "codex", "sessions");
        _cache = Path.Combine(_root, "cache", "limit-history.json");
        Directory.CreateDirectory(_projects);
        Directory.CreateDirectory(_codex);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>A Claude assistant record with usage: the same shape <c>UsageReportTests</c> writes.</summary>
    private static string Record(DateTimeOffset at, string id, long input = 0, long output = 0, long cacheCreation = 0, long cacheRead = 0) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(at) + "\",\"requestId\":\"req_1\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-test\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":" + cacheCreation + ",\"cache_read_input_tokens\":" + cacheRead + "}}}";

    /// <summary>A limit entry, rejected, written at <paramref name="written"/>: the same shape <c>UsageReportTests</c> writes.</summary>
    private static string Limit(DateTimeOffset written, DateTimeOffset resetsAt) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(written) + "\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":" + resetsAt.ToUnixTimeSeconds() +
        ",\"rateLimitType\":\"five_hour\"}},\"message\":{\"model\":\"<synthetic>\",\"content\":[]}}";

    /// <summary>A session record naming its folder (a Windows path, so its backslashes are escaped for JSON).</summary>
    private static string Meta(string cwd) =>
        "{\"timestamp\":\"" + Stamp(At(18, 9, 0)) + "\",\"type\":\"session_meta\",\"payload\":{\"id\":\"synthetic\",\"cwd\":\"" +
        cwd.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}}";

    private static string Turn(string model) =>
        "{\"timestamp\":\"" + Stamp(At(18, 9, 0)) + "\",\"type\":\"turn_context\",\"payload\":{\"model\":\"" + model + "\"}}";

    /// <summary>A Codex token-count event with a running total, at <paramref name="at"/>.</summary>
    private static string Total(DateTimeOffset at, long input, long cached, long output, long reasoning) =>
        "{\"timestamp\":\"" + Stamp(at) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{" +
        "\"total_token_usage\":{\"input_tokens\":" + input + ",\"cached_input_tokens\":" + cached + ",\"cache_write_input_tokens\":0" +
        ",\"output_tokens\":" + output + ",\"reasoning_output_tokens\":" + reasoning + ",\"total_tokens\":" + (input + output) + "}}}}";

    private string Write(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_projects, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    private string WriteRollout(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_codex, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    private LimitsReport Report(UsageSettings? usage = null, string? codexRoot = null) =>
        LimitHistory.ReadReportWithCache(_projects, Now, _cache, zone: Utc, usage: usage, codexHistoryRoot: codexRoot);

    private static UsageSettings Rule(UsageAttribution attribution, bool reasoning = true) => new() { Attribution = attribution, ReasoningAsOutput = reasoning };

    private static UsageDay DayOf(LimitsReport report, DateOnly day) => report.Usage.Days.Single(row => row.Day == day);

    [Fact]
    public void TheRuleGivesTheTimeOfTheChosenAttribution()
    {
        var eventTime = At(18, 9, 0);
        var start = At(17, 8, 0);
        var last = At(18, 11, 0);

        Assert.Equal(last, UsageAttributionRule.TimeOf(UsageAttribution.LastUpdate, eventTime, start, last));
        Assert.Equal(start, UsageAttributionRule.TimeOf(UsageAttribution.Start, eventTime, start, last));
        Assert.Equal(eventTime, UsageAttributionRule.TimeOf(UsageAttribution.Event, eventTime, start, last));
    }

    [Fact]
    public void TheDefaultIsLastUpdateAndAMessageAcrossMidnightMovesToTheLaterDayUnderIt()
    {
        Write("project-a/session.jsonl",
        [
            Record(At(17, 23, 59), "msg_x", input: 4, output: 10),
            Record(At(18, 0, 1), "msg_x", input: 4, output: 50),
        ]);

        Assert.Equal(UsageAttribution.LastUpdate, new UsageSettings().Attribution);
        var byDefault = Report();
        // One file, last written by its record at 00:01 on the 18th: the message counts on the 18th, not the 17th.
        Assert.Equal(new TokenUsage(4, 50, 0, 0), DayOf(byDefault, Today).Total);
        Assert.Equal(TokenUsage.Zero, DayOf(byDefault, Yesterday).Total);
        Assert.Equal(new TokenUsage(4, 50, 0, 0), byDefault.Usage.Total);

        // Under event the same fixture keeps the earlier day (the rule UsageReportTests keeps).
        var byEvent = Report(Rule(UsageAttribution.Event));
        Assert.Equal(new TokenUsage(4, 50, 0, 0), DayOf(byEvent, Yesterday).Total);
        Assert.Equal(TokenUsage.Zero, DayOf(byEvent, Today).Total);
    }

    [Fact]
    public void AWholeSessionMovesToItsStartOrItsLastUpdateWhileEventSplitsIt()
    {
        Write("project-a/session.jsonl",
        [
            Record(At(17, 9, 0), "msg_1", output: 10),
            Record(At(18, 9, 0), "msg_2", output: 20),
        ]);

        var start = Report(Rule(UsageAttribution.Start));
        Assert.Equal(30, DayOf(start, Yesterday).Total.Output);
        Assert.Equal(0, DayOf(start, Today).Total.Output);

        var last = Report(Rule(UsageAttribution.LastUpdate));
        Assert.Equal(0, DayOf(last, Yesterday).Total.Output);
        Assert.Equal(30, DayOf(last, Today).Total.Output);

        var each = Report(Rule(UsageAttribution.Event));
        Assert.Equal(10, DayOf(each, Yesterday).Total.Output);
        Assert.Equal(20, DayOf(each, Today).Total.Output);
    }

    [Fact]
    public void TwoCopiesOfAMessageCountOnceAtTheirEarliestAttributedTimeWithTheLargestValues()
    {
        // The same message in two sessions: one copy in a file whose last update is on the 17th, one in a file last updated on the 18th.
        Write("proj-a/session.jsonl", [Record(At(17, 10, 0), "msg_d", input: 5, output: 10), Record(At(17, 10, 5), "msg_d", input: 5, output: 12)]);
        Write("proj-b/session.jsonl", [Record(At(18, 9, 0), "msg_d", input: 2, output: 40, cacheRead: 9)]);

        var report = Report();

        Assert.Equal(new TokenUsage(5, 40, 0, 9), DayOf(report, Yesterday).Total);
        Assert.Equal(TokenUsage.Zero, DayOf(report, Today).Total);
        Assert.Equal(new TokenUsage(5, 40, 0, 9), report.Usage.Total);
        var project = Assert.Single(report.Usage.Projects);
        Assert.Equal("proj-a", project.Project);
    }

    [Fact]
    public void ALaterCopyAfterNowDoesNotMoveAnEarlierCopyOfAMessage()
    {
        Write("proj-a/session.jsonl", [Record(At(18, 9, 0), "msg_e", output: 30)]);
        // The later copy is after now, so it is left out of the window; the earlier copy places the message on the 18th, and the larger value counts.
        Write("proj-b/session.jsonl", [Record(At(18, 14, 0), "msg_e", output: 99)]);

        var report = Report();

        Assert.Equal(99, DayOf(report, Today).Total.Output);
        Assert.Equal(99, report.Usage.Total.Output);
        Assert.Equal("proj-a", Assert.Single(report.Usage.Projects).Project);
    }

    [Fact]
    public void ACodexSessionCountsOnceAtItsLastTotalAndNeverInAClaudeLimitHit()
    {
        var hit = At(18, 11, 0);
        Write("proj-a/session.jsonl", [Record(At(18, 9, 30), "msg_c", input: 3, output: 7), Limit(hit, hit.AddHours(3))]);
        WriteRollout("2026/11/18/rollout-one.jsonl",
        [
            Meta("C:\\Work\\proj-x"),
            Turn("codex-model"),
            Total(At(18, 10, 1), 400, 300, 50, 10),
            Total(At(18, 10, 5), 1000, 800, 60, 20),
        ]);

        var report = Report(codexRoot: _codex);

        // Codex: input 1000 less 800 cached is 200; output 60 (reasoning inside it); cached 800. Claude's message adds 3 and 7.
        Assert.Equal(new TokenUsage(203, 67, 0, 800), report.Usage.Total);
        Assert.Equal(new TokenUsage(203, 67, 0, 800), DayOf(report, Today).Total);
        var codex = report.Usage.Projects.Single(project => project.Project == "proj-x");
        Assert.Equal(new TokenUsage(200, 60, 0, 800), codex.Total);
        Assert.Equal(1, codex.ActiveDays);

        // The hit's tokens are Claude's only: the Codex totals (10:01 and 10:05, inside the five hours) are not in them.
        var counted = Assert.Single(report.Hits);
        Assert.Equal(new TokenUsage(3, 7, 0, 0), counted.TokensBefore);

        // Without a Codex root, Codex is left out of the report.
        var claudeOnly = Report(codexRoot: null);
        Assert.Equal(new TokenUsage(3, 7, 0, 0), claudeOnly.Usage.Total);
        Assert.DoesNotContain(claudeOnly.Usage.Projects, project => project.Project == "proj-x");
    }

    [Fact]
    public void ACodexSessionMovesWithItsStartUnderStartAndStaysOnItsLastDayUnderLastUpdateAndEvent()
    {
        // Its first total is on the 17th, its last on the 18th: the whole session's last total moves with the rule.
        WriteRollout("2026/11/17/rollout-two.jsonl",
        [
            Meta("C:\\Work\\proj-x"),
            "{\"timestamp\":\"2026-11-17T23:50:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":" +
            "{\"input_tokens\":100,\"cached_input_tokens\":0,\"cache_write_input_tokens\":0,\"output_tokens\":10,\"reasoning_output_tokens\":0}}}}",
            Total(At(18, 10, 5), 300, 0, 30, 0),
        ]);

        var start = Report(Rule(UsageAttribution.Start), codexRoot: _codex);
        Assert.Equal(new TokenUsage(300, 30, 0, 0), DayOf(start, Yesterday).Total);
        Assert.Equal(TokenUsage.Zero, DayOf(start, Today).Total);

        var last = Report(Rule(UsageAttribution.LastUpdate), codexRoot: _codex);
        Assert.Equal(TokenUsage.Zero, DayOf(last, Yesterday).Total);
        Assert.Equal(new TokenUsage(300, 30, 0, 0), DayOf(last, Today).Total);

        var each = Report(Rule(UsageAttribution.Event), codexRoot: _codex);
        Assert.Equal(new TokenUsage(300, 30, 0, 0), DayOf(each, Today).Total);
    }

    [Fact]
    public void TheReasoningSettingChangesTheCodexOutput()
    {
        WriteRollout("2026/11/18/rollout-three.jsonl", [Meta("C:\\Work\\proj-x"), Total(At(18, 10, 0), 100, 0, 50, 20)]);

        Assert.Equal(50, Report(Rule(UsageAttribution.LastUpdate, reasoning: true), codexRoot: _codex).Usage.Total.Output);
        Assert.Equal(30, Report(Rule(UsageAttribution.LastUpdate, reasoning: false), codexRoot: _codex).Usage.Total.Output);
    }

    [Fact]
    public void ReadFolderLeavesOutAFileLastWrittenBeforeTheWindowAndAFileWithNoTotal()
    {
        var old = WriteRollout("2026/10/01/rollout-old.jsonl", [Meta("C:\\Work\\proj-x"), Total(At(18, 10, 0), 10, 0, 1, 0)]);
        File.SetLastWriteTimeUtc(old, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteRollout("2026/11/18/rollout-new.jsonl", [Meta("C:\\Work\\proj-x"), Total(At(18, 10, 0), 20, 0, 2, 0)]);
        WriteRollout("2026/11/18/rollout-empty.jsonl", [Meta("C:\\Work\\proj-x"), "{\"timestamp\":\"" + Stamp(At(18, 10, 0)) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}"]);

        var found = CodexUsageReader.ReadFolder(_codex, Now.AddDays(-30), reasoningAsOutput: true);

        var only = Assert.Single(found);
        Assert.EndsWith("rollout-new.jsonl", only.Path, StringComparison.Ordinal);
        Assert.Equal(new TokenUsage(20, 2, 0, 0), only.Usage);
    }

    [Fact]
    public void AMissingCodexFolderGivesNoSessions()
    {
        Assert.Empty(CodexUsageReader.ReadFolder(Path.Combine(_root, "no-such-folder"), Now.AddDays(-30), reasoningAsOutput: true));
    }
}

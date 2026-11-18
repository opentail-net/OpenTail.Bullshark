using System.Text;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// "Start when the usage limit resets" (gems item 5, D5.6): the reset time comes from the structured limit entry that Claude Code writes
/// to a transcript. Only a rejected limit that resets in the future counts, the latest of them wins, the time is in seconds, and a file
/// or a line that cannot be read is skipped. Each test writes its transcripts into a temp folder that it removes.
/// </summary>
public sealed class LimitResetTests : IDisposable
{
    /// <summary>Ten past ten in the morning of 16 November 2026 (UTC): before the limit in the sample line resets.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 5, 7, TimeSpan.Zero);

    /// <summary>1794834600 seconds: 13:10 UTC on 16 November 2026, the reset the owner's transcript reported (1:10pm London).</summary>
    private static readonly DateTimeOffset Reset = new(2026, 11, 16, 13, 10, 0, TimeSpan.Zero);

    /// <summary>A reset the following week, as a weekly limit would report it.</summary>
    private static readonly DateTimeOffset WeeklyReset = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-limit-" + Guid.NewGuid().ToString("N")[..8]);

    public LimitResetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A limit entry in the shape the owner's transcript holds it (2026-11-16), with the given status and reset in Unix seconds.</summary>
    private static string LimitLine(long resetsAt, string status = "rejected", string type = "five_hour") =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-16T10:05:07.429Z\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"" + status + "\",\"resetsAt\":" + resetsAt + ",\"rateLimitType\":\"" + type + "\"}}," +
        "\"message\":{\"model\":\"<synthetic>\",\"content\":[{\"type\":\"text\",\"text\":\"You've hit your 5-hour limit - resets 1:10pm (Europe/London)\"}]}}";

    /// <summary>An ordinary user line, about a kilobyte long, for filling a transcript.</summary>
    private static string Filler() =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-16T09:00:00Z\",\"message\":{\"role\":\"user\",\"content\":\"" + new string('x', 900) + "\"}}";

    /// <summary>Writes a transcript under the temp folder, creating its folders, and returns its path.</summary>
    private string Write(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void TheRealLineShapeGivesTheResetInstant()
    {
        Write("project-a/session.jsonl", [LimitLine(1794834600)]);

        Assert.Equal(Reset, LimitReset.Find(_root, Now));
    }

    [Theory]
    [InlineData("allowed")]
    [InlineData("allowed_warning")]
    public void OnlyARejectedStatusCounts(string status)
    {
        Write("project-a/session.jsonl", [LimitLine(1794834600, status: status)]);

        Assert.Null(LimitReset.Find(_root, Now));
    }

    [Fact]
    public void AResetThatIsNotAfterNowIsIgnored()
    {
        // One minute ago, and exactly now: neither is a reset still ahead.
        Write("project-a/session.jsonl", [LimitLine(Now.AddMinutes(-1).ToUnixTimeSeconds()), LimitLine(Now.ToUnixTimeSeconds())]);

        Assert.Null(LimitReset.Find(_root, Now));
    }

    [Fact]
    public void LinesThatCannotBeReadAreSkippedAndTheValidOneAroundThemIsStillFound()
    {
        var loneSurrogate = "{\"apiError\":\"usage_limit_\\ud800\",\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":1794834600}}}";
        var truncated = LimitLine(1794834600)[..60];
        Write("project-a/session.jsonl", ["", "not json", truncated, "[1,2]", loneSurrogate, LimitLine(1794834600)]);

        Assert.Equal(Reset, LimitReset.Find(_root, Now));
    }

    [Fact]
    public void AFileWithOnlyUnreadableLinesGivesNoReset()
    {
        var loneSurrogate = "{\"apiError\":\"usage_limit_\\ud800\",\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":1794834600}}}";
        Write("project-a/session.jsonl", ["", "not json", LimitLine(1794834600)[..60], "5", loneSurrogate]);

        Assert.Null(LimitReset.Find(_root, Now));
    }

    [Fact]
    public void TheLaterResetOfTwoLimitsWinsAcrossFilesIncludingASubagentFile()
    {
        // The five-hour limit is in the session's file; the weekly limit is in a subagent's file, one folder deeper.
        Write("project-a/session.jsonl", [LimitLine(1794834600, type: "five_hour")]);
        Write("project-b/parent/subagents/agent-1.jsonl", [LimitLine(WeeklyReset.ToUnixTimeSeconds(), type: "weekly")]);

        Assert.Equal(WeeklyReset, LimitReset.Find(_root, Now));
    }

    [Fact]
    public void OnlyLimitsThatHaveAlreadyResetGiveNoReset()
    {
        Write("project-a/session.jsonl", [LimitLine(Now.AddHours(-3).ToUnixTimeSeconds())]);
        Write("project-b/session.jsonl", [LimitLine(Now.AddDays(-2).ToUnixTimeSeconds(), type: "weekly")]);

        Assert.Null(LimitReset.Find(_root, Now));
    }

    [Fact]
    public void ATranscriptOlderThanEightDaysIsIgnoredAndANewerOneIsRead()
    {
        var old = Write("old/session.jsonl", [LimitLine(WeeklyReset.ToUnixTimeSeconds())]);
        File.SetLastWriteTimeUtc(old, Now.UtcDateTime.AddDays(-9));
        var recent = Write("recent/session.jsonl", [LimitLine(Reset.ToUnixTimeSeconds())]);
        File.SetLastWriteTimeUtc(recent, Now.UtcDateTime.AddDays(-7));

        Assert.Equal(Reset, LimitReset.Find(_root, Now));
    }

    [Fact]
    public void ALimitInTheLastTailOfALargeTranscriptIsFoundAndACutLineDoesNotThrow()
    {
        // An older limit at the top is outside the last 256 KB, so it is not read (D4); the one at the end is.
        var lines = new List<string> { LimitLine(WeeklyReset.ToUnixTimeSeconds()) };
        lines.AddRange(Enumerable.Repeat(Filler(), 400));
        lines.Add(LimitLine(1794834600));
        Write("project-a/session.jsonl", lines);
        Assert.True(new FileInfo(Path.Combine(_root, "project-a/session.jsonl")).Length > 256 * 1024);

        Assert.Equal(Reset, LimitReset.Find(_root, Now));
    }

    [Fact]
    public void AFileThatIsLockedIsSkippedAndTheOthersAreRead()
    {
        // The exclusive lock makes the file unreadable here on Windows, which is where the owner's transcripts are read.
        if (!OperatingSystem.IsWindows()) return;
        var locked = Write("locked/session.jsonl", [LimitLine(WeeklyReset.ToUnixTimeSeconds())]);
        Write("open/session.jsonl", [LimitLine(1794834600)]);

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(Reset, LimitReset.Find(_root, Now));
    }

    [Fact]
    public void StartingRunsTwoMinutesAfterTheReset()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), LimitReset.StartFor(Reset) - Reset);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 13, 12, 0, TimeSpan.Zero), LimitReset.StartFor(Reset));
    }

    [Fact]
    public void TheResetIsReadAsSecondsAndAMillisecondValueIsNotTakenAsOne()
    {
        // 1794834600 seconds is 13:10 UTC on 16 November 2026. The same number in milliseconds is far past the year 9999 in seconds,
        // so it is refused rather than read as a date in 2026 or 58000.
        Write("project-a/session.jsonl", [LimitLine(1794834600)]);
        Assert.Equal(Reset, LimitReset.Find(_root, Now));

        Write("project-b/session.jsonl", [LimitLine(1794834600000)]);
        Assert.Equal(Reset, LimitReset.Find(_root, Now));
    }
}

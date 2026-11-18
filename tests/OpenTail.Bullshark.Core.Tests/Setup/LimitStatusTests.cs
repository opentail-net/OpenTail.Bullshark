using System.Text;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The usage limits in force, for the status bar (A2). Only a rejected limit that resets after the time counts, one per type, the latest
/// reset of each type, and the governing one (the latest reset) comes first. Each test writes its transcripts into a temp folder it removes.
/// </summary>
public sealed class LimitStatusTests : IDisposable
{
    /// <summary>Ten past ten in the morning of 16 November 2026 (UTC).</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 5, 7, TimeSpan.Zero);

    /// <summary>13:10 UTC on 16 November 2026: the five-hour reset the owner's transcript reported.</summary>
    private static readonly DateTimeOffset FiveHourReset = new(2026, 11, 16, 13, 10, 0, TimeSpan.Zero);

    /// <summary>A weekly reset the following week.</summary>
    private static readonly DateTimeOffset WeeklyReset = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-status-" + Guid.NewGuid().ToString("N")[..8]);

    public LimitStatusTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A limit entry in the shape the owner's transcript holds it, with the given status, type and reset in Unix seconds.</summary>
    private static string LimitLine(long resetsAt, string status = "rejected", string type = "five_hour") =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-16T10:05:07.429Z\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"" + status + "\",\"resetsAt\":" + resetsAt + ",\"rateLimitType\":\"" + type + "\"}}," +
        "\"message\":{\"model\":\"<synthetic>\",\"content\":[{\"type\":\"text\",\"text\":\"You've hit your limit\"}]}}";

    /// <summary>Writes a transcript under the temp folder, creating its folders, and returns its path.</summary>
    private string Write(string relative, IEnumerable<string> lines)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void TwoTypesInForceAreBothListedAndTheGoverningOneComesFirst()
    {
        Write("project-a/session.jsonl", [LimitLine(FiveHourReset.ToUnixTimeSeconds(), type: "five_hour")]);
        Write("project-b/session.jsonl", [LimitLine(WeeklyReset.ToUnixTimeSeconds(), type: "weekly")]);

        var active = LimitStatus.Active(_root, Now);

        Assert.Equal([new ActiveLimit("weekly", WeeklyReset), new ActiveLimit("five_hour", FiveHourReset)], active);
    }

    [Fact]
    public void ALimitThatHasAlreadyResetIsNotListed()
    {
        Write("project-a/session.jsonl", [LimitLine(Now.AddHours(-3).ToUnixTimeSeconds(), type: "five_hour")]);
        Write("project-b/session.jsonl", [LimitLine(WeeklyReset.ToUnixTimeSeconds(), type: "weekly")]);

        var active = LimitStatus.Active(_root, Now);

        Assert.Equal([new ActiveLimit("weekly", WeeklyReset)], active);
    }

    [Theory]
    [InlineData("allowed")]
    [InlineData("allowed_warning")]
    public void ANonRejectedStatusIsNotListed(string status)
    {
        Write("project-a/session.jsonl", [LimitLine(FiveHourReset.ToUnixTimeSeconds(), status: status)]);

        Assert.Empty(LimitStatus.Active(_root, Now));
    }

    [Fact]
    public void TheLatestResetOfATypeIsKeptOnce()
    {
        // The same type written twice, in two files: the later reset is the one that holds, and the type is listed once.
        Write("project-a/session.jsonl", [LimitLine(FiveHourReset.ToUnixTimeSeconds(), type: "five_hour")]);
        var later = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
        Write("project-b/session.jsonl", [LimitLine(later.ToUnixTimeSeconds(), type: "five_hour")]);

        var active = LimitStatus.Active(_root, Now);

        Assert.Equal([new ActiveLimit("five_hour", later)], active);
    }

    [Fact]
    public void AMissingOrEmptyFolderGivesNoLimits()
    {
        Assert.Empty(LimitStatus.Active(Path.Combine(_root, "does-not-exist"), Now));
        Assert.Empty(LimitStatus.Active(_root, Now));
    }

    [Theory]
    [InlineData("five_hour", "5-hour")]
    [InlineData("weekly", "weekly")]
    [InlineData("seven_day", "weekly")]
    [InlineData("opus_limit", "opus_limit")]
    public void TheLimitTypeIsNamedForAPerson(string type, string label)
    {
        Assert.Equal(label, LimitStatus.LabelOf(type));
    }
}

using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// PP-D5 (2026-11-23): the Copilot usage report from <c>session.shutdown</c> events, on fixture files in a folder the test owns. Only the events files are
/// written; no Copilot program runs. The timestamps are at 12:00 UTC, so each one lands on the same local day for any time zone offset of the machine.
/// </summary>
public sealed class CopilotUsageReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-copilot-usage-" + Guid.NewGuid().ToString("N"));

    /// <summary>Now: 2026-11-23 at 12:00 UTC. The window of 7 days is 2026-11-17 to 2026-11-23 local.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A session folder with an events file holding the given lines.</summary>
    private void Session(string id, params string[] lines)
    {
        var folder = Path.Combine(_root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "events.jsonl"), lines);
    }

    /// <summary>A <c>session.shutdown</c> line at the given UTC time with the given premium requests and one model's tokens.</summary>
    private static string Shutdown(string at, int premium, long input, long output) =>
        "{\"type\":\"session.shutdown\",\"data\":{\"totalPremiumRequests\":" + premium
        + ",\"modelMetrics\":{\"model-a\":{\"requests\":{\"count\":1,\"cost\":1},\"usage\":{\"inputTokens\":" + input
        + ",\"outputTokens\":" + output + "}}}},\"id\":\"e\",\"timestamp\":\"" + at + "\",\"parentId\":null}";

    [Fact]
    public void SessionsAreSummedPerLocalDayNewestFirst()
    {
        Session("s1", "{\"type\":\"user.message\",\"data\":{\"content\":\"hi\"}}", Shutdown("2026-11-22T12:00:00Z", 2, 100, 20));
        Session("s2", Shutdown("2026-11-22T12:00:00Z", 3, 50, 5));
        Session("s3", Shutdown("2026-11-23T12:00:00Z", 1, 10, 1));

        var report = CopilotUsageReader.Read(_root, Now, 7, CancellationToken.None);

        Assert.Equal(0, report.SkippedFiles);
        Assert.Equal(2, report.Days.Count);
        Assert.Equal(new CopilotUsageDay(new DateOnly(2026, 11, 23), 1, 1, 10, 1), report.Days[0]);
        Assert.Equal(new CopilotUsageDay(new DateOnly(2026, 11, 22), 2, 5, 150, 25), report.Days[1]);
    }

    [Fact]
    public void ASessionOutsideTheWindowIsLeftOut()
    {
        Session("old", Shutdown("2026-11-10T12:00:00Z", 9, 999, 999));

        var report = CopilotUsageReader.Read(_root, Now, 7, CancellationToken.None);

        Assert.Empty(report.Days);
    }

    [Fact]
    public void TheLastShutdownOfASessionIsTheOneCounted()
    {
        Session("s1",
            Shutdown("2026-11-23T10:00:00Z", 1, 10, 1),
            Shutdown("2026-11-23T11:00:00Z", 4, 40, 4));

        var report = CopilotUsageReader.Read(_root, Now, 7, CancellationToken.None);

        Assert.Equal(new CopilotUsageDay(new DateOnly(2026, 11, 23), 1, 4, 40, 4), Assert.Single(report.Days));
    }

    [Fact]
    public void ASessionWithNoShutdownIsNotCounted()
    {
        Session("open", "{\"type\":\"user.message\",\"data\":{\"content\":\"still running\"}}");

        var report = CopilotUsageReader.Read(_root, Now, 7, CancellationToken.None);

        Assert.Empty(report.Days);
    }

    [Fact]
    public void AMissingRootGivesNoDaysAndNoSkippedFiles()
    {
        var report = CopilotUsageReader.Read(Path.Combine(_root, "missing"), Now, 7, CancellationToken.None);

        Assert.Empty(report.Days);
        Assert.Equal(0, report.SkippedFiles);
    }
}

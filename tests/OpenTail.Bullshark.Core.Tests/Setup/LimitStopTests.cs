using OpenTail.Bullshark.Core.Setup;
using Xunit;
using static OpenTail.Bullshark.Core.Tests.Setup.LimitLines;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The stop a transcript ends at (limit wake, 2026-11-20): a usage-limit entry that no user entry (other than a tool result) or other assistant entry
/// follows. Synthetic lines only.
/// </summary>
public sealed class LimitStopTests
{
    private static readonly DateTimeOffset Hit = new(2026, 11, 20, 10, 0, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = new(2026, 11, 20, 12, 20, 0, TimeSpan.Zero);

    [Fact]
    public void AStopWithOnlyMetadataAfterItIsFound()
    {
        var stop = LimitStop.Find([
            Assistant(Hit.AddMinutes(-1), "msg-0"),
            Limit(Hit, Reset),
            Metadata("system", Hit.AddSeconds(1)),
            Metadata("cost-state", Hit.AddSeconds(2)),
            Metadata("last-prompt", Hit.AddSeconds(3)),
            Metadata("ai-title", Hit.AddSeconds(4)),
            Metadata("mode", Hit.AddSeconds(5)),
            Metadata("queue-operation", Hit.AddSeconds(6)),
            Metadata("file-history-snapshot", Hit.AddSeconds(7)),
        ]);

        Assert.Equal(new LimitStop(Hit, Reset, "five_hour"), stop);
    }

    [Fact]
    public void AWakeAfterTheStopEndsIt()
    {
        var stop = LimitStop.Find([Limit(Hit, Reset), Metadata("system", Hit.AddSeconds(1)), UserMessage(Reset.AddMinutes(2))]);

        Assert.Null(stop);
    }

    [Fact]
    public void AToolResultAfterTheStopDoesNotEndIt()
    {
        var stop = LimitStop.Find([Limit(Hit, Reset), ToolResult(Hit.AddSeconds(3))]);

        Assert.Equal(new LimitStop(Hit, Reset, "five_hour"), stop);
    }

    [Fact]
    public void ANonLimitAssistantEntryAfterTheStopEndsIt()
    {
        var stop = LimitStop.Find([Limit(Hit, Reset), Assistant(Hit.AddSeconds(5))]);

        Assert.Null(stop);
    }

    [Fact]
    public void TwoLimitEntriesInARowAreOneHitAndTheLastCounts()
    {
        var later = Hit.AddSeconds(30);
        var stop = LimitStop.Find([Limit(Hit, Reset), Limit(later, Reset.AddMinutes(5))]);

        Assert.Equal(new LimitStop(later, Reset.AddMinutes(5), "five_hour"), stop);
    }

    [Fact]
    public void AStatusThatIsNotRejectedIsNotAStop()
    {
        Assert.Null(LimitStop.Find([Limit(Hit, Reset, status: "allowed_warning")]));
    }

    [Fact]
    public void ANewerNonRejectedLimitEntryHidesAnOlderRejectedOne()
    {
        // The last usage-limit entry is the candidate, so a later one that is not rejected means no stop.
        var stop = LimitStop.Find([Limit(Hit, Reset), Limit(Hit.AddSeconds(9), Reset, status: "allowed_warning")]);

        Assert.Null(stop);
    }

    [Fact]
    public void ALimitEntryWithNoTimeIsNotAStop()
    {
        var untimed = Limit(Hit, Reset).Replace("\"timestamp\":\"" + Stamp(Hit) + "\",", string.Empty);

        Assert.Null(LimitStop.Find([untimed]));
    }

    [Fact]
    public void GarbageLinesAreSkippedAndTheStopAroundThemIsStillFound()
    {
        var truncated = Limit(Hit, Reset)[..60];
        var stop = LimitStop.Find(["", "not json", truncated, "[1,2]", Limit(Hit, Reset), "{\"type\":"]);

        Assert.Equal(new LimitStop(Hit, Reset, "five_hour"), stop);
    }

    [Fact]
    public void ASevenDayLimitKeepsItsType()
    {
        var stop = LimitStop.Find([Limit(Hit, Reset, type: "seven_day")]);

        Assert.Equal(new LimitStop(Hit, Reset, "seven_day"), stop);
    }

    [Fact]
    public void AnEmptyTranscriptHasNoStop()
    {
        Assert.Null(LimitStop.Find([]));
    }

    [Fact]
    public void AStopIsRecentWithinTheEightDayWindowAndNotAfter()
    {
        var stop = new LimitStop(Hit, Reset, "five_hour");

        Assert.True(stop.IsRecent(Hit.AddDays(8)));
        Assert.False(stop.IsRecent(Hit.AddDays(8).AddSeconds(1)));
    }
}

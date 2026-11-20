using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The words a stopped session shows (limit wake, 2026-11-20): the reset time with the day when it is not today, "· wakes after" while it waits,
/// "· waking now", the notice's note, and nothing for no stop or an old one. The time zone is UTC so the test does not depend on the machine.
/// </summary>
public sealed class LimitWakeTextTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    /// <summary>Ten in the morning of Friday 20 November 2026, UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly LimitStop Stop = new(Now.AddMinutes(-5), new DateTimeOffset(2026, 11, 20, 12, 20, 0, TimeSpan.Zero), "five_hour");

    private static readonly LimitWakeDecision Waiting = new(LimitWakeAction.Wait);

    [Fact]
    public void TodayBeforeTheResetShowsTheTimeAndWaiting()
    {
        var text = LimitWakeText.Line(Stop, Waiting, Now, Zone);

        Assert.Equal("Limit resets 12:20 · wakes after", text);
    }

    [Fact]
    public void TodayBeforeTheResetWithTheFlagOffShowsTheTimeOnly()
    {
        var text = LimitWakeText.Line(Stop, new LimitWakeDecision(LimitWakeAction.None), Now, Zone);

        Assert.Equal("Limit resets 12:20", text);
    }

    [Fact]
    public void TomorrowShowsTheDayWithTheTime()
    {
        var stop = Stop with { ResetsAt = new DateTimeOffset(2026, 11, 21, 7, 20, 0, TimeSpan.Zero) };

        var text = LimitWakeText.Line(stop, null, Now, Zone);

        Assert.Equal("Limit resets Sat 07:20", text);
    }

    [Fact]
    public void AResetPastShowsLimitHasReset()
    {
        var stop = Stop with { ResetsAt = Now.AddMinutes(-1) };

        Assert.Equal("Limit has reset", LimitWakeText.Line(stop, null, Now, Zone));
    }

    [Fact]
    public void AfterTheResetWhileWaitingForAnotherLimitSaysWakesAfter()
    {
        var stop = Stop with { ResetsAt = Now.AddMinutes(-1) };

        Assert.Equal("Limit has reset · wakes after", LimitWakeText.Line(stop, Waiting, Now, Zone));
    }

    [Fact]
    public void WakingNowSaysSo()
    {
        var stop = Stop with { ResetsAt = Now.AddMinutes(-5) };

        Assert.Equal("Limit has reset · waking now", LimitWakeText.Line(stop, new LimitWakeDecision(LimitWakeAction.WakeNow), Now, Zone));
    }

    [Fact]
    public void ANoticeShowsItsNote()
    {
        var stop = Stop with { ResetsAt = Now.AddMinutes(-5) };
        var notice = new LimitWakeDecision(LimitWakeAction.Notify, "running: open it to continue");

        Assert.Equal("Limit has reset · running: open it to continue", LimitWakeText.Line(stop, notice, Now, Zone));
    }

    [Fact]
    public void NoStopShowsNothing()
    {
        Assert.Null(LimitWakeText.Line(null, Waiting, Now, Zone));
    }

    [Fact]
    public void AStopOlderThanTheWindowShowsNothing()
    {
        var old = Stop with { HitAt = Now.AddDays(-9) };

        Assert.Null(LimitWakeText.Line(old, Waiting, Now, Zone));
    }

    [Fact]
    public void TheClockIsTheTimeZonesLocalClock()
    {
        // 12:20 UTC is 13:20 in a zone one hour ahead: the text shows the zone's clock.
        var oneHourAhead = TimeZoneInfo.CreateCustomTimeZone("synthetic+1", TimeSpan.FromHours(1), "synthetic", "synthetic");

        Assert.Equal("13:20", LimitWakeText.ClockOf(Stop.ResetsAt, Now, oneHourAhead));
    }
}

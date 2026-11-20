using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The wake rule (limit wake, 2026-11-20): one test for each row of the decision table, and the boundary, re-arm and clock cases. The inputs are
/// made up here; nothing reads a transcript, the listing or the trust record.
/// </summary>
public sealed class LimitWakeTests
{
    private static readonly DateTimeOffset Hit = new(2026, 11, 20, 10, 0, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = new(2026, 11, 20, 12, 20, 0, TimeSpan.Zero);
    private static readonly LimitStop Stop = new(Hit, Reset, "five_hour");

    /// <summary>Five minutes after the reset: past the margin, so the time rows do not hold.</summary>
    private static readonly DateTimeOffset AfterReset = Reset.AddMinutes(5);

    private static readonly WakeFolder Usable = new(true, null);

    private static LimitWakeInput Input(
        LimitStop? stop = null,
        bool noStop = false,
        bool auto = true,
        DateTimeOffset? now = null,
        WakeListing listing = WakeListing.NotListed,
        DateTimeOffset? wokenHit = null,
        IReadOnlyList<DateTimeOffset>? wakeTimes = null,
        DateTimeOffset? inForce = null,
        WakeFolder? folder = null,
        Action? folderCalled = null,
        Action? inForceCalled = null,
        bool keptInRail = false) =>
        new(noStop ? null : stop ?? Stop, auto, now ?? AfterReset, listing, wokenHit, wakeTimes,
            () => { inForceCalled?.Invoke(); return inForce; },
            () => { folderCalled?.Invoke(); return folder ?? Usable; },
            keptInRail);

    [Fact]
    public void Row1_TheOwnerDidNotAskSoNothingIsDone()
    {
        var decision = LimitWake.Decide(Input(auto: false));

        Assert.Equal(LimitWakeAction.None, decision.Action);
    }

    [Fact]
    public void Row1_NoStopSoNothingIsDone()
    {
        var decision = LimitWake.Decide(Input(noStop: true));

        Assert.Equal(LimitWakeAction.None, decision.Action);
    }

    [Fact]
    public void Row1_AStopOlderThanTheEightDayWindowIsNotWoken()
    {
        var decision = LimitWake.Decide(Input(now: Hit.AddDays(8).AddSeconds(1), folder: new WakeFolder(true, null)));

        Assert.Equal(LimitWakeAction.None, decision.Action);
    }

    [Fact]
    public void Row2_BeforeTheReset_WaitsAndDoesNotCallTheFolderOrTheLimitCheck()
    {
        var folderCalled = false;
        var inForceCalled = false;

        var decision = LimitWake.Decide(Input(now: Reset.AddMinutes(-1),
            folderCalled: () => folderCalled = true, inForceCalled: () => inForceCalled = true));

        Assert.Equal(LimitWakeAction.Wait, decision.Action);
        Assert.False(folderCalled);
        Assert.False(inForceCalled);
    }

    [Fact]
    public void Row2_AnotherLimitStillInForce_Waits()
    {
        var weeklyReset = Reset.AddDays(3);

        var decision = LimitWake.Decide(Input(inForce: weeklyReset));

        Assert.Equal(LimitWakeAction.Wait, decision.Action);
    }

    [Fact]
    public void Row3_ThisHitWasWokenAlready_NothingIsDone()
    {
        var decision = LimitWake.Decide(Input(wokenHit: Hit));

        Assert.Equal(LimitWakeAction.None, decision.Action);
    }

    [Fact]
    public void Row4_ThreeWakesInTheLastDay_NoticeAndNoWake()
    {
        var times = new[] { AfterReset.AddHours(-1), AfterReset.AddHours(-2), AfterReset.AddHours(-3) };

        var decision = LimitWake.Decide(Input(wakeTimes: times));

        Assert.Equal(LimitWakeAction.Notify, decision.Action);
        Assert.Equal("woken 3 times in a day; not waking again", decision.Note);
    }

    [Fact]
    public void Row4_WakesOlderThanADayDoNotCount()
    {
        var times = new[] { AfterReset.AddHours(-25), AfterReset.AddHours(-26), AfterReset.AddHours(-30) };

        var decision = LimitWake.Decide(Input(wakeTimes: times));

        Assert.Equal(LimitWakeAction.WakeNow, decision.Action);
    }

    [Fact]
    public void Row5_ListedStoppedOrFailed_NoticeOnly()
    {
        var decision = LimitWake.Decide(Input(listing: WakeListing.Stopped));

        Assert.Equal(LimitWakeAction.Notify, decision.Action);
    }

    [Fact]
    public void Row6_ListedAlive_NoticeOnlyAndNoStart()
    {
        var decision = LimitWake.Decide(Input(listing: WakeListing.Alive));

        Assert.Equal(LimitWakeAction.Notify, decision.Action);
        Assert.Equal("running: open it to continue", decision.Note);
    }

    [Fact]
    public void Row7_NotListedAndTheFolderIsUsable_WakesNow()
    {
        var decision = LimitWake.Decide(Input(listing: WakeListing.NotListed, folder: Usable));

        Assert.Equal(LimitWakeAction.WakeNow, decision.Action);
    }

    [Fact]
    public void Row7b_NotListedButKeptInTheRail_NoticeAndNoStartAttempt()
    {
        // The shared start refuses a session the rail holds, so the rule gives the notice and does not look at the folder.
        var folderCalled = false;

        var decision = LimitWake.Decide(Input(keptInRail: true, folderCalled: () => folderCalled = true));

        Assert.Equal(LimitWakeAction.Notify, decision.Action);
        Assert.Equal(LimitWake.KeptInRailNote, decision.Note);
        Assert.False(folderCalled);
    }

    [Fact]
    public void Row8_NotListedAndTheFolderIsNotUsable_NoticeWithTheReason()
    {
        var decision = LimitWake.Decide(Input(folder: new WakeFolder(false, "the folder is missing")));

        Assert.Equal(LimitWakeAction.Notify, decision.Action);
        Assert.Equal("not woken: the folder is missing", decision.Note);
    }

    [Fact]
    public void Row8_AnUnusableFolderWithNoReasonStillGivesAReason()
    {
        var decision = LimitWake.Decide(Input(folder: new WakeFolder(false, null)));

        Assert.Equal("not woken: its folder cannot be used", decision.Note);
    }

    [Fact]
    public void ANewHitAfterAWokenOneIsEligibleAgain()
    {
        var newer = new LimitStop(Hit.AddHours(2), Reset.AddHours(2), "five_hour");

        var decision = LimitWake.Decide(Input(stop: newer, now: newer.ResetsAt.AddMinutes(5), wokenHit: Hit));

        Assert.Equal(LimitWakeAction.WakeNow, decision.Action);
    }

    [Fact]
    public void AClockEarlierThanTheHitDoesNothingAndWaits()
    {
        var decision = LimitWake.Decide(Input(now: Hit.AddMinutes(-30)));

        Assert.Equal(LimitWakeAction.Wait, decision.Action);
    }

    [Fact]
    public void ExactlyTheMarginAfterTheResetWakes()
    {
        var decision = LimitWake.Decide(Input(now: LimitReset.StartFor(Reset), folder: Usable));

        Assert.Equal(LimitWakeAction.WakeNow, decision.Action);
    }

    [Fact]
    public void OneSecondBeforeTheMarginWaits()
    {
        var decision = LimitWake.Decide(Input(now: LimitReset.StartFor(Reset).AddSeconds(-1), folder: Usable));

        Assert.Equal(LimitWakeAction.Wait, decision.Action);
    }

    [Fact]
    public void ABeforeTheMarginAListedAliveSessionOnlyWaits()
    {
        // The time row comes first: before the margin a listed-alive session is only a wait, and gets no notice yet.
        var decision = LimitWake.Decide(Input(now: Reset.AddMinutes(1), listing: WakeListing.Alive));

        Assert.Equal(LimitWakeAction.Wait, decision.Action);
    }

    [Fact]
    public void TheWakeCountIsTheWakesOfTheDayBeforeNow()
    {
        var times = new[] { AfterReset.AddHours(-1), AfterReset.AddHours(-23), AfterReset.AddHours(-24) };

        // One hour and 23 hours are inside the day; exactly 24 hours is not.
        Assert.Equal(2, LimitWake.WakesInDay(times, AfterReset));
        Assert.Equal(3, LimitWake.WithWake(times, AfterReset).Count);
    }

    [Fact]
    public void AWakeRecordKeepsOnlyTheDayAndAddsTheNewWake()
    {
        var times = new[] { AfterReset.AddHours(-1), AfterReset.AddHours(-25) };

        var kept = LimitWake.WithWake(times, AfterReset);

        Assert.Equal([AfterReset.AddHours(-1), AfterReset], kept);
    }

    [Fact]
    public void TheFixedLineIsTheOwnersWording()
    {
        Assert.Equal("The usage limit has reset. Please continue where you left off.", LimitWake.Line);
    }
}

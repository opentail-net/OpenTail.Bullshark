using OpenTail.Bullshark.Core.Git;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Git;

/// <summary>Reliability batch R8: a merge check that found a problem is made again at most every five minutes, and not for an unchanged HEAD within 30 minutes of its run.</summary>
public sealed class MergeRecheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private const string HeadA = "1111111111111111111111111111111111111111";
    private const string HeadB = "2222222222222222222222222222222222222222";

    /// <summary>A problem that was tried and run at <paramref name="ranAt"/>, with the HEAD it ran on.</summary>
    private static ProblemMemory Memory(DateTimeOffset ranAt, string? head = HeadA, DateTimeOffset? triedAt = null) =>
        new(triedAt ?? ranAt, ranAt, head);

    [Fact]
    public void WithNoMemoryAnAttemptMayStart()
    {
        Assert.True(MergeRecheck.MayTry(null, T0));
    }

    [Fact]
    public void AnAttemptWithinFiveMinutesOfTheLastIsNotMade()
    {
        var last = Memory(T0);

        Assert.False(MergeRecheck.MayTry(last, T0.AddMinutes(1)));
        Assert.False(MergeRecheck.MayTry(last, T0.AddMinutes(4).AddSeconds(59)));
    }

    [Fact]
    public void AnAttemptFiveMinutesAfterTheLastMayStart()
    {
        var last = Memory(T0);

        Assert.True(MergeRecheck.MayTry(last, T0.AddMinutes(5)));
    }

    [Fact]
    public void AClockThatWentBackMayTry()
    {
        Assert.True(MergeRecheck.MayTry(Memory(T0), T0.AddMinutes(-3)));
    }

    [Fact]
    public void AnUnchangedHeadWithinThirtyMinutesOfTheRunIsSkipped()
    {
        var last = Memory(T0);

        Assert.True(MergeRecheck.SkipForSameHead(last, HeadA, T0.AddMinutes(5)));
        Assert.True(MergeRecheck.SkipForSameHead(last, HeadA, T0.AddMinutes(29)));
    }

    [Fact]
    public void AnUnchangedHeadAfterThirtyMinutesIsRun()
    {
        Assert.False(MergeRecheck.SkipForSameHead(Memory(T0), HeadA, T0.AddMinutes(30)));
    }

    [Fact]
    public void AMovedHeadIsRunAtOnceWhenTheFloorHasPassed()
    {
        var last = Memory(T0);

        Assert.False(MergeRecheck.SkipForSameHead(last, HeadB, T0.AddMinutes(5)));
        Assert.True(MergeRecheck.MayTry(last, T0.AddMinutes(5)));
    }

    [Fact]
    public void AHeadThatCannotBeReadIsNeverTheSame()
    {
        Assert.False(MergeRecheck.SkipForSameHead(Memory(T0), null, T0.AddMinutes(5)));
    }

    [Fact]
    public void ANoHeadMemoryIsNeverSkipped()
    {
        Assert.False(MergeRecheck.SkipForSameHead(Memory(T0, head: null), HeadA, T0.AddMinutes(5)));
    }

    [Fact]
    public void SkippedAttemptsDoNotMoveTheTimeOfTheRun()
    {
        // A run at T0 on HEAD A. Attempts at 5, 10, ... 25 minutes are skipped for the same HEAD; the run's time stays T0, so the next run is at 30.
        var run = Memory(T0);
        var skipped = Enumerable.Range(1, 5).Select(i => T0.AddMinutes(i * 5)).ToList();

        Assert.All(skipped, at => Assert.True(MergeRecheck.SkipForSameHead(run, HeadA, at)));
        Assert.False(MergeRecheck.SkipForSameHead(run, HeadA, T0.AddMinutes(30)));
    }

    [Fact]
    public void TheFloorAndTheSameHeadIntervalAreTheIntervalsTheRuleSays()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), MergeRecheck.Floor);
        Assert.Equal(TimeSpan.FromMinutes(30), MergeRecheck.SameHeadInterval);
    }
}

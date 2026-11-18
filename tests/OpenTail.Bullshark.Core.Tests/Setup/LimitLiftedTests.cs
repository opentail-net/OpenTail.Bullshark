using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The limits that reset since the last look (toasts by event, 2026-11-18): a type that was in force and is not any more is listed by its label;
/// a type still in force, even with a later reset, is not. Pure: the limits are given, nothing is read.
/// </summary>
public sealed class LimitLiftedTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 5, 7, TimeSpan.Zero);

    [Fact]
    public void TypesInForceAreThoseWithAResetAfterNow()
    {
        var limits = new[]
        {
            new ActiveLimit("five_hour", Now.AddHours(2)),
            new ActiveLimit("weekly", Now.AddDays(3)),
            new ActiveLimit("opus_limit", Now.AddMinutes(-1)),
        };

        Assert.True(LimitStatus.TypesInForce(limits, Now).SetEquals(new[] { "five_hour", "weekly" }));
    }

    [Fact]
    public void ALimitThatResetIsListedByItsLabel()
    {
        var before = new HashSet<string> { "five_hour" };

        var lifted = LimitStatus.Lifted(before, [], Now);

        Assert.Equal(["5-hour"], lifted);
    }

    [Fact]
    public void ALimitStillInForceWithALaterResetIsNotListed()
    {
        var before = new HashSet<string> { "five_hour" };
        var limits = new[] { new ActiveLimit("five_hour", Now.AddHours(4)) };

        Assert.Empty(LimitStatus.Lifted(before, limits, Now));
    }

    [Fact]
    public void OnlyTheLimitThatResetIsListedWhenAnotherStaysIn()
    {
        var before = new HashSet<string> { "five_hour", "weekly" };
        var limits = new[] { new ActiveLimit("weekly", Now.AddDays(2)) };

        Assert.Equal(["5-hour"], LimitStatus.Lifted(before, limits, Now));
    }

    [Fact]
    public void ANewLimitIsNotListed()
    {
        var before = new HashSet<string>();
        var limits = new[] { new ActiveLimit("weekly", Now.AddDays(2)) };

        Assert.Empty(LimitStatus.Lifted(before, limits, Now));
    }

    [Fact]
    public void TwoTypesThatBothResetAreListedOnceEachByLabel()
    {
        var before = new HashSet<string> { "seven_day", "weekly", "five_hour" };

        // seven_day and weekly are both "weekly": the label is given once.
        Assert.Equal(["5-hour", "weekly"], LimitStatus.Lifted(before, [], Now));
    }
}

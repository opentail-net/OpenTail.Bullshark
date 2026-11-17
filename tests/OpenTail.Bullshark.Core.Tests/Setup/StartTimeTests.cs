using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>The start time the setup page types (gems item 5): HH:mm, the next time the clock shows it, and a clear reason when it is not a time.</summary>
public sealed class StartTimeTests
{
    /// <summary>Nine in the morning on this computer's clock, so the hours below read the same whatever the time zone is.</summary>
    private static readonly DateTimeOffset Nine = Local(2026, 11, 17, 9, 0);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Fact]
    public void BlankMeansNoStartTimeAndNoError()
    {
        var (blank, blankError) = StartTime.Parse("  ", Nine);
        var (absent, absentError) = StartTime.Parse(null, Nine);

        Assert.Null(blank);
        Assert.Null(blankError);
        Assert.Null(absent);
        Assert.Null(absentError);
    }

    [Fact]
    public void ATimeStillAheadIsToday()
    {
        var (at, error) = StartTime.Parse("14:30", Nine);

        Assert.Null(error);
        Assert.Equal(Local(2026, 11, 17, 14, 30), at);
    }

    [Fact]
    public void ATimeAlreadyPassedIsTomorrow()
    {
        var (at, error) = StartTime.Parse("08:00", Nine);

        Assert.Null(error);
        Assert.Equal(Local(2026, 11, 18, 8, 0), at);
    }

    [Fact]
    public void ASingleDigitHourIsAccepted()
    {
        var (at, error) = StartTime.Parse("9:05", Nine);

        Assert.Null(error);
        Assert.Equal(Local(2026, 11, 17, 9, 5), at);
    }

    /// <summary>Berlin, whose clocks change in March and October 2026. The fixed zone makes the tests the same on any computer.</summary>
    private static TimeZoneInfo Berlin() =>
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Berlin");

    [Fact]
    public void TheNextDayKeepsItsWallClockTimeWhenTheClocksGoForwardInBetween()
    {
        // Noon on 28 March, standard time. The next 03:30 is on 29 March, the day the clocks go forward, so it is summer time: 01:30 UTC.
        var now = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.FromHours(1));

        var (at, error) = StartTime.Parse("03:30", now, Berlin());

        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), at);
    }

    [Fact]
    public void TheNextDayKeepsItsWallClockTimeWhenTheClocksGoBackInBetween()
    {
        // Noon on 24 October, summer time. The next 03:30 is on 25 October, the day the clocks go back, so it is standard time: 02:30 UTC.
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));

        var (at, error) = StartTime.Parse("03:30", now, Berlin());

        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.Zero), at);
    }

    [Fact]
    public void ATimeStillAheadOnTheZonesClockIsToday()
    {
        // 23:00 UTC on 28 March is 00:00 on 29 March in Berlin, so 00:30 is still ahead today there, the 29th.
        var now = new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero);

        var (at, _) = StartTime.Parse("00:30", now, Berlin());

        Assert.Equal(new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero), at);
    }

    [Fact]
    public void AStartOnAnotherDayShowsItsDateAndAStartTodayShowsOnlyItsTime()
    {
        var zone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal("14:30", StartTime.Describe(new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero), now, zone));
        Assert.Equal("2026-11-18 08:00", StartTime.Describe(new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero), now, zone));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("12:60")]
    [InlineData("12")]
    [InlineData("ab:cd")]
    [InlineData("14:5")]
    [InlineData("-1:00")]
    public void ANonTimeGivesAReasonAndNoTime(string text)
    {
        var (at, error) = StartTime.Parse(text, Nine);

        Assert.Null(at);
        Assert.NotNull(error);
        Assert.Contains("HH:mm", error);
    }
}

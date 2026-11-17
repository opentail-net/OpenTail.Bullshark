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

using OpenTail.Bullshark.Core.Health;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Health;

public sealed class LoginExpiryTests
{
    // The warning as it appears in a session's terminal, with colour codes (finding F8). Wording copied from the screen.
    private const string Warning =
        "\u001b[38;2;255;193;7m⚠ Your login expires in 3 days \u001b[38;2;153;153;153m· run /login to renew\u001b[K";

    [Fact]
    public void ReadsDaysThroughColourCodes()
    {
        var remaining = LoginExpiry.Find(Warning);

        Assert.Equal(TimeSpan.FromDays(3), remaining);
    }

    [Fact]
    public void ReadsHours()
    {
        Assert.Equal(TimeSpan.FromHours(5), LoginExpiry.Find("Your login expires in 5 hours"));
    }

    [Fact]
    public void NoWarningMeansNull()
    {
        Assert.Null(LoginExpiry.Find("Claude Code v2.1.325  Haiku 5.5 · Claude Pro"));
    }

    [Fact]
    public void TheMostRecentWarningWins()
    {
        var text = "Your login expires in 3 days\n...later...\nYour login expires in 1 day";
        Assert.Equal(TimeSpan.FromDays(1), LoginExpiry.Find(text));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(1, true)]
    public void AlertsWhenTwoDaysOrLessRemain(int days, bool expected)
    {
        Assert.Equal(expected, LoginExpiry.IsExpiringSoon(TimeSpan.FromDays(days)));
    }
}

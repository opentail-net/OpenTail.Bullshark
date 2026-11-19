using OpenTail.Bullshark.Core.Health;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Health;

/// <summary>The AC sleep timeout read from powercfg's query output (night batch N3). The sample texts follow the English output of powercfg.</summary>
public sealed class PowerSettingsTests
{
    private const string Query =
        "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n" +
        "  Subgroup GUID: 238c9fa8-0aad-41ed-83f4-97be242c8f20  (Sleep)\r\n" +
        "    Power Setting GUID: 29f6c1db-86da-48c5-9fdb-f2b67b1f44da  (Sleep after)\r\n" +
        "      Possible Setting Index: 000\r\n" +
        "      Possible Setting Friendly Name: Never\r\n" +
        "    Current AC Power Setting Index: 0x00000708\r\n" +
        "    Current DC Power Setting Index: 0x00000384\r\n";

    [Fact]
    public void ReadsTheACTimeoutInMinutes()
    {
        // 0x708 is 1800 seconds: 30 minutes.
        Assert.Equal(30, PowerSettings.ParseSleepMinutes(Query));
    }

    [Fact]
    public void ZeroIsNeverAndIsZero()
    {
        var text = Query.Replace("0x00000708", "0x00000000");

        Assert.Equal(0, PowerSettings.ParseSleepMinutes(text));
    }

    [Theory]
    [InlineData("0x0000003c", 1)]
    [InlineData("0x0000001e", 1)]
    [InlineData("0x0000005a", 2)]
    [InlineData("0x00000e10", 60)]
    public void SecondsRoundUpToWholeMinutes(string hex, int minutes)
    {
        var text = Query.Replace("0x00000708", hex);

        Assert.Equal(minutes, PowerSettings.ParseSleepMinutes(text));
    }

    [Fact]
    public void AMissingLineIsNull()
    {
        Assert.Null(PowerSettings.ParseSleepMinutes("Power Scheme GUID: 381b4222\r\nCurrent DC Power Setting Index: 0x00000384\r\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Der Befehl ist nicht bekannt.")]
    public void EmptyOrUnknownTextIsNull(string? text)
    {
        Assert.Null(PowerSettings.ParseSleepMinutes(text));
    }

    [Fact]
    public void AHexValueThatIsTooLargeIsNullNotAnException()
    {
        var text = Query.Replace("0x00000708", "0xFFFFFFFFFFFFFFFFFFFF");

        Assert.Null(PowerSettings.ParseSleepMinutes(text));
    }

    [Fact]
    public void TheQueryArgumentsAreTheReadOnlyQuery()
    {
        Assert.Equal(["/query", "SCHEME_CURRENT", "SUB_SLEEP", "STANDBYIDLE"], PowerSettings.QueryArguments);
    }
}

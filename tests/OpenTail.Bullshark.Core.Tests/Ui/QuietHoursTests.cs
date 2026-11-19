using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>Quiet hours (night batch N4): the ui.json value, the window's edges, the midnight crossing, and the label.</summary>
public sealed class QuietHoursTests
{
    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    [Fact]
    public void AnObjectWithFromAndToIsAWindow()
    {
        var window = QuietHours.Parse(JsonNode.Parse("{\"from\":\"22:00\",\"to\":\"07:00\"}"));

        Assert.Equal(new QuietWindow(T(22), T(7)), window);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("\"off\"")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"from\":\"22:00\"}")]
    [InlineData("{\"from\":\"22:00\",\"to\":\"25:00\"}")]
    [InlineData("{\"from\":\"nine\",\"to\":\"07:00\"}")]
    [InlineData("{\"from\":22,\"to\":7}")]
    [InlineData("{\"from\":\"22:00\",\"to\":\"22:00\"}")]
    public void JunkAndEqualTimesAreOff(string? json)
    {
        var node = json is null ? null : JsonNode.Parse(json);

        Assert.Null(QuietHours.Parse(node));
    }

    [Theory]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    public void AWindowThatCrossesMidnightIsQuietFromFromUpToTo(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, QuietHours.IsQuiet(T(hour, minute), T(22), T(7)));
    }

    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]
    [InlineData(13, 30, true)]
    [InlineData(16, 59, true)]
    [InlineData(17, 0, false)]
    [InlineData(23, 0, false)]
    public void AWindowInsideOneDayIsQuietFromFromUpToTo(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, QuietHours.IsQuiet(T(hour, minute), T(9), T(17)));
    }

    [Fact]
    public void FromEqualToToIsNeverQuiet()
    {
        Assert.False(QuietHours.IsQuiet(T(22), T(22), T(22)));
        Assert.False(QuietHours.IsQuiet(T(3), T(22), T(22)));
    }

    [Fact]
    public void TheMidnightPresetsCrossTheRightWayRound()
    {
        Assert.True(QuietHours.IsQuiet(T(0, 0), T(0), T(9)));
        Assert.False(QuietHours.IsQuiet(T(9, 0), T(0), T(9)));
        Assert.Equal(3, QuietHours.Presets.Count);
    }

    [Fact]
    public void IsQuietAtUsesLocalTime()
    {
        var local = new DateTimeOffset(2026, 11, 19, 23, 30, 0, DateTimeOffset.Now.Offset);
        var window = new QuietWindow(T(22), T(7));

        Assert.True(QuietHours.IsQuietAt(window, local));
        Assert.False(QuietHours.IsQuietAt(null, local));
    }

    [Fact]
    public void LabelIsOffOrTheWindow()
    {
        Assert.Equal("off", QuietHours.Label(null));
        Assert.Equal("22:00-07:00", QuietHours.Label(new QuietWindow(T(22), T(7))));
        Assert.Equal("00:00-09:00", QuietHours.Label(new QuietWindow(T(0), T(9))));
    }

    [Fact]
    public void ToNodeWritesTheObjectOrNull()
    {
        var node = QuietHours.ToNode(new QuietWindow(T(23), T(8)));

        Assert.Equal("{\"from\":\"23:00\",\"to\":\"08:00\"}", node!.ToJsonString());
        Assert.Null(QuietHours.ToNode(null));
    }

    [Fact]
    public void ParseReadsWhatToNodeWrote()
    {
        var window = new QuietWindow(T(0), T(9));

        Assert.Equal(window, QuietHours.Parse(QuietHours.ToNode(window)));
    }
}

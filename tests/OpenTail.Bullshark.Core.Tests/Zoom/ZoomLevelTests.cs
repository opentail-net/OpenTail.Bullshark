using OpenTail.Bullshark.Core.Zoom;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Zoom;

public sealed class ZoomLevelTests
{
    [Fact]
    public void TheDefaultIsOneHundredAndItIsAStep()
    {
        Assert.Equal(100, ZoomLevel.Default);
        Assert.Equal(100, ZoomLevel.Nearest(ZoomLevel.Default));
        Assert.Equal(1.0, ZoomLevel.Scale(ZoomLevel.Default));
    }

    [Fact]
    public void UpAndDownMoveOneStepAndStopAtTheEnds()
    {
        Assert.Equal(110, ZoomLevel.Up(100));
        Assert.Equal(90, ZoomLevel.Down(100));
        Assert.Equal(300, ZoomLevel.Up(300));
        Assert.Equal(50, ZoomLevel.Down(50));
    }

    [Fact]
    public void AValueBetweenStepsIsTakenToTheNearestStep()
    {
        Assert.Equal(100, ZoomLevel.Nearest(105));
        Assert.Equal(110, ZoomLevel.Nearest(108));
        Assert.Equal(300, ZoomLevel.Nearest(900));
        Assert.Equal(50, ZoomLevel.Nearest(1));
        Assert.Equal(110, ZoomLevel.Up(105));
    }

    [Fact]
    public void ASliderPositionMapsToItsStep()
    {
        Assert.Equal(3, ZoomLevel.IndexOf(100));
        Assert.Equal(200, ZoomLevel.AtIndex(8));
        Assert.Equal(50, ZoomLevel.AtIndex(-4));
        Assert.Equal(300, ZoomLevel.AtIndex(99));
    }

    [Fact]
    public void ScaleIsTheStepAsAFactor()
    {
        Assert.Equal(2.0, ZoomLevel.Scale(200));
        Assert.Equal(0.5, ZoomLevel.Scale(50));
    }

    [Theory]
    [InlineData(0xBB, true, ZoomKey.In)]
    [InlineData(0x6B, true, ZoomKey.In)]
    [InlineData(0xBD, true, ZoomKey.Out)]
    [InlineData(0x6D, true, ZoomKey.Out)]
    [InlineData(0x30, true, ZoomKey.Reset)]
    [InlineData(0x60, true, ZoomKey.Reset)]
    [InlineData(0xBB, false, ZoomKey.None)]
    [InlineData(0x41, true, ZoomKey.None)]
    public void AVirtualKeyWithCtrlIsAZoomKey(int virtualKey, bool ctrl, ZoomKey expected)
    {
        Assert.Equal(expected, ZoomKeys.FromVirtualKey(virtualKey, ctrl));
    }
}

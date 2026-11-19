using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Cache;

namespace OpenTail.Bullshark.Core.Tests.Cache;

/// <summary>The context meter on a rail row (ctx 142k, the warning at 80 %) and the window setting it reads. Pure: no file is read.</summary>
public sealed class ContextMeterTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(-5, "")]
    [InlineData(1, "ctx 1")]
    [InlineData(999, "ctx 999")]
    [InlineData(1_000, "ctx 1k")]
    [InlineData(142_300, "ctx 142k")]
    [InlineData(999_999, "ctx 999k")]
    [InlineData(1_000_000, "ctx 1.0M")]
    [InlineData(1_250_000, "ctx 1.2M")]
    [InlineData(1_999_999, "ctx 1.9M")]
    public void TheFigureIsRoundedDownAndNamesTheUnit(long tokens, string text)
    {
        Assert.Equal(text, ContextMeter.Short(tokens));
    }

    [Fact]
    public void ASessionBiggerThanTheChosenWindowIsJudgedAgainstOneMillion()
    {
        Assert.Equal(200_000, ContextMeter.EffectiveWindow(150_000, 200_000));
        Assert.Equal(1_000_000, ContextMeter.EffectiveWindow(408_000, 200_000));
        Assert.Equal(2_000_000, ContextMeter.EffectiveWindow(1_500_000, 2_000_000));
        Assert.False(ContextMeter.IsHigh(408_000, ContextMeter.EffectiveWindow(408_000, 200_000)));
        Assert.True(ContextMeter.IsHigh(917_000, ContextMeter.EffectiveWindow(917_000, 200_000)));
    }

    [Fact]
    public void TheWarningIsExactlyAtEightyPercentOfTheWindow()
    {
        Assert.False(ContextMeter.IsHigh(159_999, 200_000));
        Assert.True(ContextMeter.IsHigh(160_000, 200_000));
        Assert.True(ContextMeter.IsHigh(200_000, 200_000));
    }

    [Fact]
    public void TheWarningHasNoRoundingAtAnOddWindow()
    {
        // 80 % of 50,001 is 40,000.8: 40,000 tokens are below it, 40,001 are at it.
        Assert.False(ContextMeter.IsHigh(40_000, 50_001));
        Assert.True(ContextMeter.IsHigh(40_001, 50_001));
    }

    [Fact]
    public void NoWarningWithoutAWindow()
    {
        Assert.False(ContextMeter.IsHigh(10_000, 0));
        Assert.False(ContextMeter.IsHigh(10_000, -1));
    }

    [Fact]
    public void AMissingValueGivesTheAssumedDefault()
    {
        Assert.Equal(200_000, ContextMeter.WindowFrom(null));
    }

    [Theory]
    [InlineData("\"1000000\"")]
    [InlineData("\"soon\"")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("0")]
    [InlineData("-5")]
    public void JunkGivesTheAssumedDefault(string json)
    {
        var node = JsonNode.Parse(json);

        Assert.Equal(200_000, ContextMeter.WindowFrom(node));
    }

    [Theory]
    [InlineData(10, 50_000)]
    [InlineData(49_999, 50_000)]
    [InlineData(1_000_000, 1_000_000)]
    [InlineData(9_000_000, 5_000_000)]
    public void ANumberIsClampedToTheRange(long stored, long expected)
    {
        Assert.Equal(expected, ContextMeter.WindowFrom(JsonValue.Create(stored)));
    }

    [Fact]
    public void AFractionIsRoundedAndTheNodeIsNotChanged()
    {
        var node = JsonNode.Parse("250000.4");

        Assert.Equal(250_000, ContextMeter.WindowFrom(node));
        Assert.Equal("250000.4", node!.ToJsonString());
    }

    [Fact]
    public void TheTwoPresetsAreTheAssumedDefaultAndOneMillion()
    {
        Assert.Equal(new long[] { 200_000, 1_000_000 }, ContextMeter.Presets);
    }

    [Fact]
    public void ThePresetsShowTheirLabelsAndTheDefaultIsLabelledAssumed()
    {
        var small = ContextMeter.ChoiceOf(JsonValue.Create(200_000L));
        var large = ContextMeter.ChoiceOf(JsonValue.Create(1_000_000L));

        Assert.Equal(new ContextWindowChoice(200_000, "200k (assumed)", IsCustom: false), small);
        Assert.Equal(new ContextWindowChoice(1_000_000, "1M", IsCustom: false), large);
    }

    [Fact]
    public void AMissingValueShowsTheDefaultPreset()
    {
        Assert.Equal(200_000, ContextMeter.ChoiceOf(null).Tokens);
        Assert.False(ContextMeter.ChoiceOf(null).IsCustom);
    }

    [Fact]
    public void AnyOtherNumberShowsAsCustomWithTheWindowTheEngineUses()
    {
        var choice = ContextMeter.ChoiceOf(JsonValue.Create(300_000L));

        Assert.Equal("Custom (300,000)", choice.Label);
        Assert.True(choice.IsCustom);
        Assert.Equal(300_000, choice.Tokens);
    }

    [Fact]
    public void AClampedCustomValueShowsTheClampedWindow()
    {
        var choice = ContextMeter.ChoiceOf(JsonValue.Create(10L));

        Assert.Equal(50_000, choice.Tokens);
        Assert.Equal("Custom (50,000)", choice.Label);
    }

    [Fact]
    public void ReadingACustomValueDoesNotChangeIt()
    {
        var node = JsonValue.Create(300_000L);

        ContextMeter.ChoiceOf(node);

        Assert.Equal("300000", node!.ToJsonString());
    }
}

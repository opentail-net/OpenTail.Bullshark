using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The Settings page's re-alert choices (Insights and Settings plan, Naming): the presets, the default, Off, and a stored value that is not a
/// preset, which shows as "Custom (N min)" with the minutes the engine uses. Pure: no file is read or written.
/// </summary>
public sealed class EscalationChoicesTests
{
    [Fact]
    public void ThePresetsAreOffFiveFifteenThirtyAndSixtyMinutes()
    {
        Assert.Equal(new[] { 0, 5, 15, 30, 60 }, EscalationChoices.Presets);
    }

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(5, "5 min")]
    [InlineData(15, "15 min")]
    [InlineData(30, "30 min")]
    [InlineData(60, "60 min")]
    public void EachPresetShowsItsOwnChoice(int minutes, string label)
    {
        var choice = EscalationChoices.Of(JsonValue.Create(minutes));

        Assert.Equal(minutes, choice.Minutes);
        Assert.Equal(label, choice.Label);
        Assert.False(choice.IsCustom);
    }

    [Fact]
    public void AMissingValueShowsTheDefaultOfFifteenMinutes()
    {
        var choice = EscalationChoices.Of(null);

        Assert.Equal(15, choice.Minutes);
        Assert.False(choice.IsCustom);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("-5")]
    public void AValueThatIsNotANumberOrIsNegativeShowsTheDefault(string text)
    {
        var node = text.StartsWith('-') ? JsonValue.Create(-5) : JsonValue.Create(text);

        var choice = EscalationChoices.Of(node);

        Assert.Equal(15, choice.Minutes);
        Assert.False(choice.IsCustom);
    }

    [Fact]
    public void AValueThatIsNotAPresetShowsAsCustomWithTheMinutesTheEngineUses()
    {
        var node = JsonValue.Create(20);

        var choice = EscalationChoices.Of(node);

        Assert.Equal(20, choice.Minutes);
        Assert.Equal("Custom (20 min)", choice.Label);
        Assert.True(choice.IsCustom);
    }

    [Fact]
    public void AValueAboveTheEngineLimitShowsTheLimitTheEngineUses()
    {
        var choice = EscalationChoices.Of(JsonValue.Create(5000));

        Assert.Equal(1440, choice.Minutes);
        Assert.Equal("Custom (1440 min)", choice.Label);
    }

    [Fact]
    public void ReadingACustomValueDoesNotChangeIt()
    {
        var node = JsonValue.Create(20);

        EscalationChoices.Of(node);

        Assert.Equal("20", node.ToJsonString());
    }
}

using OpenTail.Bullshark.Core.Continue;

namespace OpenTail.Bullshark.Core.Tests.Continue;

public sealed class ContinueSettingTextTests
{
    [Theory]
    [InlineData("full", ContinueMode.Full)]
    [InlineData("lastTurns", ContinueMode.LastTurns)]
    [InlineData("summary", ContinueMode.Summary)]
    public void EachModeTextParsesToItsMode(string text, ContinueMode mode) =>
        Assert.Equal<ContinueMode?>(mode, ContinueModeText.Parse(text));

    [Theory]
    [InlineData("Full")]
    [InlineData("LastTurns")]
    [InlineData("lastturns")]
    [InlineData("last-turns")]
    [InlineData("SUMMARY")]
    [InlineData(" full")]
    [InlineData("")]
    [InlineData("tail")]
    [InlineData(null)]
    public void AnUnknownModeTextIsNull(string? text) =>
        Assert.Null(ContinueModeText.Parse(text));

    [Theory]
    [InlineData(ContinueMode.Full, "full")]
    [InlineData(ContinueMode.LastTurns, "lastTurns")]
    [InlineData(ContinueMode.Summary, "summary")]
    public void EachModeWritesItsSettingText(ContinueMode mode, string text) =>
        Assert.Equal(text, ContinueModeText.ToText(mode));

    [Theory]
    [InlineData(ContinueMode.Full)]
    [InlineData(ContinueMode.LastTurns)]
    [InlineData(ContinueMode.Summary)]
    public void AModeTextRoundTrips(ContinueMode mode) =>
        Assert.Equal<ContinueMode?>(mode, ContinueModeText.Parse(ContinueModeText.ToText(mode)));

    [Theory]
    [InlineData("provider", ContinueScope.Provider)]
    [InlineData("all", ContinueScope.All)]
    public void EachScopeTextParsesToItsScope(string text, ContinueScope scope) =>
        Assert.Equal<ContinueScope?>(scope, ContinueScopeText.Parse(text));

    [Theory]
    [InlineData("Provider")]
    [InlineData("All")]
    [InlineData("everything")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownScopeTextIsNull(string? text) =>
        Assert.Null(ContinueScopeText.Parse(text));

    [Theory]
    [InlineData(ContinueScope.Provider)]
    [InlineData(ContinueScope.All)]
    public void AScopeTextRoundTrips(ContinueScope scope) =>
        Assert.Equal<ContinueScope?>(scope, ContinueScopeText.Parse(ContinueScopeText.ToText(scope)));
}

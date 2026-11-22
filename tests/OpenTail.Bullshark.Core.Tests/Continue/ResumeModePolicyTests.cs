using OpenTail.Bullshark.Core.Continue;

namespace OpenTail.Bullshark.Core.Tests.Continue;

public sealed class ResumeModePolicyTests
{
    [Fact]
    public void TheSessionsOwnModeWinsOverTheGlobalSetting() =>
        Assert.Equal<ContinueMode?>(ContinueMode.Summary, ResumeModePolicy.Resolve(ContinueMode.Summary, ContinueMode.LastTurns));

    [Fact]
    public void TheGlobalSettingIsUsedWhenTheSessionHasNoMode() =>
        Assert.Equal<ContinueMode?>(ContinueMode.Full, ResumeModePolicy.Resolve(null, ContinueMode.Full));

    [Fact]
    public void NullWhenNeitherIsSetLetsTheScreenUseItsOwnDefault() =>
        Assert.Null(ResumeModePolicy.Resolve(null, null));

    [Theory]
    [InlineData(ContinueMode.Full)]
    [InlineData(ContinueMode.LastTurns)]
    [InlineData(ContinueMode.Summary)]
    public void TheSessionsModeIsKeptWhateverTheGlobalSettingIs(ContinueMode sessionMode) =>
        Assert.Equal<ContinueMode?>(sessionMode, ResumeModePolicy.Resolve(sessionMode, ContinueMode.Full));

    [Fact]
    public void AnUnknownSettingTextFallsBackToTheScreensDefault() =>
        Assert.Null(ResumeModePolicy.Resolve(null, ContinueModeText.Parse("tail")));
}

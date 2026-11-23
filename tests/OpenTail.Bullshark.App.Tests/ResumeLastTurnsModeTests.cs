using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The old last-turns mode, read as Summary at the Zero level with the tail on (RS-D27), at each place a stored mode becomes a screen's start: the
/// setting text, the policy that resolves it, the shared start rule that the three resume screens use, and the cross-platform rule. Pure: no window.
/// The mode is kept as a text so old settings and marks still read.
/// </summary>
public sealed class ResumeLastTurnsModeTests
{
    [Fact]
    public void AnOldSavedLastTurnsSettingStillParsesAsLastTurns()
    {
        Assert.Equal(ContinueMode.LastTurns, ContinueModeText.Parse("lastTurns"));
    }

    [Fact]
    public void AStoredLastTurnsOpensTheScreenOnTheSummaryRadio()
    {
        Assert.Equal(ProviderResumeMode.Summary, ProviderResumeRules.StartMode(ContinueMode.LastTurns));
    }

    [Fact]
    public void AnOldSavedLastTurnsSettingReadsAsSummaryAtZeroWithTheTailOn()
    {
        var mode = ContinueModeText.Parse("lastTurns");

        Assert.Equal(ProviderResumeMode.Summary, ProviderResumeRules.StartMode(mode));
        Assert.True(ProviderResumeRules.OpensAtZeroWithTail(mode));
    }

    [Fact]
    public void AStoredLastTurnsResolvesAsLastTurnsThenReadsAsSummary()
    {
        // The policy keeps the stored text; the screen reads it (RS-D27).
        var resolved = ResumeModePolicy.Resolve(ContinueMode.LastTurns, ContinueMode.Full);

        Assert.Equal(ContinueMode.LastTurns, resolved);
        Assert.Equal(ProviderResumeMode.Summary, ProviderResumeRules.StartMode(resolved));
    }

    [Fact]
    public void OnlyAStoredLastTurnsOpensAtZeroWithTheTailOn()
    {
        Assert.True(ProviderResumeRules.OpensAtZeroWithTail(ContinueMode.LastTurns));
        Assert.False(ProviderResumeRules.OpensAtZeroWithTail(ContinueMode.Summary));
        Assert.False(ProviderResumeRules.OpensAtZeroWithTail(ContinueMode.Full));
        Assert.False(ProviderResumeRules.OpensAtZeroWithTail(null));
    }

    [Fact]
    public void TheTurnsOfAStoredLastTurnsAreTheDefaultTenWhenNothingIsRemembered()
    {
        Assert.Equal(10, ProviderResumeRules.DefaultTurns);
        Assert.Equal(10, ProviderResumeRules.StartTurns(null));
    }

    [Fact]
    public void TheRememberedTurnsAreKeptWhenTheyAreAValidCount()
    {
        Assert.Equal(4, ProviderResumeRules.StartTurns(4));
        Assert.Equal(0, ProviderResumeRules.StartTurns(0));
        Assert.Equal(ProviderResumeRules.MaxTurns, ProviderResumeRules.StartTurns(ProviderResumeRules.MaxTurns));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(201)]
    public void AnInvalidRememberedCountGivesTheDefault(int remembered) =>
        Assert.Equal(ProviderResumeRules.DefaultTurns, ProviderResumeRules.StartTurns(remembered));

    [Fact]
    public void AStoredLastTurnsMayStartInAnotherPlatformAsASummary()
    {
        // The owner's answer (RS-O1): yes, as a summary at the Zero level with the tail. The screen's mode is Summary, so the cross rule allows it.
        Assert.True(ResumeTarget.Allowed(ProviderId.Claude, ContinueMode.Summary, ProviderId.Codex));
        Assert.Null(ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.Summary, ProviderId.Codex));
    }
}

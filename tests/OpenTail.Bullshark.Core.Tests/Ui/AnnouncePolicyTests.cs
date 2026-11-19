using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// The announcements (perf and testability batch, Q6 d): "notify" off silences the sound, the flash and every toast; quiet hours log only; a window in
/// front gets no toast; each kind of toast has its own switch.
/// </summary>
public sealed class AnnouncePolicyTests
{
    private static PassSettings Settings(string json) => PassSettings.From((JsonObject)JsonNode.Parse(json)!);

    private static readonly PassSettings Everything = Settings("{}");

    [Fact]
    public void NotifyOffSilencesTheSoundTheFlashAndTheToast()
    {
        var plan = AnnouncePolicy.ForWaits(Settings("{\"notify\":false}"), windowActive: false, quiet: false);

        Assert.Equal(new WaitAnnouncement(Sounds: false, Flashes: false, Toasts: false, LogsOnly: false), plan);
    }

    [Fact]
    public void QuietHoursLogTheWaitsAndNothingSoundsOrPopsUp()
    {
        var plan = AnnouncePolicy.ForWaits(Everything, windowActive: false, quiet: true);

        Assert.Equal(new WaitAnnouncement(Sounds: false, Flashes: false, Toasts: false, LogsOnly: true), plan);
    }

    [Fact]
    public void AWindowInFrontHearsAndFlashesButGetsNoToast()
    {
        var plan = AnnouncePolicy.ForWaits(Everything, windowActive: true, quiet: false);

        Assert.True(plan.Sounds);
        Assert.True(plan.Flashes);
        Assert.False(plan.Toasts);
        Assert.False(plan.LogsOnly);
    }

    [Fact]
    public void AWindowInTheBackgroundGetsTheToastWhenToastsAreOn()
    {
        Assert.True(AnnouncePolicy.ForWaits(Everything, windowActive: false, quiet: false).Toasts);
        Assert.False(AnnouncePolicy.ForWaits(Settings("{\"toast\":false}"), windowActive: false, quiet: false).Toasts);
    }

    [Fact]
    public void EachKindOfToastHasItsOwnSwitch()
    {
        var blockedOff = Settings("{\"toastEvents\":{\"blocked\":false}}");

        Assert.False(AnnouncePolicy.WaitToastAllowed(blockedOff, AttentionReason.Blocked));
        Assert.True(AnnouncePolicy.WaitToastAllowed(blockedOff, AttentionReason.RestartLoop));
        Assert.True(AnnouncePolicy.WaitToastAllowed(blockedOff, AttentionReason.Overlap));
    }

    [Fact]
    public void ASilentToastNeedsItsSwitchTheWindowInTheBackgroundAndNoQuietHours()
    {
        Assert.True(AnnouncePolicy.SilentToastShown(Everything, ToastCategory.Ready, windowActive: false, quiet: false));
        Assert.False(AnnouncePolicy.SilentToastShown(Everything, ToastCategory.Ready, windowActive: true, quiet: false));
        Assert.False(AnnouncePolicy.SilentToastShown(Everything, ToastCategory.Ready, windowActive: false, quiet: true));
        Assert.False(AnnouncePolicy.SilentToastShown(Settings("{\"toastEvents\":{\"ready\":false}}"), ToastCategory.Ready, false, false));
        Assert.True(AnnouncePolicy.SilentToastShown(Settings("{\"toastEvents\":{\"ready\":false}}"), ToastCategory.LimitReset, false, false));
        Assert.False(AnnouncePolicy.SilentToastShown(Settings("{\"notify\":false}"), ToastCategory.LimitReset, false, false));
    }

    [Fact]
    public void TheProblemToastFollowsTheSameSwitches()
    {
        Assert.True(AnnouncePolicy.ProblemToastShown(Everything, windowActive: false, quiet: false));
        Assert.False(AnnouncePolicy.ProblemToastShown(Settings("{\"toastEvents\":{\"problems\":false}}"), false, false));
        Assert.False(AnnouncePolicy.ProblemToastShown(Everything, windowActive: true, quiet: false));
        Assert.False(AnnouncePolicy.ProblemToastShown(Everything, windowActive: false, quiet: true));
    }
}

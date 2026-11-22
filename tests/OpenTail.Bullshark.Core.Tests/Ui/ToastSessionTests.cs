using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// A banner carries the session it is about (behaviour batch, R2, rule 4): a click selects that session. The session id travels with the message and
/// is the first item's, the one the title and the body name.
/// </summary>
public sealed class ToastSessionTests
{
    private static AttentionItem Item(string name, string? sessionId, AttentionReason reason = AttentionReason.Blocked) =>
        new(name, sessionId, reason, "detail", DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheToastCarriesTheSessionOfItsFirstItem()
    {
        var message = ToastText.Build([Item("ovc-coder", "s-1"), Item("ovc-merge", "s-2", AttentionReason.Failed)], []);

        Assert.NotNull(message);
        Assert.Equal("s-1", message.SessionId);
        Assert.Equal("ovc-coder · blocked, and 1 more", message.Body);
    }

    [Fact]
    public void TheSessionIsTheFirstPermittedItemsWhenTheFilterDropsTheFirst()
    {
        var message = ToastText.Build(
            [Item("ovc-coder", "s-1"), Item("ovc-merge", "s-2", AttentionReason.Failed)],
            [],
            reason => reason != AttentionReason.Blocked);

        Assert.NotNull(message);
        Assert.Equal("s-2", message.SessionId);
    }

    [Fact]
    public void ARepeatAloneCarriesItsSession()
    {
        var message = ToastText.Build([], [new ReAlert(Item("ovc-coder", "s-7"), 1)]);

        Assert.Equal("s-7", message?.SessionId);
        Assert.Equal("Still waiting", message?.Title);
    }

    [Fact]
    public void AnItemWithNoSessionGivesAToastWithNoSession()
    {
        var message = ToastText.Build([Item("ovc-start", null, AttentionReason.StartFailed)], []);

        Assert.NotNull(message);
        Assert.Null(message.SessionId);
    }

    [Fact]
    public void TheReadyAndLimitWakeToastsCarryTheirSessionAndNoSessionByDefault()
    {
        Assert.Equal("s-9", ToastText.Ready(["ovc-coder"], "s-9")?.SessionId);
        Assert.Null(ToastText.Ready(["ovc-coder"])?.SessionId);
        Assert.Equal("s-4", ToastText.LimitWake("ovc-coder", "s-4")?.SessionId);
        Assert.Null(ToastText.LimitWake("ovc-coder")?.SessionId);
    }

    [Fact]
    public void TheMessageWithoutASessionIsEqualToThePreviousShape()
    {
        Assert.Equal(new ToastMessage("Ready for review", "ovc-coder"), ToastText.Ready(["ovc-coder"]));
    }
}

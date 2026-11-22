using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>The toast words (toasts-2026-11-18): the session name and the reason, the title for a new wait or a re-alert, and nothing else.</summary>
public sealed class ToastTextTests
{
    private static AttentionItem Item(string name, AttentionReason reason = AttentionReason.Blocked, string detail = "waiting for input") =>
        new(name, "session-" + name, reason, detail, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ASingleNewWaitNamesTheSessionAndItsReason()
    {
        var message = ToastText.Build([Item("ovc-coder")], []);

        Assert.Equal(new ToastMessage("Needs you", "ovc-coder · blocked", "session-ovc-coder"), message);
    }

    [Fact]
    public void SeveralSessionsNameTheFirstAndCountTheRest()
    {
        var message = ToastText.Build([Item("ovc-coder"), Item("ovc-review"), Item("ovc-merge")], []);

        Assert.Equal(new ToastMessage("Needs you", "ovc-coder · blocked, and 2 more", "session-ovc-coder"), message);
    }

    [Fact]
    public void ASessionWithTwoReasonsIsCountedOnce()
    {
        // A session can wait for a reason and also share a file (overlap). It is one session, so no "and 1 more".
        var message = ToastText.Build([Item("ovc-coder"), Item("ovc-coder", AttentionReason.Overlap)], []);

        Assert.Equal(new ToastMessage("Needs you", "ovc-coder · blocked", "session-ovc-coder"), message);
    }

    [Fact]
    public void ARealertIsStillWaitingWithTheSameBodyStyle()
    {
        var message = ToastText.Build([], [new ReAlert(Item("ovc-coder"), 1)]);

        Assert.Equal(new ToastMessage("Still waiting", "ovc-coder · blocked", "session-ovc-coder"), message);
    }

    [Fact]
    public void ANewWaitWinsTheTitleOverARealertInTheSameBatch()
    {
        var message = ToastText.Build([Item("ovc-merge", AttentionReason.Stopped)], [new ReAlert(Item("ovc-coder"), 2)]);

        Assert.Equal(new ToastMessage("Needs you", "ovc-merge · stopped, and 1 more", "session-ovc-merge"), message);
    }

    [Fact]
    public void ARealertForTheSameSessionAsANewWaitIsNotCountedTwice()
    {
        var message = ToastText.Build([Item("ovc-coder")], [new ReAlert(Item("ovc-coder"), 1)]);

        Assert.Equal(new ToastMessage("Needs you", "ovc-coder · blocked", "session-ovc-coder"), message);
    }

    [Fact]
    public void NothingToAnnounceGivesNoToast()
    {
        Assert.Null(ToastText.Build([], []));
    }

    [Theory]
    [InlineData(AttentionReason.Blocked, "blocked")]
    [InlineData(AttentionReason.RestartLoop, "restart loop")]
    [InlineData(AttentionReason.StartFailed, "start failed")]
    [InlineData(AttentionReason.Stopped, "stopped")]
    [InlineData(AttentionReason.Failed, "failed")]
    [InlineData(AttentionReason.Overlap, "shares a file with another session")]
    [InlineData(AttentionReason.Conflict, "conflicts with the default branch")]
    [InlineData(AttentionReason.LimitReached, "run limit reached")]
    [InlineData(AttentionReason.NameTaken, "name taken by another session")]
    [InlineData(AttentionReason.Silent, "no output for a while")]
    public void EachReasonHasItsOwnWords(AttentionReason reason, string words)
    {
        Assert.Equal(words, ToastText.ReasonText(reason));
        Assert.Equal(words, ToastText.Build([Item("ovc-coder", reason)], [])!.Body.Substring("ovc-coder · ".Length));
    }

    [Fact]
    public void EveryReasonIsCoveredByTheTable()
    {
        // A reason added to the enum must get its own words in ReasonText, not the fallback. EachReasonHasItsOwnWords pins the words.
        foreach (var reason in Enum.GetValues<AttentionReason>())
            Assert.NotEqual("needs you", ToastText.ReasonText(reason));
    }

    [Fact]
    public void ALongNameIsCutToSixtyCharactersWithAnEllipsis()
    {
        var name = new string('x', 100);

        var message = ToastText.Build([Item(name)], []);

        var shown = message!.Body[..^" · blocked".Length];
        Assert.Equal(ToastText.MaxNameLength, shown.Length);
        Assert.EndsWith("…", shown, StringComparison.Ordinal);
        Assert.Equal(new string('x', 59) + "…", shown);
    }

    [Fact]
    public void ANameOfExactlySixtyCharactersIsShownWhole()
    {
        var name = new string('y', ToastText.MaxNameLength);

        Assert.Equal(name, ToastText.ShortName(name));
    }

    [Fact]
    public void TheDetailOfAnItemIsNeverShown()
    {
        // A permission prompt can carry a command or a path. The toast says the reason only.
        var secret = Item("ovc-coder", AttentionReason.Blocked, "waiting for Bash(rm -rf C:\\Secret\\customer.txt)");

        var message = ToastText.Build([secret], []);

        Assert.Equal(new ToastMessage("Needs you", "ovc-coder · blocked", "session-ovc-coder"), message);
        Assert.DoesNotContain("Secret", message!.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Bash", message.Body, StringComparison.Ordinal);
    }
}

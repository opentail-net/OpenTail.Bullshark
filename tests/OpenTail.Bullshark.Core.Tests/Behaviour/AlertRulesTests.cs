using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// The alert rules (behaviour batch, R2): which change makes a banner and a sound, for its kind, the window's focus, the selected session, the
/// re-alert interval and the kinds switched on. The attention list is not touched by these rules.
/// </summary>
public sealed class AlertRulesTests
{
    private static readonly AlertFocus Away = new(WindowFocused: false, SelectedSessionId: null);
    private static readonly AlertFocus InFront = new(WindowFocused: true, SelectedSessionId: null);

    private static AlertFocus AwaySelecting(string sessionId) => new(WindowFocused: false, SelectedSessionId: sessionId);

    private static AlertFocus InFrontSelecting(string sessionId) => new(WindowFocused: true, SelectedSessionId: sessionId);

    private static AlertsSettings Settings(int repeat = 0, FocusGate gate = FocusGate.Unfocused, IReadOnlySet<AlertGroup>? kinds = null) => new()
    {
        RepeatMinutes = repeat,
        FocusGate = gate,
        Kinds = kinds ?? new HashSet<AlertGroup> { AlertGroup.NeedsInput, AlertGroup.Errored },
    };

    private static AttentionItem Item(string name, string? sessionId, AttentionReason reason) =>
        new(name, sessionId, reason, "detail", DateTimeOffset.UnixEpoch);

    [Fact]
    public void KindOfMapsEachReason()
    {
        Assert.Equal(AlertGroup.NeedsInput, AlertRules.KindOf(AttentionReason.Blocked));
        foreach (var reason in new[] { AttentionReason.RestartLoop, AttentionReason.StartFailed, AttentionReason.Stopped, AttentionReason.Failed, AttentionReason.NameTaken })
            Assert.Equal(AlertGroup.Errored, AlertRules.KindOf(reason));
        foreach (var reason in new[] { AttentionReason.Overlap, AttentionReason.Conflict, AttentionReason.LimitReached, AttentionReason.Silent, AttentionReason.UsageLimit })
            Assert.Equal(AlertGroup.Notice, AlertRules.KindOf(reason));
    }

    [Fact]
    public void TheDefaultsAlertForNeedsInputAndErroredAwayFromTheWindowOnly()
    {
        var defaults = BehaviourSettings.Defaults.Alerts;

        Assert.True(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, Away, "s1", defaults));
        Assert.True(AlertRules.Shows(AlertGroup.Errored, AlertTrigger.Change, Away, "s1", defaults));
        Assert.False(AlertRules.Shows(AlertGroup.Finished, AlertTrigger.Change, Away, "s1", defaults));
        Assert.True(AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Change, Away, "s1", defaults));
    }

    [Fact]
    public void AFocusedWindowGetsNoBannerUnderTheUnfocusedGate()
    {
        var alerts = Settings(gate: FocusGate.Unfocused, kinds: new HashSet<AlertGroup> { AlertGroup.NeedsInput, AlertGroup.Errored, AlertGroup.Finished });
        Assert.False(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, InFront, "s1", alerts));
        Assert.False(AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Change, InFront, null, alerts));
        Assert.False(AlertRules.Shows(AlertGroup.Finished, AlertTrigger.Change, InFrontSelecting("s2"), "s1", alerts));
    }

    [Fact]
    public void UnderTheUnfocusedOrUnselectedGateAFocusedWindowStillHearsOtherSessions()
    {
        var alerts = Settings(gate: FocusGate.UnfocusedOrUnselected);
        Assert.True(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, InFrontSelecting("s2"), "s1", alerts));
        Assert.False(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, InFrontSelecting("s1"), "s1", alerts));
        Assert.True(AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Change, InFront, null, alerts));
    }

    [Fact]
    public void AnUnfocusedWindowHearsEverySessionUnderEitherGate()
    {
        foreach (var gate in new[] { FocusGate.Unfocused, FocusGate.UnfocusedOrUnselected })
        {
            var alerts = Settings(gate: gate);
            Assert.True(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, AwaySelecting("s1"), "s1", alerts));
            Assert.True(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, AwaySelecting("s1"), "s2", alerts));
            Assert.True(AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Change, AwaySelecting("s1"), null, alerts));
        }
    }

    [Fact]
    public void TheUnfocusedGateIgnoresTheSelection()
    {
        Assert.True(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, AwaySelecting("s1"), "s1", Settings(gate: FocusGate.Unfocused)));
    }

    [Fact]
    public void TheSelectionMatchesSessionIdsWhateverTheCase()
    {
        var alerts = Settings(gate: FocusGate.UnfocusedOrUnselected);

        Assert.False(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, InFrontSelecting("ABC-1"), "abc-1", alerts));
    }

    [Fact]
    public void ARepeatNeedsAnIntervalAndNoticesNeverRepeat()
    {
        Assert.False(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Repeat, Away, "s1", Settings(repeat: 0)));
        Assert.True(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Repeat, Away, "s1", Settings(repeat: 15)));
        Assert.False(AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Repeat, Away, "s1", Settings(repeat: 15)));
    }

    [Fact]
    public void AKindSwitchedOffDoesNotAlert()
    {
        var onlyErrored = Settings(kinds: new HashSet<AlertGroup> { AlertGroup.Errored });

        Assert.False(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, Away, "s1", onlyErrored));
        Assert.True(AlertRules.Shows(AlertGroup.Errored, AlertTrigger.Change, Away, "s1", onlyErrored));

        var none = Settings(kinds: new HashSet<AlertGroup>());
        Assert.False(AlertRules.Shows(AlertGroup.NeedsInput, AlertTrigger.Change, Away, "s1", none));
        Assert.False(AlertRules.Shows(AlertGroup.Errored, AlertTrigger.Change, Away, "s1", none));
        // A notice is not a kind of the setting, so it alerts whatever the kinds say.
        Assert.True(AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Change, Away, null, none));
    }

    [Fact]
    public void FinishedAlertsOnlyWhenSwitchedOn()
    {
        var on = Settings(kinds: new HashSet<AlertGroup> { AlertGroup.NeedsInput, AlertGroup.Errored, AlertGroup.Finished });

        Assert.True(AlertRules.Shows(AlertGroup.Finished, AlertTrigger.Change, Away, "s1", on));
        Assert.False(AlertRules.Shows(AlertGroup.Finished, AlertTrigger.Change, Away, "s1", Settings()));
    }

    [Fact]
    public void ChangesKeepsOnlyTheItemsThatAlertAndLeavesTheInputAlone()
    {
        IReadOnlyList<AttentionItem> gained =
        [
            Item("s1-blocked", "s1", AttentionReason.Blocked),
            Item("s2-failed", "s2", AttentionReason.Failed),
            Item("s3-overlap", "s3", AttentionReason.Overlap),
        ];

        var unfocused = AlertRules.Changes(gained, AwaySelecting("s1"), Settings());
        var held = AlertRules.Changes(gained, InFrontSelecting("s1"), Settings(gate: FocusGate.UnfocusedOrUnselected));
        var none = AlertRules.Changes(gained, InFront, Settings());

        Assert.Equal(new[] { "s1-blocked", "s2-failed", "s3-overlap" }, unfocused.Select(item => item.Name));
        Assert.Equal(new[] { "s2-failed", "s3-overlap" }, held.Select(item => item.Name));
        Assert.Empty(none);
        Assert.Equal(3, gained.Count);
    }

    [Fact]
    public void RepeatsNeedAnIntervalAndKeepTheDueOnesThatAlert()
    {
        IReadOnlyList<ReAlert> due = [new ReAlert(Item("s1", "s1", AttentionReason.Blocked), 1)];

        Assert.Single(AlertRules.Repeats(due, Away, Settings(repeat: 15)));
        Assert.Empty(AlertRules.Repeats(due, Away, Settings(repeat: 0)));
        Assert.Empty(AlertRules.Repeats(due, InFront, Settings(repeat: 15)));
    }

    [Fact]
    public void ReadyCoderAlertsOnlyWhenFinishedIsOnAndTheWindowIsAway()
    {
        IReadOnlyList<ReviewItem> ready = [new ReviewItem("coder", "s9", 2, DateTimeOffset.UnixEpoch)];
        var on = Settings(kinds: new HashSet<AlertGroup> { AlertGroup.NeedsInput, AlertGroup.Errored, AlertGroup.Finished });

        Assert.Empty(AlertRules.Ready(ready, Away, Settings()));
        Assert.Single(AlertRules.Ready(ready, Away, on));
        Assert.Empty(AlertRules.Ready(ready, InFront, on));
    }
}

using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The re-alerts for Needs-you items the owner has not answered (escalation-2026-11-18): the interval, the cap, acknowledge, snooze,
/// the spell rules, and the parsing of ui.json's escalateMinutes. Pure: the time is passed in, so no clock and no timer.
/// </summary>
public sealed class AttentionEscalationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Fifteen = TimeSpan.FromMinutes(15);

    private static AttentionItem Waiting(string name, AttentionReason reason = AttentionReason.Blocked) =>
        new(name, "s-" + name, reason, "detail", T0);

    private static AttentionItem Notice(string name, AttentionReason reason) => reason switch
    {
        AttentionReason.Overlap => new AttentionItem(name, "s-" + name, reason, "clash", T0, FilePath: @"C:\work\a.cs"),
        AttentionReason.Conflict => new AttentionItem(name, "s-" + name, reason, "conflict", T0, Files: ["a.cs"]),
        _ => new AttentionItem(name, "s-" + name, reason, "limit", T0),
    };

    private static IReadOnlyList<string> Names(IEnumerable<ReAlert> due) => due.Select(d => d.Item.Name).ToList();

    [Fact]
    public void NoReAlertBeforeTheIntervalAfterTheFirstSight()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };

        Assert.Empty(escalation.Update(list, T0, Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(14), Fifteen));
    }

    [Fact]
    public void AWaitingItemReAlertsAtTheIntervalThenAtTwiceAndThriceItThenNever()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };
        escalation.Update(list, T0, Fifteen);

        var first = Assert.Single(escalation.Update(list, T0.AddMinutes(15), Fifteen));
        var second = Assert.Single(escalation.Update(list, T0.AddMinutes(30), Fifteen));
        var third = Assert.Single(escalation.Update(list, T0.AddMinutes(45), Fifteen));

        Assert.Equal([1, 2, 3], new[] { first.Again, second.Again, third.Again });
        Assert.Empty(escalation.Update(list, T0.AddMinutes(60), Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(600), Fifteen));
    }

    [Fact]
    public void AnAcknowledgedItemNeverReAlertsForTheRestOfItsSpell()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };
        escalation.Update(list, T0, Fifteen);

        escalation.Acknowledge("coder");

        Assert.Empty(escalation.Update(list, T0.AddMinutes(15), Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(45), Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(120), Fifteen));
    }

    [Fact]
    public void ASnoozeDefersTheReAlertThenGivesOneAtTheEndAndTheCountRestarts()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };
        escalation.Update(list, T0, Fifteen);
        escalation.Snooze("coder", T0.AddMinutes(60));

        Assert.Empty(escalation.Update(list, T0.AddMinutes(15), Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(45), Fifteen));

        // The end of the snooze: one re-alert, which is the first of a new count.
        var atEnd = Assert.Single(escalation.Update(list, T0.AddMinutes(60), Fifteen));
        Assert.Equal(1, atEnd.Again);

        // Then the count runs as a spell does: at the interval, twice more, then never.
        Assert.Equal(2, Assert.Single(escalation.Update(list, T0.AddMinutes(75), Fifteen)).Again);
        Assert.Equal(3, Assert.Single(escalation.Update(list, T0.AddMinutes(90), Fifteen)).Again);
        Assert.Empty(escalation.Update(list, T0.AddMinutes(105), Fifteen));
    }

    [Fact]
    public void AnAcknowledgeAfterASnoozeCancelsIt()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };
        escalation.Update(list, T0, Fifteen);
        escalation.Snooze("coder", T0.AddMinutes(60));
        escalation.Acknowledge("coder");

        Assert.Empty(escalation.Update(list, T0.AddMinutes(60), Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(120), Fifteen));
    }

    [Fact]
    public void ASnoozeAfterAnAcknowledgeGivesItsReAlertAtTheEnd()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };
        escalation.Update(list, T0, Fifteen);
        escalation.Acknowledge("coder");
        escalation.Snooze("coder", T0.AddMinutes(60));

        Assert.Equal(1, Assert.Single(escalation.Update(list, T0.AddMinutes(60), Fifteen)).Again);
    }

    [Fact]
    public void AnItemThatLeavesTheListAndReturnsIsANewSpell()
    {
        var escalation = new AttentionEscalation();
        escalation.Update([Waiting("coder")], T0, Fifteen);
        escalation.Acknowledge("coder");

        // Gone at 10 minutes; back at 20. The acknowledgement is not carried over: the new spell starts at 20.
        Assert.Empty(escalation.Update([], T0.AddMinutes(10), Fifteen));
        Assert.Empty(escalation.Update([Waiting("coder")], T0.AddMinutes(20), Fifteen));

        // 15 minutes after 20, not after 0: the new spell's first re-alert is at 35.
        Assert.Empty(escalation.Update([Waiting("coder")], T0.AddMinutes(30), Fifteen));
        Assert.Equal(1, Assert.Single(escalation.Update([Waiting("coder")], T0.AddMinutes(35), Fifteen)).Again);
    }

    [Theory]
    [InlineData(AttentionReason.Overlap)]
    [InlineData(AttentionReason.Conflict)]
    [InlineData(AttentionReason.LimitReached)]
    public void NoticesNeverReAlert(AttentionReason reason)
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Notice("coder", reason) };

        Assert.Empty(escalation.Update(list, T0, Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(15), Fifteen));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(45), Fifteen));
    }

    [Theory]
    [InlineData(AttentionReason.Blocked, true)]
    [InlineData(AttentionReason.RestartLoop, true)]
    [InlineData(AttentionReason.StartFailed, true)]
    [InlineData(AttentionReason.Stopped, true)]
    [InlineData(AttentionReason.Failed, true)]
    [InlineData(AttentionReason.Overlap, false)]
    [InlineData(AttentionReason.Conflict, false)]
    [InlineData(AttentionReason.LimitReached, false)]
    public void OnlyTheReasonsTheOwnerMustActOnEscalate(AttentionReason reason, bool escalates)
    {
        Assert.Equal(escalates, AttentionEscalation.Escalates(Waiting("coder", reason)));
    }

    [Fact]
    public void AZeroIntervalSwitchesReAlertsOffAndTheSpellsAreKept()
    {
        var escalation = new AttentionEscalation();
        var list = new[] { Waiting("coder") };
        escalation.Update(list, T0, Fifteen);

        Assert.Empty(escalation.Update(list, T0.AddMinutes(15), TimeSpan.Zero));
        Assert.Empty(escalation.Update(list, T0.AddMinutes(30), TimeSpan.Zero));

        // Back on at 45: the spell started at 0, so the first re-alert is due now.
        Assert.Equal(1, Assert.Single(escalation.Update(list, T0.AddMinutes(45), Fifteen)).Again);
    }

    [Fact]
    public void TwoItemsEscalateIndependently()
    {
        var escalation = new AttentionEscalation();
        var both = new[] { Waiting("alpha"), Waiting("beta") };
        escalation.Update(both, T0, Fifteen);
        escalation.Acknowledge("alpha");

        Assert.Equal(["beta"], Names(escalation.Update(both, T0.AddMinutes(15), Fifteen)));
        Assert.Equal(["beta"], Names(escalation.Update(both, T0.AddMinutes(30), Fifteen)));
    }

    [Fact]
    public void AReasonChangeInsideASpellKeepsTheSpell()
    {
        var escalation = new AttentionEscalation();
        escalation.Update([Waiting("coder", AttentionReason.Blocked)], T0, Fifteen);

        // A change of reason at 5 minutes is not a new spell: the re-alert stays at 15, not 20.
        Assert.Empty(escalation.Update([Waiting("coder", AttentionReason.RestartLoop)], T0.AddMinutes(5), Fifteen));
        Assert.Equal(1, Assert.Single(escalation.Update([Waiting("coder", AttentionReason.RestartLoop)], T0.AddMinutes(15), Fifteen)).Again);
    }

    [Fact]
    public void AcknowledgingOrSnoozingAnUnknownNameDoesNothing()
    {
        var escalation = new AttentionEscalation();

        escalation.Acknowledge("nobody");
        escalation.Snooze("nobody", T0.AddHours(1));

        Assert.Empty(escalation.Update([Waiting("nobody")], T0, Fifteen));
        Assert.Equal(1, Assert.Single(escalation.Update([Waiting("nobody")], T0.AddMinutes(15), Fifteen)).Again);
    }

    [Fact]
    public void AMissingOrUnusableIntervalIsFifteenMinutes()
    {
        Assert.Equal(Fifteen, AttentionEscalation.IntervalOf(null));
        Assert.Equal(Fifteen, AttentionEscalation.IntervalOf(JsonValue.Create("soon")));
        Assert.Equal(Fifteen, AttentionEscalation.IntervalOf(JsonValue.Create(true)));
        Assert.Equal(Fifteen, AttentionEscalation.IntervalOf(JsonValue.Create(-5)));
        Assert.Equal(Fifteen, AttentionEscalation.IntervalOf(JsonNode.Parse("[15]")));
    }

    [Fact]
    public void ZeroIsOffAndOtherNumbersAreClampedToOneDayAtMost()
    {
        Assert.Equal(TimeSpan.Zero, AttentionEscalation.IntervalOf(JsonValue.Create(0)));
        Assert.Equal(TimeSpan.FromMinutes(30), AttentionEscalation.IntervalOf(JsonValue.Create(30)));
        Assert.Equal(TimeSpan.FromMinutes(1440), AttentionEscalation.IntervalOf(JsonValue.Create(1440)));
        Assert.Equal(TimeSpan.FromMinutes(1440), AttentionEscalation.IntervalOf(JsonValue.Create(5000)));
        Assert.Equal(TimeSpan.FromMinutes(1), AttentionEscalation.IntervalOf(JsonValue.Create(0.2)));
        Assert.Equal(TimeSpan.FromMinutes(15), AttentionEscalation.IntervalOf(JsonNode.Parse("15")));
    }
}

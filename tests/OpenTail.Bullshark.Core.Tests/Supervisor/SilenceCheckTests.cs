using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The silent-working notice (lifecycle batch L3): which working sessions are flagged, the setting that sets the threshold and the Settings
/// choices for it, and how the notice reaches the Needs-you list. Synthetic names and ids only.
/// </summary>
public sealed class SilenceCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FortyFive = TimeSpan.FromMinutes(45);
    private const string SessionA = "11111111-2222-3333-4444-555555555555";

    private static (string Name, string SessionId, string State, DateTimeOffset? LastRequest) Live(
        string name, string state, DateTimeOffset? lastRequest) =>
        (name, SessionA, state, lastRequest);

    // ---- Find ----

    [Fact]
    public void AWorkingSessionWithNoRequestForTheThresholdIsFlagged()
    {
        var flagged = SilenceCheck.Find([Live("coder", "working", Now.AddMinutes(-50))], Now, FortyFive);

        Assert.Equal(["coder"], flagged);
    }

    [Fact]
    public void AWorkingSessionThatRequestedRecentlyIsNotFlagged()
    {
        Assert.Empty(SilenceCheck.Find([Live("coder", "working", Now.AddMinutes(-30))], Now, FortyFive));
    }

    [Fact]
    public void ExactlyTheThresholdIsFlaggedAndOneSecondLessIsNot()
    {
        Assert.Equal(["coder"], SilenceCheck.Find([Live("coder", "working", Now.Add(-FortyFive))], Now, FortyFive));
        Assert.Empty(SilenceCheck.Find([Live("coder", "working", Now.Add(-FortyFive).AddSeconds(1))], Now, FortyFive));
    }

    [Fact]
    public void ASessionWithNoKnownRequestTimeIsNotFlagged()
    {
        // An unknown time is not a silent session: nothing is known to say it went quiet.
        Assert.Empty(SilenceCheck.Find([Live("coder", "working", null)], Now, FortyFive));
    }

    [Fact]
    public void ARequestTimeAheadOfTheClockIsNotFlagged()
    {
        Assert.Empty(SilenceCheck.Find([Live("coder", "working", Now.AddHours(1))], Now, FortyFive));
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("done")]
    [InlineData("busy")]
    [InlineData("stale")]
    [InlineData("stopped")]
    public void OnlyASessionListedAsWorkingIsFlagged(string state)
    {
        // A blocked session waits for the owner (a wait, not a silence); a done, stale or stopped one is not running.
        Assert.Empty(SilenceCheck.Find([Live("coder", state, Now.AddHours(-2))], Now, FortyFive));
    }

    [Fact]
    public void TheStateIsMatchedWithoutRegardToCase()
    {
        Assert.Equal(["coder"], SilenceCheck.Find([Live("coder", "Working", Now.AddHours(-1))], Now, FortyFive));
    }

    [Fact]
    public void ZeroThresholdIsOffAndFlagsNothing()
    {
        // The setting is off: an hour without a request is not a notice.
        Assert.Empty(SilenceCheck.Find([Live("coder", "working", Now.AddHours(-5))], Now, TimeSpan.Zero));
    }

    [Fact]
    public void SeveralSessionsAreFlaggedInTheOrderGiven()
    {
        var flagged = SilenceCheck.Find(
            [Live("beta", "working", Now.AddHours(-1)), Live("alpha", "working", Now.AddMinutes(-5)), Live("gamma", "working", Now.AddHours(-3))],
            Now, FortyFive);

        Assert.Equal(["beta", "gamma"], flagged);
    }

    // ---- The setting reader ----

    [Fact]
    public void AMissingSilentMinutesMeansTheDefaultOfFortyFive()
    {
        Assert.Equal(FortyFive, SilenceCheck.ThresholdOf(null));
        Assert.Equal(45, SilenceChoices.MinutesOf(null));
    }

    [Fact]
    public void ZeroIsOff()
    {
        Assert.Equal(TimeSpan.Zero, SilenceCheck.ThresholdOf(JsonValue.Create(0)));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(120, 120)]
    [InlineData(5, 5)]
    [InlineData(1440, 1440)]
    public void ANumberIsTheThresholdInMinutes(int stored, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SilenceCheck.ThresholdOf(JsonValue.Create(stored)));
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(5000, 1440)]
    public void ANumberOutsideTheRangeIsClamped(int stored, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SilenceCheck.ThresholdOf(JsonValue.Create(stored)));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("\"45\"")]
    public void JunkTextIsTheDefault(string text)
    {
        // A string (even one that holds a number) is not a number here, as for the re-alert interval.
        var node = text.StartsWith('"') ? JsonNode.Parse(text) : JsonValue.Create(text);

        Assert.Equal(FortyFive, SilenceCheck.ThresholdOf(node));
    }

    [Fact]
    public void ANegativeNumberIsTheDefault()
    {
        Assert.Equal(FortyFive, SilenceCheck.ThresholdOf(JsonValue.Create(-5)));
    }

    [Fact]
    public void ReadingTheSettingDoesNotChangeIt()
    {
        var node = JsonValue.Create(20);

        SilenceCheck.ThresholdOf(node);
        SilenceChoices.Of(node);

        Assert.Equal("20", node.ToJsonString());
    }

    // ---- The Settings choices ----

    [Fact]
    public void ThePresetsAreOffThirtyFortyFiveSixtyAndOneTwentyMinutes()
    {
        Assert.Equal(new[] { 0, 30, 45, 60, 120 }, SilenceChoices.Presets);
    }

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(30, "30 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "60 min")]
    [InlineData(120, "120 min")]
    public void EachPresetShowsItsOwnChoice(int minutes, string label)
    {
        var choice = SilenceChoices.Of(JsonValue.Create(minutes));

        Assert.Equal(minutes, choice.Minutes);
        Assert.Equal(label, choice.Label);
        Assert.False(choice.IsCustom);
    }

    [Fact]
    public void AMissingValueShowsTheDefaultPreset()
    {
        var choice = SilenceChoices.Of(null);

        Assert.Equal(45, choice.Minutes);
        Assert.Equal("45 min", choice.Label);
        Assert.False(choice.IsCustom);
    }

    [Fact]
    public void AValueThatIsNotAPresetShowsAsCustomWithTheMinutesTheEngineUses()
    {
        var choice = SilenceChoices.Of(JsonValue.Create(20));

        Assert.Equal(20, choice.Minutes);
        Assert.Equal("Custom (20 min)", choice.Label);
        Assert.True(choice.IsCustom);
    }

    [Fact]
    public void AValueClampedBelowTheRangeShowsTheMinimumTheEngineUses()
    {
        var choice = SilenceChoices.Of(JsonValue.Create(2));

        Assert.Equal(5, choice.Minutes);
        Assert.Equal("Custom (5 min)", choice.Label);
    }

    // ---- The notice in the attention list ----

    private static SessionRecord Coder(string name, string sessionId, string? lastAlert = null) =>
        new(SessionRole.Coder, sessionId, name, Now.AddHours(-3), 0, [], null, lastAlert);

    private static AgentInfo Listed(string sessionId, string state, string name = "coder") =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: name, Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    [Fact]
    public void AFlaggedWorkingSessionGetsASilentNoticeWithTheThresholdInMinutes()
    {
        var items = AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")],
            silent: ["coder"], silentAfter: FortyFive);

        var item = Assert.Single(items);
        Assert.Equal(AttentionReason.Silent, item.Reason);
        Assert.Equal(SessionA, item.SessionId);
        Assert.Equal("no output for 45 min; may be a long command", item.Detail);
    }

    [Fact]
    public void AWorkingSessionIsNotNoticedWhenNothingIsFlagged()
    {
        Assert.Empty(AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")], silent: [], silentAfter: FortyFive));
        Assert.Empty(AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")]));
    }

    [Fact]
    public void AZeroThresholdGivesNoNoticeEvenWhenNamesAreGiven()
    {
        // The setting is off: the names do not matter.
        Assert.Empty(AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")],
            silent: ["coder"], silentAfter: TimeSpan.Zero));
    }

    [Fact]
    public void ABlockedSessionKeepsItsWaitAndGetsNoSilentNotice()
    {
        var item = Assert.Single(AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "blocked")],
            silent: ["coder"], silentAfter: FortyFive));

        Assert.Equal(AttentionReason.Blocked, item.Reason);
    }

    [Fact]
    public void ALimitNoticeWinsOverTheSilentNoticeOfTheSameSession()
    {
        var limited = Coder("coder", SessionA) with { LimitLevel = RunBudget.ReachedLevel };

        var item = Assert.Single(AttentionList.From([limited], [Listed(SessionA, "working")],
            silent: ["coder"], silentAfter: FortyFive));

        Assert.Equal(AttentionReason.LimitReached, item.Reason);
    }

    [Fact]
    public void ANotListedSessionGetsNoSilentNotice()
    {
        Assert.Empty(AttentionList.From([Coder("coder", SessionA)], [], silent: ["coder"], silentAfter: FortyFive));
    }

    [Fact]
    public void ASilentNoticeIsANoticeNotAWaitAndNeverEscalates()
    {
        var item = Assert.Single(AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")],
            silent: ["coder"], silentAfter: FortyFive));

        Assert.False(AttentionEscalation.Escalates(item));
        Assert.Equal(ToastCategory.Notices, ToastCategories.Of(item.Reason));
        Assert.Equal("no output for a while", ToastText.ReasonText(item.Reason));
    }

    [Fact]
    public void ASilentNoticeIsAnnouncedOnceWhileItsKeyIsRemembered()
    {
        var items = AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")],
            silent: ["coder"], silentAfter: FortyFive);
        var announced = AttentionList.SilentKeys(items);

        // The first sight is new. With its key remembered, the same notice is not new again.
        Assert.Single(AttentionList.Gained([], items, announced: new HashSet<string>(StringComparer.Ordinal)));
        Assert.Empty(AttentionList.Gained([], items, announced));
    }

    [Fact]
    public void ASilentNoticeIsNewAgainOnceItsKeyIsDroppedAtTheEndOfASpell()
    {
        var items = AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")],
            silent: ["coder"], silentAfter: FortyFive);
        var announced = new HashSet<string>(AttentionList.SilentKeys(items), StringComparer.Ordinal);

        // The session writes again: the notice leaves the list, and the caller drops its key. The next spell is announced again.
        var quiet = AttentionList.From([Coder("coder", SessionA)], [Listed(SessionA, "working")]);
        announced.ExceptWith(AttentionList.SilentKeys(items).Where(key => !AttentionList.SilentKeys(quiet).Contains(key)));

        Assert.Single(AttentionList.Gained(quiet, items, announced));
    }
}

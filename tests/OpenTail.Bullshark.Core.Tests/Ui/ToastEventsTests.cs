using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// The toasts by event (toasts by event, 2026-11-18): the category of each reason, the switches in ui.json, the filter on the Needs-you toast, and
/// the two silent toasts (ready for review, usage limit reset). The toast words never carry a path or a detail.
/// </summary>
public sealed class ToastEventsTests
{
    private static AttentionItem Item(string name, AttentionReason reason = AttentionReason.Blocked, string detail = "waiting for input") =>
        new(name, "session-" + name, reason, detail, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(AttentionReason.Blocked, ToastCategory.Blocked)]
    [InlineData(AttentionReason.RestartLoop, ToastCategory.Problems)]
    [InlineData(AttentionReason.StartFailed, ToastCategory.Problems)]
    [InlineData(AttentionReason.Stopped, ToastCategory.Problems)]
    [InlineData(AttentionReason.Failed, ToastCategory.Problems)]
    [InlineData(AttentionReason.Overlap, ToastCategory.Notices)]
    [InlineData(AttentionReason.Conflict, ToastCategory.Notices)]
    [InlineData(AttentionReason.LimitReached, ToastCategory.Notices)]
    [InlineData(AttentionReason.NameTaken, ToastCategory.Problems)]
    [InlineData(AttentionReason.Silent, ToastCategory.Notices)]
    public void EachReasonBelongsToOneCategory(AttentionReason reason, ToastCategory category)
    {
        Assert.Equal(category, ToastCategories.Of(reason));
    }

    [Fact]
    public void EveryCategoryHasItsOwnKeyAndTitle()
    {
        var keys = Enum.GetValues<ToastCategory>().Select(ToastCategories.NameOf).ToList();

        Assert.Equal(new[] { "blocked", "problems", "notices", "ready", "limitReset" }, keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var category in Enum.GetValues<ToastCategory>())
            Assert.False(string.IsNullOrWhiteSpace(ToastCategories.TitleOf(category)));
    }

    [Fact]
    public void AMissingObjectOrKeyMeansOn()
    {
        foreach (var category in Enum.GetValues<ToastCategory>())
        {
            Assert.True(ToastCategories.Enabled(null, category));
            Assert.True(ToastCategories.Enabled(new JsonObject(), category));
        }
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void AValueThatIsNotABooleanMeansOn(string json)
    {
        var obj = new JsonObject { ["blocked"] = JsonNode.Parse(json) };

        Assert.True(ToastCategories.Enabled(obj, ToastCategory.Blocked));
    }

    [Fact]
    public void AFalseKeyTurnsOnlyItsCategoryOff()
    {
        var obj = new JsonObject { ["notices"] = false };

        Assert.False(ToastCategories.Enabled(obj, ToastCategory.Notices));
        Assert.True(ToastCategories.Enabled(obj, ToastCategory.Blocked));
        Assert.True(ToastCategories.Enabled(obj, ToastCategory.LimitReset));
    }

    [Fact]
    public void WithChangesOneKeyAndKeepsTheRest()
    {
        var stored = JsonNode.Parse("""{ "blocked": false, "futureKey": "kept" }""");

        var written = ToastCategories.With(stored, ToastCategory.Ready, false);

        Assert.False(ToastCategories.Enabled(written, ToastCategory.Blocked));
        Assert.False(ToastCategories.Enabled(written, ToastCategory.Ready));
        Assert.True(ToastCategories.Enabled(written, ToastCategory.Problems));
        Assert.Equal("kept", written["futureKey"]!.GetValue<string>());
    }

    [Fact]
    public void WithDoesNotChangeTheStoredNode()
    {
        var stored = JsonNode.Parse("""{ "blocked": false }""");

        _ = ToastCategories.With(stored, ToastCategory.Blocked, true);

        Assert.Equal("""{"blocked":false}""", stored!.ToJsonString());
    }

    [Fact]
    public void WithOnANewObjectMakesOneKey()
    {
        var written = ToastCategories.With(null, ToastCategory.LimitReset, false);

        Assert.Equal("""{"limitReset":false}""", written.ToJsonString());
    }

    [Fact]
    public void WithAllCategoriesOnGivesTheSameToastsAsBefore()
    {
        // The default (no filter) and an all-on object allow every reason: the Needs-you toast is the one ToastText gave before.
        var allOn = new JsonObject();
        bool Allowed(AttentionReason reason) => ToastCategories.Enabled(allOn, ToastCategories.Of(reason));

        var filtered = ToastText.Build([Item("ovc-coder"), Item("ovc-merge", AttentionReason.Stopped)], [], Allowed);
        var plain = ToastText.Build([Item("ovc-coder"), Item("ovc-merge", AttentionReason.Stopped)], []);

        Assert.Equal(plain, filtered);
    }

    [Fact]
    public void AWaitCategoryOffDropsItsItemsFromTheNeedsYouToast()
    {
        bool Allowed(AttentionReason reason) => ToastCategories.Of(reason) != ToastCategory.Blocked;

        var message = ToastText.Build([Item("ovc-coder"), Item("ovc-merge", AttentionReason.Stopped)], [], Allowed);

        Assert.Equal(new ToastMessage("Needs you", "ovc-merge · stopped"), message);
    }

    [Fact]
    public void AllTheWaitsSwitchedOffGiveNoToastEvenWithItems()
    {
        var message = ToastText.Build([Item("ovc-coder")], [new ReAlert(Item("ovc-merge", AttentionReason.Failed), 1)], _ => false);

        Assert.Null(message);
    }

    [Fact]
    public void AStillWaitingAlertStaysWhenItsCategoryIsOnAndTheNewWaitIsOff()
    {
        bool Allowed(AttentionReason reason) => ToastCategories.Of(reason) == ToastCategory.Blocked;
        var message = ToastText.Build([Item("ovc-merge", AttentionReason.Stopped)], [new ReAlert(Item("ovc-coder"), 1)], Allowed);

        Assert.Equal(new ToastMessage("Still waiting", "ovc-coder · blocked"), message);
    }

    [Fact]
    public void ReadyNamesTheFirstCoderAndCountsTheRest()
    {
        Assert.Equal(new ToastMessage("Ready for review", "ovc-coder"), ToastText.Ready(["ovc-coder"]));
        Assert.Equal(new ToastMessage("Ready for review", "ovc-coder, and 2 more"),
            ToastText.Ready(["ovc-coder", "ovc-review", "ovc-merge"]));
    }

    [Fact]
    public void ReadyCountsEachNameOnce()
    {
        Assert.Equal(new ToastMessage("Ready for review", "ovc-coder, and 1 more"), ToastText.Ready(["ovc-coder", "ovc-coder", "ovc-review"]));
    }

    [Fact]
    public void NoReadyCoderGivesNoToast()
    {
        Assert.Null(ToastText.Ready([]));
    }

    [Fact]
    public void ALongReadyNameIsCut()
    {
        var name = new string('z', 100);

        var message = ToastText.Ready([name]);

        Assert.Equal(ToastText.MaxNameLength, message!.Body.Length);
        Assert.EndsWith("…", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitResetNamesTheLimitsOnce()
    {
        Assert.Equal(new ToastMessage("Usage limit reset", "5-hour"), ToastText.LimitReset(["5-hour"]));
        Assert.Equal(new ToastMessage("Usage limit reset", "5-hour, weekly"), ToastText.LimitReset(["5-hour", "weekly", "5-hour"]));
    }

    [Fact]
    public void NoLimitGivesNoToast()
    {
        Assert.Null(ToastText.LimitReset([]));
        Assert.Null(ToastText.LimitReset(["  "]));
    }

    [Fact]
    public void TheNewToastsCarryNoPathOrDetail()
    {
        var ready = ToastText.Ready(["ovc-coder"])!;
        var limit = ToastText.LimitReset(["5-hour"])!;

        foreach (var text in new[] { ready.Title, ready.Body, limit.Title, limit.Body })
        {
            Assert.DoesNotContain("\\", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/", text, StringComparison.Ordinal);
        }
    }
}

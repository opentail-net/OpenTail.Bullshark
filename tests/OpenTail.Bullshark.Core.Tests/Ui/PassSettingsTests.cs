using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// The ui.json values one pass uses, read as a set (perf and testability batch, Q2). Each value must be the answer its own reader gives for the same
/// file, so the pass draws and announces what the separate reads did.
/// </summary>
public sealed class PassSettingsTests
{
    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void AMissingFileGivesTheDefaultsEachReaderGives()
    {
        var settings = PassSettings.From(null);

        Assert.True(settings.Notify);
        Assert.True(settings.Toast);
        Assert.All(Enum.GetValues<ToastCategory>(), category => Assert.True(settings.ToastOn(category)));
        Assert.Null(settings.Quiet);
        Assert.Equal(ContextMeter.DefaultWindow, settings.ContextWindow);
        Assert.Equal(SilenceCheck.DefaultThreshold, settings.SilentAfter);
        Assert.Equal(AttentionEscalation.DefaultInterval, settings.EscalateEvery);
        Assert.False(settings.AttentionFile);
    }

    [Fact]
    public void EachValueIsTheAnswerItsOwnReaderGives()
    {
        var file = Obj("""
            {
              "notify": false,
              "toast": "yes",
              "toastEvents": { "blocked": false, "ready": true },
              "quietHours": { "from": "22:00", "to": "07:00" },
              "contextWindow": 1000000,
              "silentMinutes": 0,
              "escalateMinutes": 5,
              "attentionFile": true
            }
            """);

        var settings = PassSettings.From(file);

        Assert.Equal(AttentionList.NotificationsOn(file["notify"]), settings.Notify);
        Assert.Equal(AttentionList.NotificationsOn(file["toast"]), settings.Toast);
        Assert.Equal(QuietHours.Parse(file["quietHours"]), settings.Quiet);
        Assert.Equal(ContextMeter.WindowFrom(file["contextWindow"]), settings.ContextWindow);
        Assert.Equal(SilenceCheck.ThresholdOf(file["silentMinutes"]), settings.SilentAfter);
        Assert.Equal(AttentionEscalation.IntervalOf(file["escalateMinutes"]), settings.EscalateEvery);
        Assert.Equal(AttentionFile.Enabled(file["attentionFile"]), settings.AttentionFile);
        foreach (var category in Enum.GetValues<ToastCategory>())
            Assert.Equal(ToastCategories.Enabled(file[ToastCategories.Key], category), settings.ToastOn(category));
    }

    [Fact]
    public void ANonBooleanToastSwitchMeansOnAndAFalseKindIsOff()
    {
        var settings = PassSettings.From(Obj("{\"notify\":false,\"toast\":\"yes\",\"toastEvents\":{\"blocked\":false}}"));

        Assert.False(settings.Notify);
        Assert.True(settings.Toast);
        Assert.False(settings.ToastOn(ToastCategory.Blocked));
        Assert.True(settings.ToastOn(ToastCategory.Problems));
    }

    [Fact]
    public void QuietHoursAreAppliedAtTheTimeGiven()
    {
        var settings = PassSettings.From(Obj("{\"quietHours\":{\"from\":\"22:00\",\"to\":\"07:00\"}}"));
        var night = new DateTimeOffset(2026, 11, 19, 23, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 19, 23, 30, 0)));
        var day = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 19, 12, 0, 0)));

        Assert.True(settings.IsQuietAt(night));
        Assert.False(settings.IsQuietAt(day));
        Assert.False(PassSettings.Defaults.IsQuietAt(night));
    }

    [Fact]
    public void AnUnreadableValueTakesItsReadersDefault()
    {
        // A value of the wrong type is what each reader calls missing, so the set gives the same default.
        var settings = PassSettings.From(Obj("{\"contextWindow\":\"big\",\"silentMinutes\":\"never\",\"escalateMinutes\":[]}"));

        Assert.Equal(ContextMeter.DefaultWindow, settings.ContextWindow);
        Assert.Equal(SilenceCheck.DefaultThreshold, settings.SilentAfter);
        Assert.Equal(AttentionEscalation.DefaultInterval, settings.EscalateEvery);
    }

    [Fact]
    public void TheKeysAreTheFilesKeys()
    {
        // The Settings page writes these names; the pass reads the same ones (a rename would break both at once).
        Assert.Equal("notify", PassSettings.NotifyKey);
        Assert.Equal("toast", PassSettings.ToastKey);
        Assert.Equal("contextWindow", PassSettings.ContextWindowKey);
        Assert.Equal("silentMinutes", PassSettings.SilentKey);
        Assert.Equal("escalateMinutes", PassSettings.EscalateKey);
    }
}

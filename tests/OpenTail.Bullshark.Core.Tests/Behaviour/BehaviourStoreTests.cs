using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// behaviour.json (decisions of 2026-11-22): the defaults in code, a default for each key that is absent or not usable, a problem that gives the
/// defaults, and a file the store never writes. Each test makes its own folder and removes only that folder.
/// </summary>
public sealed class BehaviourStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-behaviour-" + Guid.NewGuid().ToString("N"));

    public BehaviourStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string StateFile => Path.Combine(_folder, "ui.json");

    private string BehaviourFile => BehaviourStore.PathBeside(StateFile);

    private void WriteBehaviour(string text) => File.WriteAllText(BehaviourFile, text);

    private static void AssertDefaults(BehaviourSettings s)
    {
        Assert.Equal(0, s.Alerts.RepeatMinutes);
        Assert.Equal(FocusGate.Unfocused, s.Alerts.FocusGate);
        Assert.True(s.Alerts.Kinds.SetEquals(new[] { AlertGroup.NeedsInput, AlertGroup.Errored }));
        Assert.Equal(60, s.Lookups.MissMemorySeconds);
        Assert.Equal("2.1.330", s.Versions.Minimum.Claude);
        Assert.Equal("0.171.0", s.Versions.Minimum.Codex);
        Assert.Equal(UnreadableVersion.Warn, s.Versions.Unreadable);
        Assert.True(s.Exit.EndOnProcessExit);
        Assert.False(s.Resume.AllowNewestInFolder);
        Assert.Equal(AmbiguousResume.Refuse, s.Resume.Ambiguous);
        Assert.Equal(0, s.State.WorkingDecayMinutes);
        Assert.True(s.State.ClampClockBackwards);
        Assert.Equal(60, s.Index.RefreshSeconds);
        Assert.Equal(IndexStamp.CountAndNewest, s.Index.Stamp);
        Assert.Equal(UsageAttribution.LastUpdate, s.Usage.Attribution);
        Assert.True(s.Usage.ReasoningAsOutput);
    }

    [Fact]
    public void TheDefaultsInCodeMatchTheDecisionTable()
    {
        AssertDefaults(BehaviourSettings.Defaults);
    }

    [Fact]
    public void AMissingFileGivesTheDefaultsWithoutProblemAndNeverCreatesOne()
    {
        var load = BehaviourStore.LoadBeside(StateFile);

        AssertDefaults(load.Settings);
        Assert.Null(load.Problem);
        Assert.Equal(0, load.Ignored);
        Assert.False(File.Exists(BehaviourFile));
    }

    [Fact]
    public void TheFileIsInTheFolderOfTheStateFile()
    {
        Assert.Equal(Path.Combine(_folder, "behaviour.json"), BehaviourFile);
    }

    [Fact]
    public void APartialFileKeepsTheDefaultsForTheAbsentKeys()
    {
        WriteBehaviour("{\"alerts\":{\"repeatMinutes\":5},\"lookups\":{\"missMemorySeconds\":0}}");

        var load = BehaviourStore.LoadBeside(StateFile);

        Assert.Equal(5, load.Settings.Alerts.RepeatMinutes);
        Assert.Equal(TimeSpan.FromMinutes(5), load.Settings.Alerts.RepeatInterval);
        Assert.Equal(0, load.Settings.Lookups.MissMemorySeconds);
        Assert.Equal(FocusGate.Unfocused, load.Settings.Alerts.FocusGate);
        Assert.True(load.Settings.Alerts.Kinds.SetEquals(new[] { AlertGroup.NeedsInput, AlertGroup.Errored }));
        Assert.Equal("2.1.330", load.Settings.Versions.Minimum.Claude);
        Assert.Null(load.Problem);
        Assert.Equal(0, load.Ignored);
    }

    [Fact]
    public void EveryKnobReadsItsValue()
    {
        WriteBehaviour("""
        {
          "alerts": { "repeatMinutes": 10, "focusGate": "unfocusedOrUnselected", "kinds": ["needsInput", "finished"] },
          "lookups": { "missMemorySeconds": 30 },
          "versions": { "minimum": { "claude": "2.2.0", "codex": "0.200.1" }, "unreadable": "refuse" },
          "exit": { "endOnProcessExit": false },
          "resume": { "allowNewestInFolder": true, "ambiguous": "earliest" },
          "state": { "workingDecayMinutes": 45, "clampClockBackwards": false },
          "index": { "refreshSeconds": 0, "stamp": "countAndNewest" },
          "usage": { "attribution": "event", "reasoningAsOutput": false }
        }
        """);

        var s = BehaviourStore.LoadBeside(StateFile).Settings;

        Assert.Equal(10, s.Alerts.RepeatMinutes);
        Assert.Equal(FocusGate.UnfocusedOrUnselected, s.Alerts.FocusGate);
        Assert.True(s.Alerts.Kinds.SetEquals(new[] { AlertGroup.NeedsInput, AlertGroup.Finished }));
        Assert.Equal(30, s.Lookups.MissMemorySeconds);
        Assert.Equal("2.2.0", s.Versions.Minimum.Claude);
        Assert.Equal("0.200.1", s.Versions.Minimum.Codex);
        Assert.Equal(UnreadableVersion.Refuse, s.Versions.Unreadable);
        Assert.False(s.Exit.EndOnProcessExit);
        Assert.True(s.Resume.AllowNewestInFolder);
        Assert.Equal(AmbiguousResume.Earliest, s.Resume.Ambiguous);
        Assert.Equal(45, s.State.WorkingDecayMinutes);
        Assert.False(s.State.ClampClockBackwards);
        Assert.Equal(0, s.Index.RefreshSeconds);
        Assert.Equal(IndexStamp.CountAndNewest, s.Index.Stamp);
        Assert.Equal(UsageAttribution.Event, s.Usage.Attribution);
        Assert.False(s.Usage.ReasoningAsOutput);
    }

    [Fact]
    public void AMalformedFileGivesTheDefaultsAndOnlyTheExceptionTypeAsProblem()
    {
        WriteBehaviour("{ \"alerts\": { \"repeatMinutes\": 5 ");

        var load = BehaviourStore.LoadBeside(StateFile);

        AssertDefaults(load.Settings);
        // The runtime's JSON exception type: only its name is kept, so the test checks the family, not the exact class.
        Assert.NotNull(load.Problem);
        Assert.Contains("Json", load.Problem);
    }

    [Fact]
    public void AFileWhoseRootIsNotAnObjectGivesTheDefaults()
    {
        WriteBehaviour("[1, 2]");

        var load = BehaviourStore.LoadBeside(StateFile);

        AssertDefaults(load.Settings);
        Assert.Equal("NotAnObject", BehaviourStore.Parse("[1, 2]").Problem);
    }

    [Fact]
    public void AnUnreadableFileGivesTheDefaultsAndTheExceptionType()
    {
        WriteBehaviour("{\"alerts\":{\"repeatMinutes\":5}}");
        // A file that another process holds without sharing cannot be read: the store gives the defaults and the exception type only.
        using var held = new FileStream(BehaviourFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var load = BehaviourStore.LoadBeside(StateFile);

        AssertDefaults(load.Settings);
        Assert.Equal("IOException", load.Problem);
        Assert.Equal(0, load.Ignored);
        Assert.True(File.Exists(BehaviourFile));
    }

    [Fact]
    public void ABadEnumValueGivesTheDefaultForThatKeyOnly()
    {
        WriteBehaviour("{\"alerts\":{\"focusGate\":\"always\",\"repeatMinutes\":3}}");

        var load = BehaviourStore.LoadBeside(StateFile);

        Assert.Equal(FocusGate.Unfocused, load.Settings.Alerts.FocusGate);
        Assert.Equal(3, load.Settings.Alerts.RepeatMinutes);
        Assert.Equal(1, load.Ignored);
        Assert.Null(load.Problem);
    }

    [Fact]
    public void WrongTypesAndOutOfRangeValuesGiveTheDefaults()
    {
        WriteBehaviour("{\"alerts\":{\"repeatMinutes\":\"5\"},\"state\":{\"workingDecayMinutes\":-1}," +
                       "\"exit\":{\"endOnProcessExit\":\"no\"},\"index\":{\"refreshSeconds\":1.5}}");

        var load = BehaviourStore.LoadBeside(StateFile);

        AssertDefaults(load.Settings);
        Assert.Equal(4, load.Ignored);
    }

    [Fact]
    public void ARepeatAboveTheLongestIntervalGivesTheDefault()
    {
        var load = BehaviourStore.Parse("{\"alerts\":{\"repeatMinutes\":1441}}");

        Assert.Equal(0, load.Settings.Alerts.RepeatMinutes);
        Assert.Equal(1, load.Ignored);
    }

    [Fact]
    public void KindsWithAnUnknownNameGiveTheDefaultAndAnEmptyListIsValid()
    {
        var unknown = BehaviourStore.Parse("{\"alerts\":{\"kinds\":[\"needsInput\",\"notice\"]}}");
        var empty = BehaviourStore.Parse("{\"alerts\":{\"kinds\":[]}}");

        Assert.True(unknown.Settings.Alerts.Kinds.SetEquals(new[] { AlertGroup.NeedsInput, AlertGroup.Errored }));
        Assert.Equal(1, unknown.Ignored);
        Assert.Empty(empty.Settings.Alerts.Kinds);
        Assert.Equal(0, empty.Ignored);
    }

    [Fact]
    public void AVersionThatIsNotDottedDigitsGivesTheDefault()
    {
        var load = BehaviourStore.Parse("{\"versions\":{\"minimum\":{\"claude\":\"2.1.x\",\"codex\":\"\"}}}");

        Assert.Equal("2.1.330", load.Settings.Versions.Minimum.Claude);
        Assert.Equal("0.171.0", load.Settings.Versions.Minimum.Codex);
        Assert.Equal(2, load.Ignored);
    }

    [Fact]
    public void ASectionThatIsNotAnObjectIsCountedAndItsKeysGiveTheDefaults()
    {
        var load = BehaviourStore.Parse("{\"alerts\":5,\"exit\":{\"endOnProcessExit\":false}}");

        Assert.Equal(0, load.Settings.Alerts.RepeatMinutes);
        Assert.False(load.Settings.Exit.EndOnProcessExit);
        Assert.Equal(1, load.Ignored);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        var load = BehaviourStore.Parse("// the owner's note\n{\"alerts\":{\"repeatMinutes\":7,},}");

        Assert.Equal(7, load.Settings.Alerts.RepeatMinutes);
        Assert.Null(load.Problem);
    }

    [Fact]
    public void ALegacyEscalateSetsTheRepeatUntilTheFileHasItsOwn()
    {
        // The Settings page still writes ui.json's escalateMinutes: it sets the interval until behaviour.json has alerts.repeatMinutes.
        Assert.Equal(30, BehaviourStore.Parse(null, JsonValue.Create(30)).Settings.Alerts.RepeatMinutes);
        Assert.Equal(0, BehaviourStore.Parse(null, JsonValue.Create(0)).Settings.Alerts.RepeatMinutes);
        Assert.Equal(5, BehaviourStore.Parse("{\"alerts\":{\"repeatMinutes\":5}}", JsonValue.Create(30)).Settings.Alerts.RepeatMinutes);
        Assert.Equal(0, BehaviourStore.Parse(null).Settings.Alerts.RepeatMinutes);
    }

    [Fact]
    public void TheFileIsNeverWritten()
    {
        WriteBehaviour("{\"alerts\":{\"repeatMinutes\":5}}");
        var bytes = File.ReadAllBytes(BehaviourFile);
        var stamp = File.GetLastWriteTimeUtc(BehaviourFile);

        BehaviourStore.LoadBeside(StateFile);
        BehaviourStore.LoadBeside(StateFile, JsonValue.Create(30));

        Assert.Equal(bytes, File.ReadAllBytes(BehaviourFile));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(BehaviourFile));
    }

    [Fact]
    public void AMalformedFileIsNotRewritten()
    {
        WriteBehaviour("{ broken");
        var bytes = File.ReadAllBytes(BehaviourFile);

        BehaviourStore.LoadBeside(StateFile);

        Assert.Equal(bytes, File.ReadAllBytes(BehaviourFile));
    }
}

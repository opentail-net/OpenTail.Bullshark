using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Tests.Continue;

/// <summary>
/// The continue section of behaviour.json (Continue plan, C2): the defaults, each usable value, a value that is not usable (the default, and counted),
/// and a file read from a folder of its own. The test that writes a file makes its own folder and removes only that folder.
/// </summary>
public sealed class ContinueSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-continue-settings-" + Guid.NewGuid().ToString("N"));

    public ContinueSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>A behaviour.json text whose only section is <c>continue</c>, holding the given key-value pairs.</summary>
    private static BehaviourLoad WithContinue(string keyValues) => BehaviourStore.Parse("{ \"continue\": { " + keyValues + " } }");

    [Fact]
    public void TheDefaultsAreProviderScopeNoModeAndNormalSummary()
    {
        var c = BehaviourSettings.Defaults.Continue;

        Assert.Equal(ContinueScope.Provider, c.Scope);
        Assert.Null(c.DefaultMode);
        Assert.Equal(SummaryLevel.Normal, c.SummaryLevel);
    }

    [Fact]
    public void AMissingSectionGivesTheDefaultsWithNothingIgnored()
    {
        var absent = BehaviourStore.Parse(null);
        var otherSection = BehaviourStore.Parse("""{ "alerts": { "repeatMinutes": 5 } }""");

        foreach (var load in new[] { absent, otherSection })
        {
            Assert.Equal(ContinueScope.Provider, load.Settings.Continue.Scope);
            Assert.Null(load.Settings.Continue.DefaultMode);
            Assert.Equal(SummaryLevel.Normal, load.Settings.Continue.SummaryLevel);
            Assert.Equal(0, load.Ignored);
        }
    }

    [Fact]
    public void AMissingKeyGivesItsDefaultAndTheOthersAreKept()
    {
        var load = WithContinue("\"scope\": \"all\"");

        Assert.Equal(ContinueScope.All, load.Settings.Continue.Scope);
        Assert.Null(load.Settings.Continue.DefaultMode);
        Assert.Equal(SummaryLevel.Normal, load.Settings.Continue.SummaryLevel);
        Assert.Equal(0, load.Ignored);
    }

    [Theory]
    [InlineData("provider", ContinueScope.Provider)]
    [InlineData("all", ContinueScope.All)]
    public void EachScopeValueIsRead(string text, ContinueScope expected)
    {
        var load = WithContinue($"\"scope\": \"{text}\"");

        Assert.Equal(expected, load.Settings.Continue.Scope);
        Assert.Equal(0, load.Ignored);
    }

    [Theory]
    [InlineData("full", ContinueMode.Full)]
    [InlineData("lastTurns", ContinueMode.LastTurns)]
    [InlineData("summary", ContinueMode.Summary)]
    public void EachDefaultModeValueIsRead(string text, ContinueMode expected)
    {
        var load = WithContinue($"\"defaultMode\": \"{text}\"");

        Assert.Equal<ContinueMode?>(expected, load.Settings.Continue.DefaultMode);
        Assert.Equal(0, load.Ignored);
    }

    [Theory]
    [InlineData("minimal", SummaryLevel.Minimal)]
    [InlineData("normal", SummaryLevel.Normal)]
    [InlineData("complete", SummaryLevel.Complete)]
    public void EachSummaryLevelValueIsRead(string text, SummaryLevel expected)
    {
        var load = WithContinue($"\"summaryLevel\": \"{text}\"");

        Assert.Equal(expected, load.Settings.Continue.SummaryLevel);
        Assert.Equal(0, load.Ignored);
    }

    [Theory]
    [InlineData("\"ALL\"")]
    [InlineData("\"tail\"")]
    [InlineData("\"Provider\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("true")]
    public void AnUnusableScopeGivesTheDefaultAndIsCounted(string json)
    {
        var load = WithContinue($"\"scope\": {json}");

        Assert.Equal(ContinueScope.Provider, load.Settings.Continue.Scope);
        Assert.Equal(1, load.Ignored);
    }

    [Theory]
    [InlineData("\"tail\"")]
    [InlineData("\"Full\"")]
    [InlineData("\"LastTurns\"")]
    [InlineData("\"last-turns\"")]
    [InlineData("\"\"")]
    [InlineData("2")]
    [InlineData("false")]
    public void AnUnusableDefaultModeGivesNoModeAndIsCounted(string json)
    {
        var load = WithContinue($"\"defaultMode\": {json}");

        Assert.Null(load.Settings.Continue.DefaultMode);
        Assert.Equal(1, load.Ignored);
    }

    [Theory]
    [InlineData("\"Normal\"")]
    [InlineData("\"Minimal\"")]
    [InlineData("\"long\"")]
    [InlineData("1")]
    [InlineData("false")]
    public void AnUnusableSummaryLevelGivesNormalAndIsCounted(string json)
    {
        var load = WithContinue($"\"summaryLevel\": {json}");

        Assert.Equal(SummaryLevel.Normal, load.Settings.Continue.SummaryLevel);
        Assert.Equal(1, load.Ignored);
    }

    [Fact]
    public void AnUnusableValueLeavesTheUsableOnesAsTheyAre()
    {
        var load = WithContinue("\"scope\": \"all\", \"defaultMode\": \"tail\", \"summaryLevel\": \"complete\"");

        Assert.Equal(ContinueScope.All, load.Settings.Continue.Scope);
        Assert.Null(load.Settings.Continue.DefaultMode);
        Assert.Equal(SummaryLevel.Complete, load.Settings.Continue.SummaryLevel);
        Assert.Equal(1, load.Ignored);
    }

    [Fact]
    public void AContinueSectionThatIsNotAnObjectGivesTheDefaultsAndIsCounted()
    {
        var load = BehaviourStore.Parse("""{ "continue": "all" }""");

        Assert.Equal(ContinueScope.Provider, load.Settings.Continue.Scope);
        Assert.Null(load.Settings.Continue.DefaultMode);
        Assert.Equal(SummaryLevel.Normal, load.Settings.Continue.SummaryLevel);
        Assert.Equal(1, load.Ignored);
    }

    [Fact]
    public void TheValuesAreReadBackFromBehaviourJsonBesideTheStateFile()
    {
        var stateFile = Path.Combine(_folder, "ui.json");
        File.WriteAllText(BehaviourStore.PathBeside(stateFile), """
            {
              "continue": { "scope": "all", "defaultMode": "summary", "summaryLevel": "complete" }
            }
            """);

        var load = BehaviourStore.LoadBeside(stateFile);

        Assert.Null(load.Problem);
        Assert.Equal(0, load.Ignored);
        Assert.Equal(ContinueScope.All, load.Settings.Continue.Scope);
        Assert.Equal<ContinueMode?>(ContinueMode.Summary, load.Settings.Continue.DefaultMode);
        Assert.Equal(SummaryLevel.Complete, load.Settings.Continue.SummaryLevel);
    }

    [Fact]
    public void AMissingBehaviourFileGivesTheDefaultsAndIsNotCreated()
    {
        var stateFile = Path.Combine(_folder, "ui.json");

        var load = BehaviourStore.LoadBeside(stateFile);

        Assert.Equal(ContinueScope.Provider, load.Settings.Continue.Scope);
        Assert.Null(load.Settings.Continue.DefaultMode);
        Assert.Equal(SummaryLevel.Normal, load.Settings.Continue.SummaryLevel);
        Assert.False(File.Exists(BehaviourStore.PathBeside(stateFile)));
    }
}

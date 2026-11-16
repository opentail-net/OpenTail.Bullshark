using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Setup;

public sealed class PatternStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-patterns-" + Guid.NewGuid().ToString("N")[..8]);

    public PatternStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void PatternsSitBesideTheStateFile()
    {
        var store = PatternStore.Beside(Path.Combine(_dir, "state.json"));

        Assert.Equal("patterns.json", Path.GetFileName(store.Path));
        Assert.Equal(_dir, Path.GetDirectoryName(store.Path));
    }

    [Fact]
    public void AMissingFileGivesTheCliDefaults()
    {
        var store = new PatternStore(Path.Combine(_dir, "patterns.json"));

        var planCode = store.For("plan-code");
        Assert.Equal(("opus", "high", "haiku", "medium", 1), (planCode.PlannerModel, planCode.PlannerEffort, planCode.CoderModel, planCode.CoderEffort, planCode.Coders));
        Assert.Equal(2, store.For("plan-many").Coders);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void SavingOnePatternKeepsTheOthers()
    {
        var store = new PatternStore(Path.Combine(_dir, "patterns.json"));

        store.Save(PatternStore.Defaults("plan-code") with
        {
            PlannerExtra = "Small batches.",
            PlannerBrief = "Plan {task} for {codernames}.",
            CoderBrief = "Report to {plannername}.",
        });
        store.Save(PatternStore.Defaults("solo") with { CoderExtra = "Unused." });

        Assert.Equal("Small batches.", store.For("plan-code").PlannerExtra);
        Assert.Equal("Plan {task} for {codernames}.", store.For("plan-code").PlannerBrief);
        Assert.Equal("Report to {plannername}.", store.For("plan-code").CoderBrief);
        Assert.Equal("Unused.", store.For("solo").CoderExtra);

        store.Save(store.For("plan-code") with { PlannerBrief = null, CoderBrief = null });

        Assert.Null(store.For("plan-code").PlannerBrief);
        Assert.Null(store.For("plan-code").CoderBrief);
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void ACorruptFileGivesTheDefaultsInsteadOfFailing()
    {
        var path = Path.Combine(_dir, "patterns.json");
        File.WriteAllText(path, "{ not json");

        var settings = new PatternStore(path).For("plan-code");

        Assert.Equal("opus", settings.PlannerModel);
    }

    [Fact]
    public void AFileWithTheNamesOfTheFirstVersionStillLoadsAndTheValuesAreNotRead()
    {
        // The names PlannerBriefTemplate and CoderBriefTemplate were never released. An unknown property is ignored, so the file loads.
        var path = Path.Combine(_dir, "patterns.json");
        File.WriteAllText(path, """
            {"plan-code":{"PatternKey":"plan-code","PlannerModel":"sonnet","PlannerEffort":"low","CoderModel":"haiku",
            "CoderEffort":"medium","Coders":1,"PlannerExtra":"","CoderExtra":"","PlannerBriefTemplate":"old text"}}
            """);

        var settings = new PatternStore(path).For("plan-code");

        Assert.Equal("sonnet", settings.PlannerModel);
        Assert.Null(settings.PlannerBrief);
    }
}

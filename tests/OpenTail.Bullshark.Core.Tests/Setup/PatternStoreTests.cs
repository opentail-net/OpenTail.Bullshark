using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
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
    public void ACorruptFileIsNotReadAsEmptyAndIsNotOverwritten()
    {
        var path = Path.Combine(_dir, "patterns.json");
        File.WriteAllText(path, "{ not json");
        var store = new PatternStore(path);

        Assert.Throws<StoreUnreadableException>(() => store.For("plan-code"));
        Assert.Throws<StoreUnreadableException>(() => store.Save(PatternStore.Defaults("solo")));

        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void AFileThatCannotBeReadSurfacesAsTheStoreError()
    {
        var path = Path.Combine(_dir, "patterns.json");
        File.WriteAllText(path, "{}");
        var store = new PatternStore(path);

        // Another process holds the file without sharing it, as an antivirus or sync tool can.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<StoreUnreadableException>(() => store.For("plan-code"));
            Assert.Throws<StoreUnreadableException>(() => store.Save(PatternStore.Defaults("solo")));
        }

        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void TwoWritersKeepBothPatterns()
    {
        var path = Path.Combine(_dir, "patterns.json");
        var first = new PatternStore(path);
        var second = new PatternStore(path);

        // Each writer saves its own patterns at the same time. Each save reads the file again under the lock, so none is lost.
        Parallel.Invoke(
            () => { for (var i = 0; i < 25; i++) first.Save(PatternStore.Defaults($"writer-a-{i}")); },
            () => { for (var i = 0; i < 25; i++) second.Save(PatternStore.Defaults($"writer-b-{i}")); });

        var saved = new PatternStore(path).Load();
        Assert.Equal(50, saved.Count);
        for (var i = 0; i < 25; i++)
        {
            Assert.True(saved.ContainsKey($"writer-a-{i}"));
            Assert.True(saved.ContainsKey($"writer-b-{i}"));
        }
    }

    [Fact]
    public void ANullEntryIsNoSettingsForItsPattern()
    {
        var path = Path.Combine(_dir, "patterns.json");
        File.WriteAllText(path, """{"plan-code":null,"solo":{"PatternKey":"solo","PlannerModel":"sonnet","PlannerEffort":"low","CoderModel":"haiku","CoderEffort":"medium","Coders":1,"PlannerExtra":"","CoderExtra":""}}""");
        var store = new PatternStore(path);

        Assert.Equal("opus", store.For("plan-code").PlannerModel);
        Assert.Equal("sonnet", store.For("solo").PlannerModel);
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

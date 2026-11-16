using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class RunStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-runs-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    public RunStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void WithoutSessionTakesOnlyThatSpecOut()
    {
        var run = NewRun("ab12", Now, coders: 2);
        var names = run.Specs.Select(s => s.Name).ToList();

        var result = RunStore.WithoutSession([run], names[1]);

        var single = Assert.Single(result);
        Assert.Equal(names.Where(n => n != names[1]), single.Specs.Select(s => s.Name));
    }

    [Fact]
    public void ARunWithNoSessionLeftIsDropped()
    {
        var solo = new RunRecord("cd34", "solo", @"C:\W", "auto", Now,
            RunPlan.Build(PatternSpec.Find("solo")!, @"C:\W", "task", "cd34", 1));

        Assert.Empty(RunStore.WithoutSession([solo], solo.Specs[0].Name));
    }

    [Fact]
    public void AnUnknownNameLeavesTheRunsAsTheyWere()
    {
        var run = NewRun("ab12", Now, coders: 2);

        var result = RunStore.WithoutSession([run], "no-such-session");

        Assert.Same(run, Assert.Single(result));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static RunRecord NewRun(string runId, DateTimeOffset createdAt, int coders = 2) =>
        new(runId, "plan-many", @"C:\W", "auto", createdAt,
            RunPlan.Build(PatternSpec.Find("plan-many")!, @"C:\W", "task", runId, coders));

    [Fact]
    public void AnExistingFileIsReadAsItIs()
    {
        var store = new RunStore(Path.Combine(_dir, "runs.json"));
        store.Save([NewRun("ab12", Now)]);

        var only = Assert.Single(store.Load());

        Assert.Equal("ab12", only.RunId);
    }

    [Fact]
    public void EachSpecComesBackWithItsBriefAndItsResumePrompt()
    {
        var store = new RunStore(Path.Combine(_dir, "runs.json"));
        var run = NewRun("ab12", Now, coders: 3);

        store.Save([run]);
        var loaded = Assert.Single(new RunStore(store.Path).Load());

        Assert.Equal(run.Specs.Count, loaded.Specs.Count);
        for (int i = 0; i < run.Specs.Count; i++)
        {
            Assert.Equal(run.Specs[i].Name, loaded.Specs[i].Name);
            Assert.Equal(run.Specs[i].Brief, loaded.Specs[i].Brief);
            Assert.Equal(run.Specs[i].ResumePrompt, loaded.Specs[i].ResumePrompt);
            Assert.Equal(run.Specs[i].PermissionMode, loaded.Specs[i].PermissionMode);
        }
    }

    [Fact]
    public void TheLatestRunIsTheNewestOne()
    {
        var older = NewRun("ab12", Now);
        var newer = NewRun("cd34", Now.AddHours(2));

        Assert.Equal("cd34", RunStore.Latest([older, newer])?.RunId);
        Assert.Null(RunStore.Latest([]));
    }

    [Fact]
    public void SpecsOfKeepsEverySessionOfEveryRun()
    {
        var runs = new[] { NewRun("ab12", Now, coders: 2), NewRun("cd34", Now, coders: 3) };

        var specs = RunStore.SpecsOf(runs);

        Assert.Equal(3 + 4, specs.Count);
        Assert.Equal(specs.Count, specs.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void SaveLeavesNoTemporaryFileBehind()
    {
        var path = Path.Combine(_dir, "runs.json");

        new RunStore(path).Save([NewRun("ab12", Now)]);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}

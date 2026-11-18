using OpenTail.Bullshark.Core.Library;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>The plan file on a library entry (plan progress, 2026-11-18): it round-trips, an older library.json loads without it, and it counts as a mark.</summary>
public sealed class LibraryPlanFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-planfile-" + Guid.NewGuid().ToString("N")[..8]);

    public LibraryPlanFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathIn(string name = "library.json") => Path.Combine(_dir, name);

    [Fact]
    public void ThePlanFileRoundTripsThroughTheFile()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(Name: "Plan", PlanFile: @"C:\work\plan.md"));

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal(@"C:\work\plan.md", loaded.PlanFile);
    }

    [Fact]
    public void AnOlderLibraryFileLoadsWithNoPlanFile()
    {
        // A library.json written before the plan file existed: the entry has the fields it had, and no PlanFile.
        File.WriteAllText(PathIn(), """{ "s1": { "Name": "Old", "Pinned": true, "Tags": ["plan"] } }""");

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal("Old", loaded.Name);
        Assert.True(loaded.Pinned);
        Assert.Null(loaded.PlanFile);
    }

    [Fact]
    public void AnEntryWithOnlyAPlanFileIsKept()
    {
        var library = new SessionLibrary(PathIn());

        library.Update(entries => entries["s1"] = new LibraryEntry(PlanFile: @"C:\work\plan.md"));

        Assert.Equal(@"C:\work\plan.md", new SessionLibrary(PathIn()).Load()["s1"].PlanFile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPlanPathIsNoMark(string? plan)
    {
        Assert.True(new LibraryEntry(PlanFile: plan).IsEmpty);
    }

    [Fact]
    public void AnEntryWithOnlyAPlanFileIsNotEmpty()
    {
        Assert.False(new LibraryEntry(PlanFile: @"C:\work\plan.md").IsEmpty);
    }

    [Fact]
    public void ClearingThePlanFileRemovesAnEntryThatHasNothingElse()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(PlanFile: @"C:\work\plan.md"));

        library.Update(entries => entries["s1"] = entries["s1"] with { PlanFile = null });

        Assert.Empty(new SessionLibrary(PathIn()).Load());
    }
}

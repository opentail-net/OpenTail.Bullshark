using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>The magic marks in library.json (plan step 5): they round-trip, and a file written before them still loads.</summary>
public sealed class MagicLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-magic-lib-" + Guid.NewGuid().ToString("N")[..8]);

    public MagicLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathIn(string name = "library.json") => Path.Combine(_dir, name);

    [Fact]
    public void TheMagicMarksRoundTrip()
    {
        var magicAt = new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero);
        var magicNamedAt = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(1));
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(Name: "Fix login", MagicAt: magicAt, MagicNamedAt: magicNamedAt));

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal("Fix login", loaded.Name);
        Assert.Equal(magicAt, loaded.MagicAt);
        Assert.Equal(magicNamedAt, loaded.MagicNamedAt);
        Assert.Equal(magicNamedAt.Offset, loaded.MagicNamedAt!.Value.Offset);
    }

    [Fact]
    public void AFileWrittenBeforeTheMagicMarksStillLoads()
    {
        File.WriteAllText(PathIn(), "{\"s1\": {\"Name\": \"Old name\", \"Pinned\": true, \"Tags\": [\"review\"]}}");

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal("Old name", loaded.Name);
        Assert.True(loaded.Pinned);
        Assert.Null(loaded.MagicAt);
        Assert.Null(loaded.MagicNamedAt);
    }

    [Fact]
    public void AMagicNameIsAName()
    {
        var entry = new LibraryEntry(Name: "Fix login", MagicAt: DateTimeOffset.UnixEpoch, MagicNamedAt: DateTimeOffset.UnixEpoch);

        Assert.False(entry.IsEmpty);
    }

    [Fact]
    public void AnEntryWithOnlyTheMagicMarksIsRemovedOnSave()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(MagicAt: DateTimeOffset.UnixEpoch));

        Assert.Empty(library.Load());
    }
}

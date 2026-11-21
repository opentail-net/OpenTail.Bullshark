using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// Stage A5 and A6: the library keys are <see cref="SessionKey"/> text. A file with bare ids (before A5) is migrated in place, once, after a copy
/// of it is kept (owner decision O3). The tests use temp folders and synthetic ids only.
/// </summary>
public sealed class LibraryMigrationTests : IDisposable
{
    private const string Id = "17a94483-99fc-4005-9fa9-dbd63958653d";
    private const string OtherId = "8aed1196-0000-4000-8000-000000000001";

    private static readonly ProviderId Fake = new("fake");
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly Regex BackupName = new(@"^library\.json\.bak-\d{8}-\d{6}$", RegexOptions.CultureInvariant);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-library-migration-" + Guid.NewGuid().ToString("N"));

    public LibraryMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathIn(string name = "library.json") => Path.Combine(_dir, name);

    private string[] Backups() => Directory.GetFiles(_dir, "library.json.bak-*");

    /// <summary>A file in the format before A5: bare ids as keys, written by the same serializer the old build used.</summary>
    private void WriteOldFormat()
    {
        var entries = new SortedDictionary<string, LibraryEntry>(StringComparer.Ordinal)
        {
            [Id] = new LibraryEntry(Name: "Plan", Pinned: true, Tags: ["alpha", "beta"], Note: "kept", Archived: true),
            [OtherId] = new LibraryEntry(Name: "Second", Tags: ["gamma"]),
        };
        File.WriteAllText(PathIn(), JsonSerializer.Serialize(entries, Indented));
    }

    /// <summary>A file already in the format of A5.</summary>
    private void WriteNewFormat()
    {
        var entries = new SortedDictionary<string, LibraryEntry>(StringComparer.Ordinal)
        {
            ["claude:" + Id] = new LibraryEntry(Name: "Plan", Tags: ["alpha"], Note: "kept"),
            ["codex:019e0000"] = new LibraryEntry(Name: "Codex plan", Archived: true),
        };
        File.WriteAllText(PathIn(), JsonSerializer.Serialize(entries, Indented));
    }

    [Fact]
    public void ABareKeyedFileMovesToTheProviderFormWithItsMarksKept()
    {
        WriteOldFormat();

        var loaded = new SessionLibrary(PathIn()).Load()[Id];

        Assert.Equal("Plan", loaded.Name);
        Assert.True(loaded.Pinned);
        Assert.Equal(new[] { "alpha", "beta" }, loaded.Tags);
        Assert.Equal("kept", loaded.Note);
        Assert.True(loaded.Archived);
        var text = File.ReadAllText(PathIn());
        Assert.Contains("\"claude:" + Id + "\"", text);
        Assert.DoesNotContain("\"" + Id + "\"", text);
        Assert.True(new SessionLibrary(PathIn()).LoadAll(FileLock.DefaultWait).ContainsKey(new SessionKey(ProviderId.Claude, Id)));
    }

    [Fact]
    public void TheMigrationKeepsOneCopyOfTheOldBytes()
    {
        WriteOldFormat();
        var before = File.ReadAllBytes(PathIn());

        _ = new SessionLibrary(PathIn()).Load();

        var backup = Assert.Single(Backups());
        Assert.Matches(BackupName, Path.GetFileName(backup));
        Assert.Equal(before, File.ReadAllBytes(backup));
    }

    [Fact]
    public void ASecondLoadMakesNoSecondBackupAndChangesNothing()
    {
        WriteOldFormat();
        _ = new SessionLibrary(PathIn()).Load();
        var afterFirst = File.ReadAllBytes(PathIn());

        var second = new SessionLibrary(PathIn()).Load();

        Assert.Single(Backups());
        Assert.Equal(afterFirst, File.ReadAllBytes(PathIn()));
        Assert.Equal("Second", second[OtherId].Name);
    }

    [Fact]
    public void AnUpdateOfAnOldFileMigratesFirstAndKeepsTheOtherMarks()
    {
        WriteOldFormat();

        new SessionLibrary(PathIn()).Update(entries => entries[Id] = entries[Id] with { Note = "changed" });

        var keyed = new SessionLibrary(PathIn()).LoadAll(FileLock.DefaultWait);
        Assert.Equal("changed", keyed[new SessionKey(ProviderId.Claude, Id)].Note);
        Assert.Equal(new[] { "alpha", "beta" }, keyed[new SessionKey(ProviderId.Claude, Id)].Tags);
        Assert.Equal("Second", keyed[new SessionKey(ProviderId.Claude, OtherId)].Name);
        Assert.Single(Backups());
        Assert.DoesNotContain("\"" + Id + "\"", File.ReadAllText(PathIn()));
    }

    [Fact]
    public void AFileAlreadyInTheNewFormatIsNotChangedAndGetsNoBackup()
    {
        WriteNewFormat();
        var before = File.ReadAllBytes(PathIn());

        var keyed = new SessionLibrary(PathIn()).LoadAll(FileLock.DefaultWait);

        Assert.Equal(before, File.ReadAllBytes(PathIn()));
        Assert.Empty(Backups());
        Assert.Equal("Codex plan", keyed[new SessionKey(new ProviderId("codex"), "019e0000")].Name);
    }

    [Fact]
    public void AMissingLibraryIsEmptyAndGetsNoBackup()
    {
        Assert.Empty(new SessionLibrary(PathIn()).LoadAll(FileLock.DefaultWait));
        Assert.Empty(Backups());
        Assert.False(File.Exists(PathIn()));
    }

    [Fact]
    public void TheSameIdUnderTwoProvidersHasTwoEntriesThatDoNotMix()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateAll(entries =>
        {
            entries[new SessionKey(ProviderId.Claude, Id)] = new LibraryEntry(Name: "Claude plan", Tags: ["claude-tag"]);
            entries[new SessionKey(Fake, Id)] = new LibraryEntry(Name: "Fake plan", Archived: true, Tags: ["fake-tag"]);
        });

        var all = library.LoadAll(FileLock.DefaultWait);
        Assert.Equal(2, all.Count);
        Assert.Equal("Claude plan", all[new SessionKey(ProviderId.Claude, Id)].Name);
        Assert.Equal("Fake plan", all[new SessionKey(Fake, Id)].Name);
        Assert.True(all[new SessionKey(Fake, Id)].Archived);
        Assert.False(all[new SessionKey(ProviderId.Claude, Id)].Archived);

        // The bare-id view is Claude's: the fake entry is not in it, and the Claude entry is not the fake one.
        var claude = library.Load();
        Assert.Single(claude);
        Assert.Equal(new[] { "claude-tag" }, claude[Id].Tags);
        Assert.Contains("\"fake:" + Id + "\"", File.ReadAllText(PathIn()));
    }

    [Fact]
    public void AClaudeUpdateKeepsAnotherProvidersEntries()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateAll(entries => entries[new SessionKey(Fake, Id)] = new LibraryEntry(Name: "Fake plan", Note: "fake note"));

        library.Update(entries => entries[Id] = new LibraryEntry(Name: "Claude plan"));

        var all = library.LoadAll(FileLock.DefaultWait);
        Assert.Equal("fake note", all[new SessionKey(Fake, Id)].Note);
        Assert.Equal("Claude plan", all[new SessionKey(ProviderId.Claude, Id)].Name);
        Assert.Empty(Backups());
    }
}

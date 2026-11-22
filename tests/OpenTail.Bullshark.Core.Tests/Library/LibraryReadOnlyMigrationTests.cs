using System.Text.Json;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// Stage C-D8 part 1 (C6 i): an old-format library that cannot be written (a read-only file) still loads its marks in memory, logs one line,
/// and is not changed. A read does not throw. Temp folder and synthetic data only; the read-only attribute is cleared before the folder is deleted.
/// </summary>
public sealed class LibraryReadOnlyMigrationTests : IDisposable
{
    private const string Id = "17a94483-99fc-4005-9fa9-dbd63958653d";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-library-readonly-" + Guid.NewGuid().ToString("N"));

    private string PathIn => Path.Combine(_dir, "library.json");

    public LibraryReadOnlyMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        // A read-only file cannot be deleted, so the attribute is cleared on every file of the folder first.
        foreach (var file in Directory.GetFiles(_dir)) File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A file in the format before A5: the bare session id as the key.</summary>
    private void WriteOldFormat()
    {
        var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal)
        {
            [Id] = new(Name: "Plan", Tags: ["alpha"], Note: "kept"),
        };
        File.WriteAllText(PathIn, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string[] Backups() => Directory.GetFiles(_dir, "library.json.bak-*");

    [Fact]
    public void AReadOnlyOldFileLoadsInMemoryLogsOneLineAndIsNotChanged()
    {
        WriteOldFormat();
        File.SetAttributes(PathIn, FileAttributes.ReadOnly);
        var before = File.ReadAllBytes(PathIn);
        var lines = new List<string>();

        var loaded = new SessionLibrary(PathIn, lines.Add).Load();

        Assert.Equal("Plan", loaded[Id].Name);
        Assert.Equal("kept", loaded[Id].Note);
        var line = Assert.Single(lines);
        Assert.StartsWith("library: could not migrate (", line);
        Assert.EndsWith("marks are shown as they are on disk", line);
        Assert.Equal(before, File.ReadAllBytes(PathIn));
    }

    [Fact]
    public void ASecondReadLogsNothingNewAndMakesNoSecondCopy()
    {
        WriteOldFormat();
        File.SetAttributes(PathIn, FileAttributes.ReadOnly);
        var before = File.ReadAllBytes(PathIn);
        var lines = new List<string>();
        var library = new SessionLibrary(PathIn, lines.Add);

        _ = library.Load();
        var second = library.Load();

        Assert.Equal("Plan", second[Id].Name);
        Assert.Single(lines);
        var copy = Assert.Single(Backups());
        Assert.Equal(before, File.ReadAllBytes(copy));
        Assert.Equal(before, File.ReadAllBytes(PathIn));
    }

    [Fact]
    public void WhenTheFileIsWritableAgainTheNextReadMigratesIt()
    {
        WriteOldFormat();
        File.SetAttributes(PathIn, FileAttributes.ReadOnly);
        _ = new SessionLibrary(PathIn, _ => { }).Load();
        File.SetAttributes(PathIn, FileAttributes.Normal);

        var loaded = new SessionLibrary(PathIn, _ => { }).Load();

        Assert.Equal("Plan", loaded[Id].Name);
        Assert.Contains("\"claude:" + Id + "\"", File.ReadAllText(PathIn));
        Assert.Single(Backups());
    }
}

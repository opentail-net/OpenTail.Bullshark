using OpenTail.Bullshark.Core.Storage;

namespace OpenTail.Bullshark.Core.Tests.Storage;

public sealed class AppDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-migrate-" + Guid.NewGuid().ToString("N")[..8]);

    private string OldRoot => Path.Combine(_root, "OverClaude");
    private string NewRoot => Path.Combine(_root, "OpenTail.Bullshark");

    public AppDataMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void WriteOldData()
    {
        Directory.CreateDirectory(Path.Combine(OldRoot, "logs"));
        File.WriteAllText(Path.Combine(OldRoot, "state.json"), "{\"old\":true}");
        File.WriteAllText(Path.Combine(OldRoot, "runs.json"), "[]");
        File.WriteAllText(Path.Combine(OldRoot, "logs", "app.log"), "old log");
    }

    [Fact]
    public void CopiesTheOldDataIntoANewFolder()
    {
        WriteOldData();

        var copied = AppDataMigration.CopyPreviousIfMissing(OldRoot, NewRoot);

        Assert.Equal(3, copied);
        Assert.Equal("{\"old\":true}", File.ReadAllText(Path.Combine(NewRoot, "state.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(NewRoot, "runs.json")));
        Assert.Equal("old log", File.ReadAllText(Path.Combine(NewRoot, "logs", "app.log")));
    }

    [Fact]
    public void LeavesTheOldFolderInPlace()
    {
        WriteOldData();

        AppDataMigration.CopyPreviousIfMissing(OldRoot, NewRoot);

        Assert.True(File.Exists(Path.Combine(OldRoot, "state.json")));
        Assert.True(File.Exists(Path.Combine(OldRoot, "logs", "app.log")));
    }

    [Fact]
    public void DoesNothingWithoutOldState()
    {
        Directory.CreateDirectory(OldRoot);
        File.WriteAllText(Path.Combine(OldRoot, "runs.json"), "[]");

        Assert.Equal(0, AppDataMigration.CopyPreviousIfMissing(OldRoot, NewRoot));
        Assert.False(Directory.Exists(NewRoot));
    }

    [Fact]
    public void DoesNothingWhenTheNewStateAlreadyExists()
    {
        WriteOldData();
        Directory.CreateDirectory(NewRoot);
        File.WriteAllText(Path.Combine(NewRoot, "state.json"), "{\"new\":true}");

        Assert.Equal(0, AppDataMigration.CopyPreviousIfMissing(OldRoot, NewRoot));
        Assert.Equal("{\"new\":true}", File.ReadAllText(Path.Combine(NewRoot, "state.json")));
        Assert.False(File.Exists(Path.Combine(NewRoot, "runs.json")));
    }

    [Fact]
    public void CopiesWhenTheLogAlreadyCreatedTheNewFolder()
    {
        WriteOldData();
        Directory.CreateDirectory(Path.Combine(NewRoot, "logs"));
        File.WriteAllText(Path.Combine(NewRoot, "logs", "app.log"), "new log");

        AppDataMigration.CopyPreviousIfMissing(OldRoot, NewRoot);

        Assert.Equal("{\"old\":true}", File.ReadAllText(Path.Combine(NewRoot, "state.json")));
        Assert.Equal("new log", File.ReadAllText(Path.Combine(NewRoot, "logs", "app.log")));
    }
}

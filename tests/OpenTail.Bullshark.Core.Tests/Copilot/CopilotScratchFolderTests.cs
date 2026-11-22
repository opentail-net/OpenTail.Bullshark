using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>Stage G1.4 (G-D15): the scratch folders of the summary call. Every folder is under a temp folder that the test deletes.</summary>
public sealed class CopilotScratchFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-scratch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void TheParentIsCopilotScratchBesideTheStateFile()
    {
        var state = Path.Combine(_root, "app", "state.json");

        Assert.Equal(Path.Combine(_root, "app", "CopilotScratch"), CopilotScratchFolder.ParentBeside(state));
    }

    [Fact]
    public void CreateMakesAFolderWithTheMarkerFirstAndThreeEmptySubfolders()
    {
        var folder = CopilotScratchFolder.Create(Path.Combine(_root, "CopilotScratch"));

        Assert.True(CopilotScratchFolder.IsScratch(folder));
        Assert.True(File.Exists(Path.Combine(folder, CopilotScratchFolder.MarkerName)));
        Assert.Equal(new[] { CopilotScratchFolder.MarkerName }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
        Assert.Equal(
            new[] { CopilotScratchFolder.ConfigFolderName, CopilotScratchFolder.LogFolderName, CopilotScratchFolder.WorkFolderName },
            Directory.GetDirectories(folder).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folder, CopilotScratchFolder.WorkFolderName)));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folder, CopilotScratchFolder.ConfigFolderName)));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folder, CopilotScratchFolder.LogFolderName)));
        Assert.Equal(Path.Combine(_root, "CopilotScratch"), Path.GetDirectoryName(folder));
    }

    [Fact]
    public void DeleteRemovesAMarkedFolderWithItsFiles()
    {
        var folder = CopilotScratchFolder.Create(Path.Combine(_root, "CopilotScratch"));
        File.WriteAllText(Path.Combine(folder, "transcript.txt"), "synthetic");

        Assert.True(CopilotScratchFolder.Delete(folder));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void DeleteNeverRemovesAFolderWithoutTheMarker()
    {
        var folder = Path.Combine(_root, "not-ours");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "synthetic");

        Assert.False(CopilotScratchFolder.Delete(folder));
        Assert.True(File.Exists(Path.Combine(folder, "keep.txt")));
    }

    [Fact]
    public async Task DeleteAsyncRemovesAMarkedFolderWithItsFiles()
    {
        var folder = CopilotScratchFolder.Create(Path.Combine(_root, "CopilotScratch"));
        File.WriteAllText(Path.Combine(folder, "transcript.txt"), "synthetic");

        Assert.True(await CopilotScratchFolder.DeleteAsync(folder));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task DeleteAsyncNeverRemovesAFolderWithoutTheMarker()
    {
        var folder = Path.Combine(_root, "not-ours");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "synthetic");

        Assert.False(await CopilotScratchFolder.DeleteAsync(folder));
        Assert.True(File.Exists(Path.Combine(folder, "keep.txt")));
    }

    [Fact]
    public void StartupRemovesOnlyMarkedFoldersOlderThanOneDay()
    {
        var parent = Path.Combine(_root, "CopilotScratch");
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var oldMarked = CopilotScratchFolder.Create(parent);
        File.SetLastWriteTimeUtc(Path.Combine(oldMarked, CopilotScratchFolder.MarkerName), now.AddDays(-3).UtcDateTime);
        var newMarked = CopilotScratchFolder.Create(parent);
        File.SetLastWriteTimeUtc(Path.Combine(newMarked, CopilotScratchFolder.MarkerName), now.AddHours(-1).UtcDateTime);
        var oldUnmarked = Path.Combine(parent, "unmarked");
        Directory.CreateDirectory(oldUnmarked);
        Directory.SetLastWriteTimeUtc(oldUnmarked, now.AddDays(-3).UtcDateTime);

        // The one-day age is passed explicitly: the startup age is now zero, so a young marked folder (an hour old) is kept only at one day.
        var removed = CopilotScratchFolder.RemoveOld(parent, TimeSpan.FromDays(1), now);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(oldMarked));
        Assert.True(Directory.Exists(newMarked));
        Assert.True(Directory.Exists(oldUnmarked));
    }

    [Fact]
    public void AMarkedFolderLeftByAFailedDeleteIsRemovedAtTheNextStartup()
    {
        // OpenTail is single-instance, so a folder written minutes ago is not in use at startup: the startup age is zero and it is removed.
        var parent = Path.Combine(_root, "CopilotScratch");
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var left = CopilotScratchFolder.Create(parent);
        File.SetLastWriteTimeUtc(Path.Combine(left, CopilotScratchFolder.MarkerName), now.AddMinutes(-5).UtcDateTime);

        var removed = CopilotScratchFolder.RemoveOld(parent, CopilotScratchFolder.StartupAge, now);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(left));
    }

    [Fact]
    public void StartupOfAMissingParentIsZero()
    {
        Assert.Equal(0, CopilotScratchFolder.RemoveOld(Path.Combine(_root, "missing"), TimeSpan.Zero, DateTimeOffset.UtcNow));
    }
}

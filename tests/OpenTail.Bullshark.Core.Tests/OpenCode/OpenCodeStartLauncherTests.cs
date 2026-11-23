using OpenTail.Bullshark.Core.OpenCode;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// The opencode start files (OC-D16): the start-up sweep removes only old files of the app's own name, and the opencode Starts folder sits beside the
/// state file, apart from the Copilot one. Every folder is under a temp folder that the test deletes.
/// </summary>
public sealed class OpenCodeStartLauncherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 15, TimeSpan.FromHours(2));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "opentail-oc-starts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string WriteFile(string folder, string name, DateTimeOffset written)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "text");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }

    [Fact]
    public void TheStartFileAgeIsOneDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), OpenCodeStartLauncher.StartFileAge);
    }

    [Fact]
    public void TheStartsFolderIsOpencodeInsideStartsBesideTheStateFile()
    {
        var state = Path.Combine(_root, "app", "state.json");

        Assert.Equal(Path.Combine(_root, "app", "Starts", "opencode"), OpenCodeStartLauncher.StartsFolderBeside(state));
    }

    [Fact]
    public void StartupRemovesOnlyOldStartFilesOfTheAppsOwnName()
    {
        var folder = OpenCodeStartLauncher.StartsFolderBeside(Path.Combine(_root, "app", "state.json"));
        // A start file the app writes itself, made old by its write time.
        var written = OpenCodeStartLauncher.WriteStart(folder, "start text", Now.AddDays(-3));
        File.SetLastWriteTimeUtc(written, Now.AddDays(-3).UtcDateTime);
        var newStart = WriteFile(folder, "20261122-110000-1f2e3d4c.txt", Now.AddHours(-1));
        var oldJson = WriteFile(folder, "20261119-100000-0a1b2c3d.json", Now.AddDays(-3));
        var oldNotes = WriteFile(folder, "notes.txt", Now.AddDays(-3));
        var oldUpperHex = WriteFile(folder, "20261119-100000-9F8E7D6C.txt", Now.AddDays(-3));

        var removed = OpenCodeStartLauncher.RemoveOldStarts(folder, OpenCodeStartLauncher.StartFileAge, Now);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(written));
        Assert.True(File.Exists(newStart));
        Assert.True(File.Exists(oldJson));
        Assert.True(File.Exists(oldNotes));
        Assert.True(File.Exists(oldUpperHex));
    }

    [Fact]
    public void StartupOfAMissingStartsFolderIsZero()
    {
        Assert.Equal(0, OpenCodeStartLauncher.RemoveOldStarts(Path.Combine(_root, "missing"), OpenCodeStartLauncher.StartFileAge, Now));
    }
}

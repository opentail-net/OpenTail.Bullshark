using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC2e (Part 1): the shim finder of the opencode setup check. It reads the folders of a PATH value and file names only, on temp folders that hold
/// empty files. No program is started.
/// </summary>
public sealed class OpenCodeShimFinderTests
{
    private sealed class TempFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "bullshark-opencode-shim-" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Folder);

        public void Dispose()
        {
            if (!Directory.Exists(Folder)) return;
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void FindsOpencodeCmdInTheGivenPath()
    {
        using var temp = new TempFolder();
        var shim = Path.Combine(temp.Folder, "opencode.cmd");
        File.WriteAllText(shim, string.Empty);

        Assert.Equal(shim, OpenCodeSetupCheck.FindShimOnPath(temp.Folder));
    }

    [Fact]
    public void APathWithNoOpencodeFileFindsNothing()
    {
        using var temp = new TempFolder();

        Assert.Null(OpenCodeSetupCheck.FindShimOnPath(temp.Folder));
    }

    [Fact]
    public void FindsOpencodeExeWhenThereIsNoCmd()
    {
        using var temp = new TempFolder();
        var exe = Path.Combine(temp.Folder, "opencode.exe");
        File.WriteAllText(exe, string.Empty);

        Assert.Equal(exe, OpenCodeSetupCheck.FindShimOnPath(temp.Folder));
    }

    [Fact]
    public void FindsTheShimInALaterFolderOfThePathValue()
    {
        using var empty = new TempFolder();
        using var holder = new TempFolder();
        var shim = Path.Combine(holder.Folder, "opencode.cmd");
        File.WriteAllText(shim, string.Empty);

        var pathValue = empty.Folder + Path.PathSeparator + holder.Folder;

        Assert.Equal(shim, OpenCodeSetupCheck.FindShimOnPath(pathValue));
    }
}

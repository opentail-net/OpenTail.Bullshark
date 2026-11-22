using System.IO;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The session's folder made again (<see cref="SessionFolder"/>): the pure decision, and the disk work on temp folders that the test makes and removes. No
/// state folder is read or written, and no CLI runs.
/// </summary>
public sealed class SessionFolderTests
{
    private const string Recorded = @"C:\Work\example";

    /// <summary>A temp root for one test. The test makes it and removes it.</summary>
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "opentail-folder-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void AFolderThatExistsIsLeftAloneWhateverItsDrive(bool exists, bool driveExists)
    {
        Assert.Equal(FolderMake.Exists, SessionFolder.MakeFolder(Recorded, exists, driveExists));
    }

    [Fact]
    public void AMissingFolderOnAPresentDriveIsMade()
    {
        Assert.Equal(FolderMake.Made, SessionFolder.MakeFolder(Recorded, exists: false, driveExists: true));
    }

    [Fact]
    public void AMissingFolderOnAMissingDriveIsNotMade()
    {
        Assert.Equal(FolderMake.DriveMissing, SessionFolder.MakeFolder(Recorded, exists: false, driveExists: false));
    }

    [Fact]
    public void ABlankFolderIsRejected()
    {
        Assert.Throws<ArgumentException>(() => SessionFolder.MakeFolder("  ", exists: false, driveExists: true));
    }

    [Fact]
    public void TwoMissingLevelsUnderATempRootAreMadeInFullAndCountedAsFolders()
    {
        var root = NewRoot();
        try
        {
            Directory.CreateDirectory(root);
            var recorded = Path.Combine(root, "first", "second");

            Assert.Equal(FolderMake.Made, SessionFolder.Ensure(recorded, out var made));

            Assert.Equal(2, made);
            Assert.True(Directory.Exists(Path.Combine(root, "first")));
            Assert.True(Directory.Exists(recorded));
            // A second call finds the folder there: nothing is made, and the count is 0.
            Assert.Equal(FolderMake.Exists, SessionFolder.Ensure(recorded, out var again));
            Assert.Equal(0, again);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ARelativePathIsNotMadeAtAll()
    {
        var relative = "opentail-relative-" + Guid.NewGuid().ToString("N");

        Assert.Equal(FolderMake.Invalid, SessionFolder.Ensure(relative, out var made));

        Assert.Equal(0, made);
        Assert.False(Directory.Exists(Path.GetFullPath(relative)));
    }

    [Fact]
    public void APathOnADriveThatIsNotThereIsNotMade()
    {
        // The first drive letter from Q that is not on this machine. A machine with every one of them in use has no such letter: the pure rule above
        // still covers the refusal then.
        var used = Directory.GetLogicalDrives();
        var free = Enumerable.Range('Q', 'Z' - 'Q' + 1)
            .Select(letter => $"{(char)letter}:\\")
            .FirstOrDefault(root => !used.Any(drive => string.Equals(drive, root, StringComparison.OrdinalIgnoreCase)));
        if (free is null) return;

        var recorded = Path.Combine(free, "opentail-missing-" + Guid.NewGuid().ToString("N"), "folder");

        Assert.Equal(FolderMake.DriveMissing, SessionFolder.Ensure(recorded, out var made));

        Assert.Equal(0, made);
        Assert.False(Directory.Exists(recorded));
    }
}

using OpenTail.Bullshark.Core.Instructions;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Instructions;

/// <summary>
/// A CLAUDE.md the user cannot reach is an error, not a missing file (round 5 item 17): the editor must not open it as an empty file, and a save
/// must not write over it. Windows only: it needs a folder the user cannot open.
/// </summary>
public sealed class UnreadableInstructionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-instructions-" + Guid.NewGuid().ToString("N")[..8]);

    public UnreadableInstructionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AMissingFileReadsAsEmptyAndHasNoStamp()
    {
        var path = Path.Combine(_dir, "CLAUDE.md");

        Assert.Equal(string.Empty, InstructionFiles.Read(path));
        Assert.Null(FileStamp.Of(path));
    }

    [Fact]
    public void AnUnreadableFileIsAnErrorAndIsNeitherEmptyNorMissing()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "CLAUDE.md");
        File.WriteAllText(path, "# Rules\nBe brief.\n");

        using (Unreadable.FolderWithoutMe(_dir))
        {
            var readError = Record.Exception(() => InstructionFiles.Read(path));
            var stampError = Record.Exception(() => FileStamp.Of(path));

            Assert.True(readError is UnauthorizedAccessException or IOException, $"read: {readError?.GetType().Name ?? "no error"}");
            Assert.True(stampError is UnauthorizedAccessException or IOException, $"stamp: {stampError?.GetType().Name ?? "no error"}");
        }
    }

    [Fact]
    public void ASaveOverAnUnreachableFileWritesNothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "CLAUDE.md");
        File.WriteAllText(path, "# Rules\nBe brief.\n");
        // The stamp is taken while the file can be reached, as the editor does when it opens the file.
        var stamp = FileStamp.Of(path);

        using (Unreadable.FolderWithoutMe(_dir))
        {
            // The stamp cannot be checked now, so the save is refused as failed, and it is not taken for a change on disk.
            var result = InstructionFiles.Save(path, "replaced", stamp);
            Assert.Equal(SaveOutcome.Failed, result.Outcome);
        }

        Assert.Equal("# Rules\nBe brief.\n", File.ReadAllText(path));
    }
}

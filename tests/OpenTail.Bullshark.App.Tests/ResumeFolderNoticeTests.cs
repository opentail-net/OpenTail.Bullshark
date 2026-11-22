using System.IO;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The folder notice of a resume screen (<see cref="ResumeFolderNotice"/>): its exact words, and whether it blocks Start. Pure: no window and no file
/// is read; the folder that exists is the temp folder.
/// </summary>
public sealed class ResumeFolderNoticeTests
{
    private const string NoFolderText =
        "The session has no folder on record, so it cannot be started. Summary and last turns can still be previewed.";

    [Fact]
    public void ANullFolderHasNoFolderOnRecordWhetherOrNotItExists()
    {
        Assert.Equal(NoFolderText, ResumeFolderNotice.For(null, folderExists: false));
        Assert.Equal(NoFolderText, ResumeFolderNotice.For(null, folderExists: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankFolderHasNoFolderOnRecordWhetherOrNotItExists(string folder)
    {
        Assert.Equal(NoFolderText, ResumeFolderNotice.For(folder, folderExists: false));
        Assert.Equal(NoFolderText, ResumeFolderNotice.For(folder, folderExists: true));
    }

    [Fact]
    public void AMissingFolderNamesTheFolderAndSaysWhatIsStillPossible()
    {
        Assert.Equal(
            "The session's folder is missing: C:\\Work\\gone. Start needs it, so it is blocked. Summary and last turns can still be previewed.",
            ResumeFolderNotice.For("C:\\Work\\gone", folderExists: false));
    }

    [Fact]
    public void AFolderThatExistsHasNoNotice()
    {
        Assert.Null(ResumeFolderNotice.For(Path.GetTempPath(), folderExists: true));
    }

    [Fact]
    public void OnlyAPresentNoticeBlocksStart()
    {
        Assert.False(ResumeFolderNotice.BlocksStart(null));
        Assert.True(ResumeFolderNotice.BlocksStart(NoFolderText));
        Assert.True(ResumeFolderNotice.BlocksStart(ResumeFolderNotice.For("C:\\Work\\gone", folderExists: false)));
    }
}

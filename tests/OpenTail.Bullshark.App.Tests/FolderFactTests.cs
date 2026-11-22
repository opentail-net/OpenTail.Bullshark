using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The folder part of a resume screen's facts line (<see cref="FolderFact"/>): "in {folder}" for a folder on record, and the words for none otherwise.
/// Pure: no window is made and no folder is read.
/// </summary>
public sealed class FolderFactTests
{
    [Fact]
    public void AFolderOnRecordGivesTheSameWordsAsBefore()
    {
        Assert.Equal("in C:\\Work\\example", FolderFact.For("C:\\Work\\example"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFolderOnRecordGivesTheNoFolderWords(string? folder)
    {
        Assert.Equal("no folder on record", FolderFact.For(folder));
    }
}

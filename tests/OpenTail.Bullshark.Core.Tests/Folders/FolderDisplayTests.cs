using OpenTail.Bullshark.Core.Folders;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Folders;

/// <summary>
/// The name a folder group is shown by (P9): the leaf of the working folder its transcripts name, not Claude's lossy encoded name. The group key
/// is unchanged. A folder counts only when it encodes to the group's key, so the name and the key always name the same folder.
/// </summary>
public sealed class FolderDisplayTests
{
    private const string Key = "C--Git-split-OpenTail-Bullshark";

    /// <summary>The key Claude gives a folder: the group's key for a test that names that folder.</summary>
    private static string KeyOf(string folder) => FolderKey.From(folder);

    [Fact]
    public void TheLeafOfTheNamedFolderIsTheNameAndTheWholePathIsTheTooltip()
    {
        var label = FolderDisplay.Of(Key, [@"C:\Git\split\OpenTail.Bullshark"]);

        Assert.Equal("OpenTail.Bullshark", label.Name);
        Assert.Equal(@"C:\Git\split\OpenTail.Bullshark", label.FullPath);
        // The name is a label for the same group: the encoded key of the full path is the group's key.
        Assert.Equal(Key, FolderKey.From(label.FullPath!));
    }

    [Fact]
    public void AShortFolderOnTheDriveIsNamedAsWritten()
    {
        // C--Git is C:\Git: the case that looked like a bug on the owner's rail.
        var label = FolderDisplay.Of("C--Git", [@"C:\Git"]);

        Assert.Equal("Git", label.Name);
        Assert.Equal(@"C:\Git", label.FullPath);
    }

    [Fact]
    public void TheFolderNamedMostOftenWinsAmongTheOnesThatEncodeToTheKey()
    {
        // Claude's encoding is lossy: my-repo and my\repo are the same key, so both count for the group.
        var key = KeyOf(@"C:\Git\my-repo");
        var label = FolderDisplay.Of(key, [@"C:\Git\my-repo", @"C:\Git\my\repo", @"C:\Git\my\repo"]);

        Assert.Equal("repo", label.Name);
        Assert.Equal(@"C:\Git\my\repo", label.FullPath);
    }

    [Fact]
    public void ATieGoesToTheFirstFolderInOrdinalOrder()
    {
        var key = KeyOf(@"C:\Git\my-repo");
        var one = FolderDisplay.Of(key, [@"C:\Git\my\repo", @"C:\Git\my-repo"]);
        var other = FolderDisplay.Of(key, [@"C:\Git\my-repo", @"C:\Git\my\repo"]);

        // '-' sorts before the separator, so the dashed spelling is the first in ordinal order.
        Assert.Equal("my-repo", one.Name);
        Assert.Equal(one, other);
    }

    [Fact]
    public void AFolderThatEncodesToAnotherKeyDoesNotNameTheGroup()
    {
        // The session moved on to a sub-folder: its last cwd is not this group's folder, so the group keeps its key.
        var label = FolderDisplay.Of("C--Git-Public", [@"C:\Git\Public\OpenTail.Stingray", @"C:\Git\Public\OpenTail.Stingray"]);

        Assert.Equal(new FolderLabel("C--Git-Public", null), label);
    }

    [Fact]
    public void FoldersAreCountedWithoutCaseAndTheSpellingMostWrittenIsShown()
    {
        var label = FolderDisplay.Of("C--Git-Repo", [@"C:\Git\Repo", @"c:\git\repo", @"C:\Git\Repo"]);

        Assert.Equal("Repo", label.Name);
        Assert.Equal(@"C:\Git\Repo", label.FullPath);
    }

    [Fact]
    public void ATrailingSeparatorDoesNotMakeAnotherFolder()
    {
        var label = FolderDisplay.Of("C--Git-repo", [@"C:\Git\repo\", @"C:\Git\repo", @"C:\Git\repo\"]);

        Assert.Equal("repo", label.Name);
        Assert.Equal(@"C:\Git\repo", label.FullPath);
    }

    [Fact]
    public void ADriveRootIsItsOwnName()
    {
        var label = FolderDisplay.Of("C--", [@"C:\"]);

        Assert.Equal(@"C:\", label.Name);
        Assert.Equal(@"C:\", label.FullPath);
    }

    [Fact]
    public void AUncFolderIsNamedByItsLastPart()
    {
        var label = FolderDisplay.Of("--server-share-dir", [@"\\server\share\dir"]);

        Assert.Equal("dir", label.Name);
        Assert.Equal(@"\\server\share\dir", label.FullPath);
    }

    [Fact]
    public void NullBlankRelativeAndOtherKeyFoldersAreNotCounted()
    {
        var label = FolderDisplay.Of(Key, [null, "", "   ", @"relative\folder", @"C:\Git\split\OpenTail.Bullshark", @"C:\Other\OpenTail.Bullshark"]);

        Assert.Equal("OpenTail.Bullshark", label.Name);
        Assert.Equal(@"C:\Git\split\OpenTail.Bullshark", label.FullPath);
    }

    [Fact]
    public void AnUnderscoreOrDotInTheLeafIsKeptAsWritten()
    {
        var label = FolderDisplay.Of(KeyOf(@"C:\Git\a.b_c"), [@"C:\Git\a.b_c"]);

        Assert.Equal("a.b_c", label.Name);
    }

    [Fact]
    public void WithNoFolderKnownTheNameIsTheKeyAndThereIsNoTooltipPath()
    {
        Assert.Equal(new FolderLabel(Key, null), FolderDisplay.Of(Key, []));
        Assert.Equal(new FolderLabel(Key, null), FolderDisplay.Of(Key, [null, " "]));
    }

    [Fact]
    public void LabelsForGivesEachKeyItsOwnFolderName()
    {
        var labels = FolderDisplay.LabelsFor(
            ["C--Git-repo", "C--Other-tool"],
            [("C--Git-repo", @"C:\Git\repo"), ("C--Other-tool", @"C:\Other\tool")]);

        Assert.Equal("repo", labels["C--Git-repo"].Name);
        Assert.Equal("tool", labels["C--Other-tool"].Name);
        Assert.Equal(@"C:\Git\repo", labels["C--Git-repo"].FullPath);
    }

    [Fact]
    public void TwoKeysWithOneLeafNameGetTheParentFolderInBrackets()
    {
        var labels = FolderDisplay.LabelsFor(
            ["C--Git-work", "C--Other-work"],
            [("C--Git-work", @"C:\Git\work"), ("C--Other-work", @"C:\Other\work")]);

        Assert.Equal("work (Git)", labels["C--Git-work"].Name);
        Assert.Equal("work (Other)", labels["C--Other-work"].Name);
    }

    [Fact]
    public void AStillSharedNameGetsTheKeyInBrackets()
    {
        // Same leaf and same parent folder name on two drives: the parent does not tell them apart, so the key does.
        var labels = FolderDisplay.LabelsFor(
            ["C--x-repo", "D--x-repo"],
            [("C--x-repo", @"C:\x\repo"), ("D--x-repo", @"D:\x\repo")]);

        Assert.Equal("repo (C--x-repo)", labels["C--x-repo"].Name);
        Assert.Equal("repo (D--x-repo)", labels["D--x-repo"].Name);
    }

    [Fact]
    public void AKeyWithNoKnownFolderKeepsItsKeyAsTheName()
    {
        var labels = FolderDisplay.LabelsFor(["(root)", "C--Old-gone"], [("C--Old-gone", null)]);

        Assert.Equal(new FolderLabel("(root)", null), labels["(root)"]);
        Assert.Equal(new FolderLabel("C--Old-gone", null), labels["C--Old-gone"]);
    }

    [Fact]
    public void KeysAreMatchedWithoutCase()
    {
        var labels = FolderDisplay.LabelsFor(["c--git-repo"], [("C--Git-repo", @"C:\Git\repo")]);

        Assert.Equal("repo", labels["C--GIT-REPO"].Name);
        Assert.Single(labels);
    }

    [Fact]
    public void ARepeatedKeyIsOneLabel()
    {
        var labels = FolderDisplay.LabelsFor(["C--Git-repo", "C--Git-repo"], [("C--Git-repo", @"C:\Git\repo")]);

        Assert.Single(labels);
        Assert.Equal("repo", labels["C--Git-repo"].Name);
    }
}

using OpenTail.Bullshark.Core.Git;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Git;

/// <summary>
/// The big-deletion check (features batch G2): the thresholds at their edges, a deleted file, a binary file, unknown counts and a rename.
/// The check reads the list the Changes tab already has; no git runs here.
/// </summary>
public sealed class DeletionCheckTests
{
    private static GitFileChange Modified(int? added, int? deleted, bool binary = false) =>
        new("notes.md", null, " M", added, deleted, binary);

    [Fact]
    public void FiftyDeletedLinesAgainstTheThreeTimesRuleIsFlaggedAtExactlyThat()
    {
        // 50 removed and 17 added: 3 * 17 = 51 > 50, so no flag. 50 removed and 16 added: 3 * 16 = 48 <= 50, so the flag.
        Assert.Null(DeletionCheck.Flag(Modified(added: 17, deleted: 50)));
        Assert.Equal("mostly deleted (-50 +16)", DeletionCheck.Flag(Modified(added: 16, deleted: 50)));
    }

    [Fact]
    public void FortyNineDeletedLinesAreNotEnoughEvenWhenTheyAreMostOfTheFile()
    {
        Assert.Null(DeletionCheck.Flag(Modified(added: 0, deleted: 49)));
        Assert.Equal("mostly deleted (-50 +0)", DeletionCheck.Flag(Modified(added: 0, deleted: 50)));
    }

    [Fact]
    public void ThreeTimesExactlyIsFlaggedAndOneLessIsNot()
    {
        Assert.Equal("mostly deleted (-60 +20)", DeletionCheck.Flag(Modified(added: 20, deleted: 60)));
        Assert.Null(DeletionCheck.Flag(Modified(added: 21, deleted: 60)));
    }

    [Fact]
    public void ADeletedFileIsFlaggedWhateverItsSize()
    {
        var deleted = new GitFileChange("retired.md", null, " D", 0, 3, false);

        Assert.Equal("deleted", DeletionCheck.Flag(deleted));
        Assert.Equal("deleted", DeletionCheck.Flag(new GitFileChange("staged.md", null, "D ", null, null, false)));
    }

    [Fact]
    public void ABinaryFileWhoseCountsAreUnknownIsNotFlaggedAsMostlyDeleted()
    {
        Assert.Null(DeletionCheck.Flag(Modified(added: null, deleted: null, binary: true)));
    }

    [Fact]
    public void UnknownCountsNeverFlag()
    {
        Assert.Null(DeletionCheck.Flag(Modified(added: null, deleted: 500)));
        Assert.Null(DeletionCheck.Flag(Modified(added: 0, deleted: null)));
        Assert.Null(DeletionCheck.Flag(new GitFileChange("new.txt", null, "??", null, null, false)));
    }

    [Fact]
    public void ARenamedFileIsJudgedByItsCountsLikeAnyOther()
    {
        var renamedAlmostEmptied = new GitFileChange("moved.md", "old.md", "R ", 2, 80, false);
        var renamedUnchanged = new GitFileChange("moved.md", "old.md", "R ", 0, 0, false);

        Assert.Equal("mostly deleted (-80 +2)", DeletionCheck.Flag(renamedAlmostEmptied));
        Assert.Null(DeletionCheck.Flag(renamedUnchanged));
    }

    [Fact]
    public void AModifiedFileThatGainedLinesIsNotFlagged()
    {
        Assert.Null(DeletionCheck.Flag(Modified(added: 400, deleted: 60)));
    }
}

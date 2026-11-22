using OpenTail.Bullshark.App;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// G2.2 and G2.5 (wording): the empty state says why it is empty, where the app looked, and how many folders were left out; a read that skipped
/// something says so; a refused resume says why. All pure: no window, no file. The lines carry counts and paths only.
/// </summary>
public sealed class CopilotRailTextTests
{
    private const string Root = @"C:\Users\someone\.copilot\session-state";

    [Fact]
    public void TheEmptyHeadingNamesCopilot()
    {
        Assert.Equal("No Copilot sessions found", CopilotRailText.EmptyHeading);
    }

    [Fact]
    public void TheEmptyReasonGivesTheFolderAndTheWayToStartOne()
    {
        var text = CopilotRailText.EmptyReason(Root, emptySessions: 0, damagedFolders: 0);

        Assert.Contains($"Copilot sessions are read from {Root}.", text);
        Assert.Contains("Start one with New Copilot session", text);
        Assert.DoesNotContain("not listed", text);
    }

    [Fact]
    public void TheEmptySessionCountIsSaidWithItsReason()
    {
        Assert.Contains("1 session folder holds a workspace file but no events, so not listed.",
            CopilotRailText.EmptyReason(Root, emptySessions: 1, damagedFolders: 0));
        Assert.Contains("5 session folders hold a workspace file but no events, so not listed.",
            CopilotRailText.EmptyReason(Root, emptySessions: 5, damagedFolders: 0));
    }

    [Fact]
    public void TheDamagedFolderCountIsSaid()
    {
        Assert.Contains("1 session folder could not be read, so not listed.",
            CopilotRailText.EmptyReason(Root, emptySessions: 0, damagedFolders: 1));
        Assert.Contains("2 session folders could not be read, so not listed.",
            CopilotRailText.EmptyReason(Root, emptySessions: 0, damagedFolders: 2));
    }

    [Fact]
    public void TheSkippedLineCountIsSaidInTheEmptyReason()
    {
        Assert.Contains("1 line of the session files was skipped (not a record, or too long).",
            CopilotRailText.EmptyReason(Root, emptySessions: 0, damagedFolders: 0, skippedLines: 1));
        Assert.Contains("1,234 lines of the session files were skipped (not records, or too long).",
            CopilotRailText.EmptyReason(Root, emptySessions: 0, damagedFolders: 0, skippedLines: 1234));
        Assert.DoesNotContain("skipped", CopilotRailText.EmptyReason(Root, emptySessions: 0, damagedFolders: 0));
    }

    [Fact]
    public void ACleanReadHasNoIncompleteNote()
    {
        Assert.Null(CopilotRailText.IncompleteNote(damagedFolders: 0, skippedLines: 0));
    }

    [Fact]
    public void ASkippedLineOrFolderMakesTheHistoryMayBeIncomplete()
    {
        Assert.Equal("Copilot history may be incomplete: 1 line skipped.",
            CopilotRailText.IncompleteNote(damagedFolders: 0, skippedLines: 1));
        Assert.Equal("Copilot history may be incomplete: 2 folders could not be read; 3 lines skipped.",
            CopilotRailText.IncompleteNote(damagedFolders: 2, skippedLines: 3));
    }

    [Fact]
    public void ANotListedResumeIsRefusedWithItsReason()
    {
        Assert.StartsWith("Not resumed:", CopilotRailText.NotListed);
    }

    [Fact]
    public void AMissingFolderIsNamedInTheRefusal()
    {
        Assert.Equal(@"Not resumed: the session's folder is missing: D:\gone", CopilotRailText.FolderMissing(@"D:\gone"));
    }

    [Fact]
    public void AResumeWithNoFolderOnRecordSaysSo()
    {
        Assert.Equal("Not resumed: the session has no folder on record, so Copilot cannot open it there.", CopilotRailText.FolderMissing(null));
        Assert.Equal("Not resumed: the session has no folder on record, so Copilot cannot open it there.", CopilotRailText.FolderMissing("  "));
    }

    [Fact]
    public void TheResumeScreenItemSaysItIsNotBuiltYet()
    {
        Assert.Contains("not built yet", CopilotRailText.ResumeScreenNotBuilt);
    }
}

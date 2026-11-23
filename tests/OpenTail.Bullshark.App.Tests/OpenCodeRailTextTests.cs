using OpenTail.Bullshark.App;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// OC2 (wording): the opencode rail's texts are the Copilot rail's with the provider's name, and their exact strings are fixed here. No line names
/// Copilot, and none repeats the database-unavailable phrases (those come from OpenCodeHistorySource.TextOf). All pure: no window, no file, no process.
/// </summary>
public sealed class OpenCodeRailTextTests
{
    private const string Database = @"C:\Fixture\opencode\opencode.db";

    [Fact]
    public void TheEmptyHeadingNamesOpenCode()
    {
        Assert.Equal("No opencode sessions found", OpenCodeRailText.EmptyHeading);
    }

    [Fact]
    public void TheEmptyReasonNamesTheDatabaseAndWhatMakesASessionShow()
    {
        Assert.Equal(
            $"opencode sessions are read from {Database}.\nA session shows once opencode has saved it in that database, as a top-level session with a folder on record.",
            OpenCodeRailText.EmptyReason(Database));
    }

    [Fact]
    public void TheSkippedRowCountIsSaidInTheEmptyReason()
    {
        Assert.Contains("1 row of the database was skipped, as it could not be used.",
            OpenCodeRailText.EmptyReason(Database, skippedRows: 1));
        Assert.Contains("1,234 rows of the database were skipped, as it could not be used.",
            OpenCodeRailText.EmptyReason(Database, skippedRows: 1234));
        Assert.DoesNotContain("skipped", OpenCodeRailText.EmptyReason(Database, skippedRows: 0));
    }

    [Fact]
    public void ACleanReadHasNoIncompleteNote()
    {
        Assert.Null(OpenCodeRailText.IncompleteNote(0));
        Assert.Null(OpenCodeRailText.IncompleteNote(-1));
    }

    [Fact]
    public void ASkippedRowIsSaidInTheIncompleteNote()
    {
        Assert.Equal("opencode history may be incomplete: 1 row skipped.", OpenCodeRailText.IncompleteNote(1));
        Assert.Equal("opencode history may be incomplete: 2 rows skipped.", OpenCodeRailText.IncompleteNote(2));
    }

    [Fact]
    public void ARefusedResumeWhoseSessionIsNotListedSaysSo()
    {
        Assert.Equal("Not resumed: this opencode session is not in the list. Reload the opencode history, then try again.", OpenCodeRailText.NotListed);
    }

    [Fact]
    public void AResumeWithNoFolderOnRecordSaysOpenCodeCannotOpenIt()
    {
        const string expected = "Not resumed: the session has no folder on record, so opencode cannot open it there.";
        Assert.Equal(expected, OpenCodeRailText.FolderMissing(null));
        Assert.Equal(expected, OpenCodeRailText.FolderMissing("   "));
    }

    [Fact]
    public void AResumeWithAMissingFolderNamesTheFolder()
    {
        Assert.Equal(@"Not resumed: the session's folder is missing: C:\Work\gone", OpenCodeRailText.FolderMissing(@"C:\Work\gone"));
    }

    [Fact]
    public void AResumeWithAnInvalidIdSaysItIsNotAnOpenCodeId()
    {
        Assert.Equal("Not resumed: the session id is not a valid opencode id.", OpenCodeRailText.InvalidId);
    }

    [Fact]
    public void AFailedReadNamesOnlyTheExceptionType()
    {
        Assert.Equal("opencode history not read: SqliteException", OpenCodeRailText.ReadFailed("SqliteException"));
    }

    [Fact]
    public void TheRailLinesForTheReadAndTheFailureAreFixed()
    {
        Assert.Equal("opencode history could not be read", OpenCodeRailText.FailedHeading);
        Assert.Equal("The status line says why. Reload to try again.", OpenCodeRailText.FailedTip);
        Assert.Equal("reading the opencode history…", OpenCodeRailText.Reading);
        Assert.Equal("Reload the opencode history from its database (no model)", OpenCodeRailText.ReloadTip);
    }

    [Fact]
    public void TheLiveNoteSaysOpenCodeLiveSessionsAreNotShown()
    {
        Assert.Equal("opencode live sessions are not shown", OpenCodeRailText.LiveHeading);
        Assert.Equal("This window shows opencode history and the terminal tabs it opens. Live opencode sessions started elsewhere are not shown, and nothing is supervised.",
            OpenCodeRailText.LiveTip);
    }

    [Fact]
    public void TheTagAndArchivedEmptyLinesNameOpenCode()
    {
        Assert.Equal("No opencode session has the tag #work", OpenCodeRailText.TagEmpty("work"));
        Assert.Equal("The tag filter shows the opencode sessions that carry the tag. Clear it from the filter menu.", OpenCodeRailText.TagEmptyTip);
        Assert.Equal("No opencode session is archived", OpenCodeRailText.ArchivedEmpty);
        Assert.Equal("Archive an opencode session from its menu, and it is listed here.", OpenCodeRailText.ArchivedEmptyTip);
    }

    [Fact]
    public void TheSearchLinesNameOpenCodeAndCountTheMatches()
    {
        Assert.Equal("The opencode history could not be read, so there is nothing to search. Reload it to try again.", OpenCodeRailText.SearchNotRead(failed: true));
        Assert.Equal("The opencode history is still being read. Search again in a moment.", OpenCodeRailText.SearchNotRead(failed: false));
        Assert.Equal("No opencode session matches \"auth\".", OpenCodeRailText.SearchNoMatch("auth"));
        Assert.Equal("2 of 9 opencode sessions match \"auth\". Newest first.", OpenCodeRailText.SearchMatches(2, 9, "auth"));
    }

    [Fact]
    public void NoOpenCodeTextNamesCopilotOrRepeatsTheDatabasePhrases()
    {
        var texts = new[]
        {
            OpenCodeRailText.EmptyHeading,
            OpenCodeRailText.EmptyReason(Database, skippedRows: 2),
            OpenCodeRailText.IncompleteNote(2)!,
            OpenCodeRailText.NotListed,
            OpenCodeRailText.FolderMissing(null),
            OpenCodeRailText.FolderMissing(@"C:\x"),
            OpenCodeRailText.InvalidId,
            OpenCodeRailText.ReadFailed("X"),
            OpenCodeRailText.FailedHeading,
            OpenCodeRailText.FailedTip,
            OpenCodeRailText.Reading,
            OpenCodeRailText.ReloadTip,
            OpenCodeRailText.LiveHeading,
            OpenCodeRailText.LiveTip,
            OpenCodeRailText.TagEmpty("t"),
            OpenCodeRailText.TagEmptyTip,
            OpenCodeRailText.ArchivedEmpty,
            OpenCodeRailText.ArchivedEmptyTip,
            OpenCodeRailText.SearchNotRead(true),
            OpenCodeRailText.SearchNotRead(false),
            OpenCodeRailText.SearchNoMatch("q"),
            OpenCodeRailText.SearchMatches(1, 1, "q"),
        };
        foreach (var text in texts)
        {
            Assert.DoesNotContain("Copilot", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("history not available", text);
        }
    }
}

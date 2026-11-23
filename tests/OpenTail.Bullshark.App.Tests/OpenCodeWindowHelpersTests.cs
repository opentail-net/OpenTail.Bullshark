using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// OC2 (the pure helpers of the opencode window partials): the row title, the archive mark opencode's own flag gives, and the conversion of the reader's
/// "kind: text" lines into the shared transcript lines. No window, no database, no process.
/// </summary>
public sealed class OpenCodeWindowHelpersTests
{
    private const string Id = "ses_0123456789abcdefABCDEFGHIJ";

    private static SessionSummary Summary(string? title) =>
        new(new SessionKey(ProviderId.OpenCode, Id), title, @"C:\Work\app", null, null, null, 0, 0, null, null);

    [Fact]
    public void TheOwnersNameWinsOverTheDatabaseTitle()
    {
        Assert.Equal("The owner's name", SessionWindow.OpenCodeRowTitle("  The owner's name  ", Summary("Database title")));
    }

    [Fact]
    public void TheDatabaseTitleIsUsedWhenThereIsNoName()
    {
        Assert.Equal("Database title", SessionWindow.OpenCodeRowTitle(null, Summary("Database title")));
        Assert.Equal("Database title", SessionWindow.OpenCodeRowTitle("   ", Summary("Database title")));
    }

    [Fact]
    public void WithNoNameOrTitleTheRowNamesOpenCodeAndTheFirstEightCharactersOfTheId()
    {
        Assert.Equal("opencode session ses_0123", SessionWindow.OpenCodeRowTitle(null, Summary(null)));
        Assert.Equal("opencode session ses_0123", SessionWindow.OpenCodeRowTitle(null, Summary("  ")));
    }

    [Fact]
    public void ASessionOpenCodeArchivedGetsAMarkWithOnlyArchivedSet()
    {
        var mark = SessionWindow.EffectiveOpenCodeMark(null, archivedInOpenCode: true);

        Assert.NotNull(mark);
        Assert.True(mark!.Archived);
        Assert.False(mark.Pinned);
        Assert.Null(mark.Name);
    }

    [Fact]
    public void AnOwnersMarkKeepsItsFieldsWhenOpenCodeArchivesTheSession()
    {
        var owner = new LibraryEntry(Name: "Mine", Pinned: true, Tags: ["auth"], Note: "n", Colour: "red");

        var mark = SessionWindow.EffectiveOpenCodeMark(owner, archivedInOpenCode: true);

        Assert.NotNull(mark);
        Assert.True(mark!.Archived);
        Assert.True(mark.Pinned);
        Assert.Equal("Mine", mark.Name);
        Assert.Equal("n", mark.Note);
        Assert.Equal("red", mark.Colour);
        Assert.Equal(new[] { "auth" }, mark.Tags);
    }

    [Fact]
    public void WithoutOpenCodesArchiveFlagTheOwnersMarkIsUsedAsItIs()
    {
        Assert.Null(SessionWindow.EffectiveOpenCodeMark(null, archivedInOpenCode: false));

        var pinned = new LibraryEntry(Pinned: true);
        Assert.Same(pinned, SessionWindow.EffectiveOpenCodeMark(pinned, archivedInOpenCode: false));

        var archived = new LibraryEntry(Archived: true);
        Assert.Same(archived, SessionWindow.EffectiveOpenCodeMark(archived, archivedInOpenCode: false));
    }

    [Fact]
    public void TheReadersLinesBecomeTranscriptLinesWithTheirKindAndText()
    {
        var lines = SessionWindow.OpenCodeTranscriptLines(["you: hi", "opencode: a: b", "you: ", "no separator"]);

        Assert.Equal(4, lines.Count);
        Assert.Equal((1, "you", "hi"), (lines[0].Number, lines[0].Kind, lines[0].Text));
        Assert.Equal((2, "opencode", "a: b"), (lines[1].Number, lines[1].Kind, lines[1].Text));
        Assert.Equal((3, "you", string.Empty), (lines[2].Number, lines[2].Kind, lines[2].Text));
        Assert.Equal((4, string.Empty, "no separator"), (lines[3].Number, lines[3].Kind, lines[3].Text));
        Assert.All(lines, line =>
        {
            Assert.Null(line.Time);
            Assert.False(line.Collapsed);
            Assert.False(line.Failed);
        });
    }

    [Fact]
    public void ALineWithALineBreakKeepsItsBreaks()
    {
        var lines = SessionWindow.OpenCodeTranscriptLines(["opencode: first\nsecond"]);

        Assert.Equal("opencode", lines[0].Kind);
        Assert.Equal("first\nsecond", lines[0].Text);
    }
}

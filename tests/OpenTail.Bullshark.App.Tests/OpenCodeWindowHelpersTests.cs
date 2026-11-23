using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Ui;
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

    private static SessionWindow.OpenTab Tab(SessionWindow.TabKind kind, ProviderId? provider = null) => new(kind, "tab", "Tab", provider);

    [Fact]
    public void TheOpenCodeTranscriptTabKeepsTheTranscriptPaneAndTheOpenCodeResultsTabTheSearchPane()
    {
        Assert.Equal(PaneKind.Transcript, SessionWindow.PaneOf(Tab(SessionWindow.TabKind.OpenCodeTranscript, ProviderId.OpenCode)));
        Assert.Equal(PaneKind.Search, SessionWindow.PaneOf(Tab(SessionWindow.TabKind.OpenCodeSearchResults, ProviderId.OpenCode)));
    }

    [Fact]
    public void TheCopilotPanesAreUnchanged()
    {
        Assert.Equal(PaneKind.Transcript, SessionWindow.PaneOf(Tab(SessionWindow.TabKind.CopilotTranscript, ProviderId.Copilot)));
        Assert.Equal(PaneKind.Search, SessionWindow.PaneOf(Tab(SessionWindow.TabKind.CopilotSearchResults, ProviderId.Copilot)));
    }

    [Fact]
    public void AnOpenCodeSessionTabClosesWithOpenCodesOwnClose()
    {
        Assert.Equal(SessionWindow.TabCloseRoute.OpenCode, SessionWindow.CloseRouteOf(SessionWindow.TabKind.Session, ProviderId.OpenCode));
    }

    [Fact]
    public void TheOpenCodeTranscriptAndResultsTabsCloseAsPlainTabs()
    {
        Assert.Equal(SessionWindow.TabCloseRoute.Plain, SessionWindow.CloseRouteOf(SessionWindow.TabKind.OpenCodeTranscript, ProviderId.OpenCode));
        Assert.Equal(SessionWindow.TabCloseRoute.Plain, SessionWindow.CloseRouteOf(SessionWindow.TabKind.OpenCodeSearchResults, ProviderId.OpenCode));
    }

    [Fact]
    public void TheCloseRoutesOfTheOtherProvidersAreUnchanged()
    {
        Assert.Equal(SessionWindow.TabCloseRoute.Copilot, SessionWindow.CloseRouteOf(SessionWindow.TabKind.Session, ProviderId.Copilot));
        Assert.Equal(SessionWindow.TabCloseRoute.Codex, SessionWindow.CloseRouteOf(SessionWindow.TabKind.Session, ProviderId.Codex));
        Assert.Equal(SessionWindow.TabCloseRoute.ClaudeAsk, SessionWindow.CloseRouteOf(SessionWindow.TabKind.Session, ProviderId.Claude));
        Assert.Equal(SessionWindow.TabCloseRoute.Plain, SessionWindow.CloseRouteOf(SessionWindow.TabKind.CopilotTranscript, ProviderId.Copilot));
        Assert.Equal(SessionWindow.TabCloseRoute.Plain, SessionWindow.CloseRouteOf(SessionWindow.TabKind.CodexSearchResults, ProviderId.Codex));
        Assert.Equal(SessionWindow.TabCloseRoute.Plain, SessionWindow.CloseRouteOf(SessionWindow.TabKind.Transcript, ProviderId.Claude));
    }

    [Fact]
    public void ASavedOpenCodeStartsOnOpenCodeWhetherOrNotCodexIsOffered()
    {
        var saved = ProviderChoice.Parse("opencode");

        Assert.Equal(ProviderId.OpenCode, SessionWindow.StartingProviderOf(saved, codexAvailable: true));
        Assert.Equal(ProviderId.OpenCode, SessionWindow.StartingProviderOf(saved, codexAvailable: false));
    }

    [Fact]
    public void TheSavedChoicesOfTheOtherProvidersStartAsBefore()
    {
        Assert.Equal(ProviderId.Codex, SessionWindow.StartingProviderOf(ProviderId.Codex, codexAvailable: true));
        Assert.Equal(ProviderId.Claude, SessionWindow.StartingProviderOf(ProviderId.Codex, codexAvailable: false));
        Assert.Equal(ProviderId.Copilot, SessionWindow.StartingProviderOf(ProviderId.Copilot, codexAvailable: true));
        Assert.Equal(ProviderId.Copilot, SessionWindow.StartingProviderOf(ProviderId.Copilot, codexAvailable: false));
        Assert.Equal(ProviderId.Claude, SessionWindow.StartingProviderOf(ProviderId.Claude, codexAvailable: true));
        Assert.Equal(ProviderId.Claude, SessionWindow.StartingProviderOf(ProviderChoice.Parse(null), codexAvailable: true));
    }

    [Fact]
    public void AnOpenCodeSessionOpenCodeArchivedHasNoArchiveItem()
    {
        Assert.Null(SessionWindow.OpenCodeArchiveItemText(archived: true, archivedInOpenCode: true));
        Assert.Null(SessionWindow.OpenCodeArchiveItemText(archived: false, archivedInOpenCode: true));
    }

    [Fact]
    public void AnOpenCodeSessionTheAppMarksShowsArchiveOrUnarchiveAsBefore()
    {
        Assert.Equal("Archive", SessionWindow.OpenCodeArchiveItemText(archived: false, archivedInOpenCode: false));
        Assert.Equal("Unarchive", SessionWindow.OpenCodeArchiveItemText(archived: true, archivedInOpenCode: false));
    }

    [Fact]
    public void AContinuePickOfAnOpenCodeSessionOpensItsResumeScreen()
    {
        Assert.Null(SessionWindow.ContinueScreenRefusalOf(ProviderId.OpenCode, copilotListed: true, copilotNotListed: "x"));
        Assert.Null(SessionWindow.ContinueScreenRefusalOf(ProviderId.OpenCode, copilotListed: false, copilotNotListed: "x"));
    }

    [Fact]
    public void TheContinueRefusalsOfTheOtherProvidersAreUnchanged()
    {
        Assert.Null(SessionWindow.ContinueScreenRefusalOf(ProviderId.Claude, copilotListed: false, copilotNotListed: "x"));
        Assert.Null(SessionWindow.ContinueScreenRefusalOf(ProviderId.Codex, copilotListed: false, copilotNotListed: "x"));
        Assert.Null(SessionWindow.ContinueScreenRefusalOf(ProviderId.Copilot, copilotListed: true, copilotNotListed: "x"));
        Assert.Equal("x", SessionWindow.ContinueScreenRefusalOf(ProviderId.Copilot, copilotListed: false, copilotNotListed: "x"));
    }
}

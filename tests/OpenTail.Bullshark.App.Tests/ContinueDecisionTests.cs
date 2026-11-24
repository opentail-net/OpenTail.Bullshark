using System.Linq;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The Continue decision (stage C5): which session the action opens, and the mode its resume screen starts on (CN-D1, CN-D5), and the status text.
/// Pure: no window is made, no file is read, and the sessions are made up.
/// </summary>
public sealed class ContinueDecisionTests
{
    private static readonly DateTimeOffset Early = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = Early.AddHours(5);

    private static SessionSummary Session(ProviderId provider, string id, DateTimeOffset? last, bool? subagent = false) =>
        new(new SessionKey(provider, id), "Session " + id, @"C:\Example\" + id, null, last, subagent, null, null, null, null);

    private static ContinueSettings Settings(ContinueScope scope = ContinueScope.Provider, ContinueMode? defaultMode = null) =>
        new() { Scope = scope, DefaultMode = defaultMode };

    private static IReadOnlyDictionary<string, LibraryEntry> Marks(params (SessionKey Key, LibraryEntry Entry)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key.ToString(), pair => pair.Entry, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, LibraryEntry> NoMarks = Marks();

    [Fact]
    public void TheProviderScopeKeepsToTheActiveProvider()
    {
        var sessions = new[] { Session(ProviderId.Claude, "a", Early), Session(ProviderId.Codex, "b", Late) };

        var decision = ContinueDecision.Decide(Settings(ContinueScope.Provider), ProviderId.Claude, sessions, NoMarks);

        Assert.Equal(new SessionKey(ProviderId.Claude, "a"), decision.Session!.Key);
    }

    [Fact]
    public void TheAllScopeTakesTheNewestSessionOfAnyProvider()
    {
        var sessions = new[] { Session(ProviderId.Claude, "a", Early), Session(ProviderId.Codex, "b", Late) };

        var decision = ContinueDecision.Decide(Settings(ContinueScope.All), ProviderId.Claude, sessions, NoMarks);

        Assert.Equal(new SessionKey(ProviderId.Codex, "b"), decision.Session!.Key);
    }

    [Fact]
    public void AnArchivedSessionAndASubagentAreLeftOut()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "archived", Late),
            Session(ProviderId.Claude, "subagent", Late, subagent: true),
            Session(ProviderId.Claude, "kept", Early),
        };
        var marks = Marks((new SessionKey(ProviderId.Claude, "archived"), new LibraryEntry(Archived: true)));

        var decision = ContinueDecision.Decide(Settings(), ProviderId.Claude, sessions, marks);

        Assert.Equal(new SessionKey(ProviderId.Claude, "kept"), decision.Session!.Key);
    }

    [Fact]
    public void ANullLastActivityIsChosenOnlyWhenNoDatedSessionIsLeft()
    {
        var dated = new[] { Session(ProviderId.Claude, "undated", null), Session(ProviderId.Claude, "dated", Early) };
        var undatedOnly = new[] { Session(ProviderId.Claude, "undated", null), Session(ProviderId.Claude, "archived", Late) };
        var marks = Marks((new SessionKey(ProviderId.Claude, "archived"), new LibraryEntry(Archived: true)));

        Assert.Equal(new SessionKey(ProviderId.Claude, "dated"), ContinueDecision.Decide(Settings(), ProviderId.Claude, dated, NoMarks).Session!.Key);
        Assert.Equal(new SessionKey(ProviderId.Claude, "undated"), ContinueDecision.Decide(Settings(), ProviderId.Claude, undatedOnly, marks).Session!.Key);
    }

    [Fact]
    public void WhenNothingIsLeftThereIsNoSessionAndNoMode()
    {
        var onlyArchived = new[] { Session(ProviderId.Claude, "a", Late) };
        var marks = Marks((new SessionKey(ProviderId.Claude, "a"), new LibraryEntry(Archived: true, ResumeMode: ContinueMode.Full)));

        var empty = ContinueDecision.Decide(Settings(defaultMode: ContinueMode.Summary), ProviderId.Claude, [], NoMarks);
        var none = ContinueDecision.Decide(Settings(defaultMode: ContinueMode.Summary), ProviderId.Claude, onlyArchived, marks);

        Assert.Null(empty.Session);
        Assert.Null(empty.Mode);
        Assert.Null(none.Session);
        Assert.Null(none.Mode);
    }

    [Fact]
    public void TheSessionsOwnRememberedModeWinsOverTheDefaultSetting()
    {
        var sessions = new[] { Session(ProviderId.Claude, "a", Late) };
        var marks = Marks((new SessionKey(ProviderId.Claude, "a"), new LibraryEntry(ResumeMode: ContinueMode.Summary)));

        var decision = ContinueDecision.Decide(Settings(defaultMode: ContinueMode.LastTurns), ProviderId.Claude, sessions, marks);

        Assert.Equal(ContinueMode.Summary, decision.Mode);
    }

    [Fact]
    public void TheDefaultSettingIsUsedWhenThePickedSessionHasNoMark()
    {
        var sessions = new[] { Session(ProviderId.Claude, "a", Late), Session(ProviderId.Claude, "b", Early) };
        // The mark belongs to the other session, which is not picked: it must not decide the mode.
        var marks = Marks((new SessionKey(ProviderId.Claude, "b"), new LibraryEntry(ResumeMode: ContinueMode.LastTurns)));

        var decision = ContinueDecision.Decide(Settings(defaultMode: ContinueMode.Full), ProviderId.Claude, sessions, marks);

        Assert.Equal(new SessionKey(ProviderId.Claude, "a"), decision.Session!.Key);
        Assert.Equal(ContinueMode.Full, decision.Mode);
    }

    [Fact]
    public void WithNoMarkAndNoDefaultTheModeIsNullSoTheScreenDecides()
    {
        var sessions = new[] { Session(ProviderId.Claude, "a", Late) };

        var decision = ContinueDecision.Decide(Settings(), ProviderId.Claude, sessions, NoMarks);

        Assert.NotNull(decision.Session);
        Assert.Null(decision.Mode);
    }

    [Fact]
    public void AMarkIsFoundUnderItsOwnProviderOnly()
    {
        // The same id under two providers: Claude's mark must not reach Codex's session of that id (the marks are keyed provider:id).
        var sessions = new[] { Session(ProviderId.Claude, "x", Early), Session(ProviderId.Codex, "x", Late) };
        var marks = Marks((new SessionKey(ProviderId.Claude, "x"), new LibraryEntry(ResumeMode: ContinueMode.Full)));

        var decision = ContinueDecision.Decide(Settings(ContinueScope.All), ProviderId.Claude, sessions, marks);

        Assert.Equal(new SessionKey(ProviderId.Codex, "x"), decision.Session!.Key);
        Assert.Null(decision.Mode);
    }

    [Theory]
    [InlineData(ContinueMode.Full)]
    [InlineData(ContinueMode.LastTurns)]
    [InlineData(ContinueMode.Summary)]
    public void AnOpenCodeSessionOpensAtTheModeItsDecisionGives(ContinueMode mode)
    {
        var sessions = new[] { Session(ProviderId.OpenCode, "a", Late) };
        var marks = Marks((new SessionKey(ProviderId.OpenCode, "a"), new LibraryEntry(ResumeMode: mode)));

        Assert.Equal(mode, ContinueDecision.Decide(Settings(defaultMode: ContinueMode.LastTurns), ProviderId.OpenCode, sessions, marks).Mode);
    }

    [Fact]
    public void TheModeTextsAndTheProviderNamesAreTheStatusWords()
    {
        Assert.Equal("in full", ContinueDecision.ModeText(ContinueMode.Full));
        Assert.Equal("from the last turns", ContinueDecision.ModeText(ContinueMode.LastTurns));
        Assert.Equal("from a summary (made only when you press Make summary)", ContinueDecision.ModeText(ContinueMode.Summary));
        Assert.Equal("in the screen's own default mode", ContinueDecision.ModeText(null));
        Assert.Equal("Claude", ContinueDecision.NameOf(ProviderId.Claude));
        Assert.Equal("Codex", ContinueDecision.NameOf(ProviderId.Codex));
        Assert.Equal("Copilot", ContinueDecision.NameOf(ProviderId.Copilot));
        Assert.Equal("OpenCode", ContinueDecision.NameOf(ProviderId.OpenCode));
    }

    [Fact]
    public void ThePickedStatusNamesTheTitleTheProviderAndTheMode()
    {
        Assert.Equal(
            "Continue: An example title (Codex). Opening its resume screen from the last turns. Nothing starts until you confirm it there.",
            ContinueDecision.PickedText("An example title", ProviderId.Codex, ContinueMode.LastTurns));
    }

    [Fact]
    public void ClaudeAndCodexScreensOpenWhateverTheCopilotListSays()
    {
        Assert.Null(ContinueDecision.ScreenRefusal(ProviderId.Claude, copilotListed: false, CopilotRailText.NotListed));
        Assert.Null(ContinueDecision.ScreenRefusal(ProviderId.Codex, copilotListed: false, CopilotRailText.NotListed));
    }

    [Fact]
    public void AListedCopilotSessionOpensItsScreen()
    {
        Assert.Null(ContinueDecision.ScreenRefusal(ProviderId.Copilot, copilotListed: true, CopilotRailText.NotListed));
    }

    [Fact]
    public void AnUnlistedCopilotSessionIsRefusedWithTheListText()
    {
        Assert.Equal(CopilotRailText.NotListed, ContinueDecision.ScreenRefusal(ProviderId.Copilot, copilotListed: false, CopilotRailText.NotListed));
    }

    [Fact]
    public void TheAllScopeListsEveryProviderInTheDropdownOrderWhenAllAreAvailable()
    {
        var providers = ContinueDecision.ProvidersOf(ContinueScope.All, ProviderId.Claude, codexAvailable: true);

        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot, ProviderId.OpenCode }, providers);
    }

    [Fact]
    public void TheAllScopeLeavesOutCodexWhereThisBuildDoesNotOfferIt()
    {
        var providers = ContinueDecision.ProvidersOf(ContinueScope.All, ProviderId.Claude, codexAvailable: false);

        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Copilot, ProviderId.OpenCode }, providers);
    }

    [Fact]
    public void TheProviderScopeListsOnlyTheActiveProvider()
    {
        foreach (var active in new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot, ProviderId.OpenCode })
        {
            Assert.Equal(new[] { active }, ContinueDecision.ProvidersOf(ContinueScope.Provider, active, codexAvailable: true));
        }
    }
}

using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.Continue;

public sealed class ContinueChooserTests
{
    private static readonly DateTimeOffset Early = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = new(2026, 11, 20, 18, 30, 0, TimeSpan.Zero);
    private static readonly ProviderId Fake = new("fake");

    private static readonly IReadOnlyDictionary<string, LibraryEntry> NoMarks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);

    /// <summary>A synthetic session: made-up id, title and folder. Not a subagent unless the test says so.</summary>
    private static SessionSummary Session(ProviderId provider, string id, DateTimeOffset? lastActivity, bool? isSubagent = false) =>
        new(
            Key: new SessionKey(provider, id),
            Title: "Synthetic " + id,
            Folder: "Synthetic folder",
            FirstActivity: null,
            LastActivity: lastActivity,
            IsSubagent: isSubagent,
            UserMessages: null,
            AssistantMessages: null,
            Compactions: null,
            Model: null);

    private static IReadOnlyDictionary<string, LibraryEntry> Marks(params (string Key, LibraryEntry Entry)[] marks) =>
        marks.ToDictionary(mark => mark.Key, mark => mark.Entry, StringComparer.Ordinal);

    private static string? IdOf(SessionSummary? session) => session?.Key.SessionId;

    [Fact]
    public void TheLatestActiveSessionWins()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "older", Early),
            Session(ProviderId.Claude, "newest", Late),
            Session(ProviderId.Claude, "middle", Early.AddDays(5)),
        };

        Assert.Equal("newest", IdOf(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Claude)));
    }

    [Fact]
    public void ASubagentIsSkipped()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "subagent", Late, isSubagent: true),
            Session(ProviderId.Claude, "parent", Early),
        };

        Assert.Equal("parent", IdOf(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Claude)));
    }

    [Fact]
    public void AnUnknownSubagentFlagIsNotASubagent()
    {
        var sessions = new[]
        {
            Session(ProviderId.Copilot, "unknown", Late, isSubagent: null),
            Session(ProviderId.Copilot, "parent", Early),
        };

        Assert.Equal("unknown", IdOf(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Copilot)));
    }

    [Fact]
    public void AnArchivedSessionIsSkippedAndAMarkThatIsNotArchivedIsNot()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "archived", Late),
            Session(ProviderId.Claude, "pinned", Early.AddDays(5)),
            Session(ProviderId.Claude, "older", Early),
        };
        var marks = Marks(
            ("claude:archived", new LibraryEntry(Archived: true)),
            ("claude:pinned", new LibraryEntry(Pinned: true)));

        Assert.Equal("pinned", IdOf(ContinueChooser.Choose(sessions, marks, ProviderId.Claude)));
    }

    [Fact]
    public void ArchivedMarksAreMatchedByTheProviderQualifiedKey()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "shared", Late),
            Session(ProviderId.Codex, "shared", Early),
        };
        var marks = Marks(("claude:shared", new LibraryEntry(Archived: true)));

        var chosen = ContinueChooser.Choose(sessions, marks, onlyProvider: null);

        Assert.Equal<ProviderId?>(ProviderId.Codex, chosen?.Key.Provider);
    }

    [Fact]
    public void ABareMarkKeyDoesNotHideAQualifiedSession()
    {
        // The bare-id view of a library does not match the provider-qualified keys, so it hides nothing.
        var sessions = new[] { Session(ProviderId.Claude, "shared", Late) };
        var marks = Marks(("shared", new LibraryEntry(Archived: true)));

        Assert.Equal("shared", IdOf(ContinueChooser.Choose(sessions, marks, ProviderId.Claude)));
    }

    [Fact]
    public void ANullLastActivitySortsLastAndIsNeverChosenOverADatedSession()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "undated", null),
            Session(ProviderId.Claude, "dated", Early),
        };

        Assert.Equal("dated", IdOf(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Claude)));
    }

    [Fact]
    public void AnUndatedSessionIsChosenOnlyWhenNothingIsDated()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "beta", null),
            Session(ProviderId.Claude, "alpha", null),
        };

        Assert.Equal("alpha", IdOf(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Claude)));
    }

    [Fact]
    public void TiesOnTheActivityTimeAreBrokenByKeyTextWhateverTheOrder()
    {
        var forward = new[]
        {
            Session(ProviderId.Claude, "alpha", Late),
            Session(ProviderId.Claude, "beta", Late),
        };
        var backward = new[] { forward[1], forward[0] };

        Assert.Equal("alpha", IdOf(ContinueChooser.Choose(forward, NoMarks, ProviderId.Claude)));
        Assert.Equal("alpha", IdOf(ContinueChooser.Choose(backward, NoMarks, ProviderId.Claude)));
    }

    [Fact]
    public void TheProviderFilterConsidersOnlyThatProvider()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "claude-newest", Late),
            Session(ProviderId.Codex, "codex-older", Early),
        };

        Assert.Equal("codex-older", IdOf(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Codex)));
    }

    [Fact]
    public void AProviderWithNoSessionsGivesNull()
    {
        var sessions = new[] { Session(ProviderId.Claude, "claude-only", Late) };

        Assert.Null(ContinueChooser.Choose(sessions, NoMarks, ProviderId.Copilot));
    }

    [Fact]
    public void WithNoProviderTheCallerHasChosenAllAndTheNewestOfAnyProviderWins()
    {
        var sessions = new[]
        {
            Session(ProviderId.Claude, "claude-older", Early),
            Session(ProviderId.Codex, "codex-newest", Late),
            Session(Fake, "fake-middle", Early.AddDays(5)),
        };

        Assert.Equal("codex-newest", IdOf(ContinueChooser.Choose(sessions, NoMarks, onlyProvider: null)));
    }

    [Fact]
    public void NothingLeftGivesNull()
    {
        Assert.Null(ContinueChooser.Choose([], NoMarks, onlyProvider: null));

        var allSkipped = new[]
        {
            Session(Fake, "subagent", Late, isSubagent: true),
            Session(Fake, "archived", Early),
        };
        var marks = Marks(("fake:archived", new LibraryEntry(Archived: true)));

        Assert.Null(ContinueChooser.Choose(allSkipped, marks, onlyProvider: null));
    }
}

using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Codex's Group by tag (tags P5): a session with two tags is in both tag groups, a session with no tag is in the "No tag" group, an archived or
/// pinned session is in no tag group (the Archived and Pinned groups list it), the Codex tag filter applies before the grouping, and a subagent comes
/// last in its group. Synthetic summaries and marks only; the window is not built here (see <see cref="CodexRailGroups"/>).
/// </summary>
public sealed class CodexRailGroupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A Codex session summary with an id and a last activity. Invented; no rollout is read.</summary>
    private static SessionSummary Session(string id, int minutesAgo, bool subagent = false) =>
        new(new SessionKey(ProviderId.Codex, id), "synthetic title", "C:\\synthetic\\folder", null, Now.AddMinutes(-minutesAgo),
            subagent, 1, 1, 0, "synthetic-model");

    private static Func<string, LibraryEntry?> Marks(IReadOnlyDictionary<string, LibraryEntry> marks) =>
        id => marks.TryGetValue(id, out var entry) ? entry : null;

    private static List<string> Ids(TagGroup<SessionSummary> group) => group.Sessions.Select(s => s.Key.SessionId).ToList();

    [Fact]
    public void ASessionWithTwoTagsIsUnderBothTagGroups()
    {
        var sessions = new[] { Session("multi", 1), Session("single", 2) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["multi"] = new(Tags: ["auth", "login"]),
            ["single"] = new(Tags: ["auth"]),
        };

        var groups = CodexRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

        Assert.Equal(["auth", "login"], groups.Select(g => g.Tag).ToList());
        Assert.Equal(new[] { "multi", "single" }, Ids(groups.Single(g => g.Tag == "auth")));
        Assert.Equal(new[] { "multi" }, Ids(groups.Single(g => g.Tag == "login")));
    }

    [Fact]
    public void AnUntaggedSessionIsUnderTheNoTagGroupLast()
    {
        var sessions = new[] { Session("tagged", 4), Session("bare", 1), Session("blank", 2) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["tagged"] = new(Tags: ["cache"]),
            ["blank"] = new(Tags: ["  "]),
        };

        var groups = CodexRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

        Assert.Equal(["cache", null], groups.Select(g => g.Tag).ToList());
        // No mark (bare) and a blank tag (blank) both hold no tag; the newer one comes first.
        Assert.Equal(new[] { "bare", "blank" }, Ids(groups[1]));
    }

    [Fact]
    public void AnArchivedOrPinnedSessionIsInNoTagGroup()
    {
        var sessions = new[] { Session("archived", 1), Session("pinned", 2), Session("pinnedArchived", 3), Session("open", 4) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["archived"] = new(Tags: ["demo"], Archived: true),
            ["pinned"] = new(Tags: ["demo"], Pinned: true),
            ["pinnedArchived"] = new(Tags: ["demo"], Pinned: true, Archived: true),
            ["open"] = new(Tags: ["demo"]),
        };

        var groups = CodexRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

        Assert.Equal(new[] { "open" }, Ids(Assert.Single(groups)));
        Assert.False(CodexRailGroups.InHistoryGroups(new LibraryEntry(Archived: true), tagFilter: null));
        Assert.False(CodexRailGroups.InHistoryGroups(new LibraryEntry(Pinned: true), tagFilter: null));
        Assert.True(CodexRailGroups.InHistoryGroups(null, tagFilter: null));
    }

    [Fact]
    public void TheTagFilterAppliesBeforeTheGrouping()
    {
        var sessions = new[] { Session("demoOnly", 1), Session("otherOnly", 2), Session("both", 3) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["demoOnly"] = new(Tags: ["demo"]),
            ["otherOnly"] = new(Tags: ["other"]),
            ["both"] = new(Tags: ["demo", "other"]),
        };

        var groups = CodexRailGroups.ByTag(sessions, Marks(marks), tagFilter: "demo");

        // A session without the filter tag is in no group, so the other-only tag has no group. The both session keeps its other tag's group.
        Assert.Equal(["demo", "other"], groups.Select(g => g.Tag).ToList());
        Assert.Equal(new[] { "demoOnly", "both" }, Ids(groups[0]));
        Assert.Equal(new[] { "both" }, Ids(groups[1]));
    }

    [Fact]
    public void ASubagentComesLastInItsGroupAndTheNewestFirst()
    {
        var sessions = new[] { Session("old", 300), Session("sub", 1, subagent: true), Session("new", 2) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["old"] = new(Tags: ["t"]),
            ["sub"] = new(Tags: ["t"]),
            ["new"] = new(Tags: ["t"]),
        };

        var group = Assert.Single(CodexRailGroups.ByTag(sessions, Marks(marks), tagFilter: null));

        Assert.Equal(new[] { "new", "old", "sub" }, Ids(group));
    }

    [Fact]
    public void TheGroupsAreTheMostSessionsFirstThenByNameAndNoTagLast()
    {
        var sessions = new[] { Session("a", 1), Session("b", 2), Session("c", 3), Session("d", 4), Session("e", 5) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["a"] = new(Tags: ["zeta"]),
            ["b"] = new(Tags: ["beta"]),
            ["c"] = new(Tags: ["beta"]),
            ["d"] = new(Tags: ["alpha"]),
        };

        var groups = CodexRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

        Assert.Equal(["beta", "alpha", "zeta", null], groups.Select(g => g.Tag).ToList());
        Assert.Equal(new[] { "e" }, Ids(groups[3]));
    }
}

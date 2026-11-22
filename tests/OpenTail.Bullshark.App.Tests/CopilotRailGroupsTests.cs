using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Copilot's Group by tag (tags P5): a session with two tags is in both tag groups, a session with no tag is in the "No tag" group, an archived or
/// pinned session is in no tag group under All (the Archived and Pinned groups list it), the Copilot tag filter applies before the grouping, and a subagent
/// comes last in its group. The views (parity P11): each view lists its own sessions, the tag filter applies to all of them, and Archived groups the
/// archived sessions by tag. Synthetic summaries and marks only; the window is not built here (see <see cref="ProviderRailGroups"/>).
/// </summary>
public sealed class CopilotRailGroupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A Copilot session summary with an id and a last activity. Invented; no rollout is read.</summary>
    private static SessionSummary Session(string id, int minutesAgo, bool subagent = false) =>
        new(new SessionKey(ProviderId.Copilot, id), "synthetic title", "C:\\synthetic\\folder", null, Now.AddMinutes(-minutesAgo),
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

        var groups = ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

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

        var groups = ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

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

        var groups = ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

        Assert.Equal(new[] { "open" }, Ids(Assert.Single(groups)));
        Assert.False(ProviderRailGroups.InHistoryGroups(new LibraryEntry(Archived: true), tagFilter: null));
        Assert.False(ProviderRailGroups.InHistoryGroups(new LibraryEntry(Pinned: true), tagFilter: null));
        Assert.True(ProviderRailGroups.InHistoryGroups(null, tagFilter: null));
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

        var groups = ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: "demo");

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

        var group = Assert.Single(ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: null));

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

        var groups = ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: null);

        Assert.Equal(["beta", "alpha", "zeta", null], groups.Select(g => g.Tag).ToList());
        Assert.Equal(new[] { "e" }, Ids(groups[3]));
    }

    [Fact]
    public void EachViewListsTheSessionsItOwns()
    {
        var plain = new LibraryEntry();
        var pinned = new LibraryEntry(Pinned: true);
        var archived = new LibraryEntry(Archived: true);
        var pinnedArchived = new LibraryEntry(Pinned: true, Archived: true);

        // All: every session is listed; a pinned one in the Pinned group, an archived one in the Archived group (pinned or not).
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.All, plain, null));
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.All, null, null));
        Assert.Equal(ProviderPlace.PinnedGroup, ProviderRailGroups.PlaceOf(RailFilter.All, pinned, null));
        Assert.Equal(ProviderPlace.ArchivedGroup, ProviderRailGroups.PlaceOf(RailFilter.All, archived, null));
        Assert.Equal(ProviderPlace.ArchivedGroup, ProviderRailGroups.PlaceOf(RailFilter.All, pinnedArchived, null));

        // History: no archived session; a pinned one in the Pinned group; the rest in the folders.
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.History, plain, null));
        Assert.Equal(ProviderPlace.PinnedGroup, ProviderRailGroups.PlaceOf(RailFilter.History, pinned, null));
        Assert.Equal(ProviderPlace.NotListed, ProviderRailGroups.PlaceOf(RailFilter.History, archived, null));
        Assert.Equal(ProviderPlace.NotListed, ProviderRailGroups.PlaceOf(RailFilter.History, pinnedArchived, null));

        // Archived: only the archived sessions, in the folders, pinned or not (Claude's Archived filter lists a pinned archived session in its folder).
        Assert.Equal(ProviderPlace.NotListed, ProviderRailGroups.PlaceOf(RailFilter.Archived, plain, null));
        Assert.Equal(ProviderPlace.NotListed, ProviderRailGroups.PlaceOf(RailFilter.Archived, pinned, null));
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.Archived, archived, null));
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.Archived, pinnedArchived, null));
    }

    [Fact]
    public void TheTagFilterAppliesToEveryView()
    {
        var other = new LibraryEntry(Tags: ["other"]);
        var demo = new LibraryEntry(Tags: ["demo"]);
        var demoArchived = new LibraryEntry(Tags: ["demo"], Archived: true);
        var demoPinned = new LibraryEntry(Tags: ["demo"], Pinned: true);

        // A session without the filter tag is listed under no view, and a session with no mark holds no tag.
        foreach (var view in new[] { RailFilter.All, RailFilter.History, RailFilter.Archived })
        {
            Assert.Equal(ProviderPlace.NotListed, ProviderRailGroups.PlaceOf(view, other, "demo"));
            Assert.Equal(ProviderPlace.NotListed, ProviderRailGroups.PlaceOf(view, null, "demo"));
        }
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.All, demo, "demo"));
        Assert.Equal(ProviderPlace.ArchivedGroup, ProviderRailGroups.PlaceOf(RailFilter.All, demoArchived, "demo"));
        Assert.Equal(ProviderPlace.PinnedGroup, ProviderRailGroups.PlaceOf(RailFilter.History, demoPinned, "demo"));
        Assert.Equal(ProviderPlace.FolderGroups, ProviderRailGroups.PlaceOf(RailFilter.Archived, demoArchived, "demo"));
    }

    [Fact]
    public void AnUnarchivedSessionIsNotListedUnderArchived()
    {
        // The Archived view groups only the archived sessions by tag: the open one is left out, the archived pinned one is kept.
        var sessions = new[] { Session("open", 1), Session("archived", 2), Session("pinnedArchived", 3) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["open"] = new(Tags: ["t"]),
            ["archived"] = new(Tags: ["t"], Archived: true),
            ["pinnedArchived"] = new(Tags: ["t"], Pinned: true, Archived: true),
        };

        var group = Assert.Single(ProviderRailGroups.ByTag(sessions, Marks(marks), tagFilter: null, RailFilter.Archived));

        Assert.Equal(new[] { "archived", "pinnedArchived" }, Ids(group));
    }
}

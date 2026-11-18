using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// Group by tag (features batch G4): the order of the groups, a session with several tags in each of its groups, the untagged group last,
/// the tags compared without case, subagents last inside a group, and the empty case. Pure: the marks come from a dictionary.
/// </summary>
public sealed class TagGroupingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A past session with an id and a last activity. Invented; no transcript is read.</summary>
    private static SessionDigest Digest(string id, int minutesAgo, bool subagent = false) =>
        new(SessionId: id, FilePath: "unused", ProjectFolder: "proj", IsSubagent: subagent,
            FirstActivity: Now.AddMinutes(-minutesAgo - 5), LastActivity: Now.AddMinutes(-minutesAgo), Turns: 0, UserMessages: 2, AssistantMessages: 2,
            ToolCalls: 0, ToolNames: new Dictionary<string, int>(), FilesWritten: [], LastAssistantText: null, LastModel: null,
            ContextTokensAtLastTurn: 0, Usage: TokenUsage.Zero);

    private static Func<string, LibraryEntry?> Marks(IReadOnlyDictionary<string, LibraryEntry> marks) =>
        id => marks.TryGetValue(id, out var entry) ? entry : null;

    [Fact]
    public void NoSessionsGiveNoGroups()
    {
        Assert.Empty(TagGrouping.Groups([], _ => null));
    }

    [Fact]
    public void GroupsComeMostSessionsFirstThenByNameAndNoTagLast()
    {
        var sessions = new[] { Digest("a", 1), Digest("b", 2), Digest("c", 3), Digest("d", 4), Digest("e", 5) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["a"] = new(Tags: ["zeta"]),
            ["b"] = new(Tags: ["beta"]),
            ["c"] = new(Tags: ["beta"]),
            ["d"] = new(Tags: ["alpha"]),
        };

        var groups = TagGrouping.Groups(sessions, Marks(marks));

        // beta has two, then alpha and zeta with one each by name; the session with no tag is last.
        Assert.Equal(["beta", "alpha", "zeta", null], groups.Select(g => g.Tag).ToList());
        Assert.Equal(["e"], groups[3].Sessions.Select(s => s.SessionId).ToList());
    }

    [Fact]
    public void ASessionWithSeveralTagsIsInEachOfItsGroups()
    {
        var sessions = new[] { Digest("multi", 1), Digest("single", 2) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["multi"] = new(Tags: ["auth", "login"]),
            ["single"] = new(Tags: ["auth"]),
        };

        var groups = TagGrouping.Groups(sessions, Marks(marks));

        Assert.Equal(["multi", "single"], groups.Single(g => g.Tag == "auth").Sessions.Select(s => s.SessionId).ToList());
        Assert.Equal(["multi"], groups.Single(g => g.Tag == "login").Sessions.Select(s => s.SessionId).ToList());
    }

    [Fact]
    public void TagsAreComparedWithoutCaseAndSpaceAndACountsSessionOnce()
    {
        var sessions = new[] { Digest("one", 1) };
        var marks = new Dictionary<string, LibraryEntry> { ["one"] = new(Tags: [" Auth ", "auth", "AUTH"]) };

        var groups = TagGrouping.Groups(sessions, Marks(marks));

        var group = Assert.Single(groups);
        Assert.Equal("auth", group.Tag);
        Assert.Single(group.Sessions);
    }

    [Fact]
    public void UntaggedSessionsFormTheLastGroupOnlyWhenThereAreSome()
    {
        var sessions = new[] { Digest("none", 1), Digest("nullmark", 2), Digest("blank", 3), Digest("tagged", 4) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["blank"] = new(Tags: ["  ", ""]),
            ["tagged"] = new(Tags: ["cache"]),
        };

        var groups = TagGrouping.Groups(sessions, Marks(marks));

        Assert.Equal(["cache", null], groups.Select(g => g.Tag).ToList());
        Assert.Equal(["none", "nullmark", "blank"], groups[1].Sessions.Select(s => s.SessionId).ToList());

        var onlyTagged = TagGrouping.Groups([Digest("tagged", 4)], Marks(marks));
        Assert.Equal(["cache"], onlyTagged.Select(g => g.Tag).ToList());
    }

    [Fact]
    public void InsideAGroupTheNewestComesFirstAndSubagentsLast()
    {
        var sessions = new[] { Digest("old", 300), Digest("sub", 1, subagent: true), Digest("new", 2) };
        var marks = new Dictionary<string, LibraryEntry>
        {
            ["old"] = new(Tags: ["t"]),
            ["sub"] = new(Tags: ["t"]),
            ["new"] = new(Tags: ["t"]),
        };

        var group = Assert.Single(TagGrouping.Groups(sessions, Marks(marks)));

        Assert.Equal(["new", "old", "sub"], group.Sessions.Select(s => s.SessionId).ToList());
    }

    [Fact]
    public void TagsOfReadsTheMarkAndAMissingMarkHoldsNone()
    {
        Assert.Empty(TagGrouping.TagsOf(null));
        Assert.Empty(TagGrouping.TagsOf(new LibraryEntry()));
        Assert.Equal(["x", "y"], TagGrouping.TagsOf(new LibraryEntry(Tags: ["X", " y ", "x"])));
    }
}

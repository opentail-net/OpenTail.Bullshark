using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// Which past sessions the magic rename may name (magic rename, D1, D2, D7). Each rule has its own test: cold age, live, subagent, archived,
/// a name of the owner's, a custom title, already magic, too few messages, and the names of a folder.
/// </summary>
public sealed class MagicCandidatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private const string Id = "17a94483-99fc-4005-9fa9-dbd63958653d";
    private static readonly ISet<string> NoneLive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>A past session with the given last message, message counts and custom title. Invented; no transcript is read.</summary>
    private static SessionDigest Digest(
        DateTimeOffset? last,
        string id = Id,
        int users = 3,
        int assistants = 3,
        bool subagent = false,
        string folder = "proj",
        string? customTitle = null) =>
        new(SessionId: id, FilePath: "unused", ProjectFolder: folder, IsSubagent: subagent,
            FirstActivity: last?.AddMinutes(-30), LastActivity: last, Turns: 0, UserMessages: users, AssistantMessages: assistants,
            ToolCalls: 0, ToolNames: new Dictionary<string, int>(), FilesWritten: [], LastAssistantText: null, LastModel: null,
            ContextTokensAtLastTurn: 0, Usage: TokenUsage.Zero, FirstUserText: "Fix the login", CustomTitle: customTitle);

    private static SessionDigest Cold(string id = Id) => Digest(Now.AddHours(-3), id);

    private static readonly LibraryEntry Magic = new(Name: "Fix login", MagicAt: Now.AddHours(-3), MagicNamedAt: Now.AddHours(-2));

    [Fact]
    public void ASessionGoneColdIsACandidate()
    {
        Assert.True(MagicCandidates.IsCold(Cold(), null, NoneLive, Now));
    }

    [Fact]
    public void ASessionClaudeNeverAnsweredIsNotOfferedAMagicRename()
    {
        // The owner's own case (2026-11-18): a session that holds only a /clear and its caveat, two "user" entries and no answer.
        var onlyCommands = Digest(Now.AddHours(-3), users: 2, assistants: 0);

        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(onlyCommands, null, NoneLive));
        Assert.False(MagicCandidates.IsCold(onlyCommands, null, NoneLive, Now));
    }

    [Fact]
    public void ASessionLessThanTwoHoursOldIsNotCold()
    {
        Assert.False(MagicCandidates.IsCold(Digest(Now.AddHours(-1)), null, NoneLive, Now));
    }

    [Fact]
    public void ExactlyTwoHoursIsNotOlderThanTwoHours()
    {
        Assert.False(MagicCandidates.IsCold(Digest(Now.AddHours(-2)), null, NoneLive, Now));
        Assert.True(MagicCandidates.IsCold(Digest(Now.AddHours(-2).AddMinutes(-1)), null, NoneLive, Now));
    }

    [Fact]
    public void ASessionWithFewerThanFourMessagesIsNotCold()
    {
        Assert.False(MagicCandidates.IsCold(Digest(Now.AddHours(-3), users: 1, assistants: 2), null, NoneLive, Now));
        Assert.True(MagicCandidates.IsCold(Digest(Now.AddHours(-3), users: 2, assistants: 2), null, NoneLive, Now));
    }

    [Fact]
    public void ALiveSessionIsNeverACandidate()
    {
        var live = new HashSet<string>([Id], StringComparer.OrdinalIgnoreCase);

        Assert.False(MagicCandidates.IsCold(Cold(), null, live, Now));
        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(Cold(), null, live));
    }

    [Fact]
    public void ASubagentIsNeverACandidate()
    {
        var subagent = Digest(Now.AddHours(-3), subagent: true);

        Assert.False(MagicCandidates.IsCold(subagent, null, NoneLive, Now));
        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(subagent, null, NoneLive));
    }

    [Fact]
    public void AnArchivedSessionIsNeverACandidate()
    {
        var archived = new LibraryEntry(Archived: true);

        Assert.False(MagicCandidates.IsCold(Cold(), archived, NoneLive, Now));
        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(Cold(), archived, NoneLive));
    }

    [Fact]
    public void ASessionWithTheOwnersNameIsNotCold()
    {
        var named = new LibraryEntry(Name: "My name");

        Assert.False(MagicCandidates.IsCold(Cold(), named, NoneLive, Now));
    }

    [Fact]
    public void ASessionWithAClaudeCustomTitleIsNeverNamed()
    {
        var titled = Digest(Now.AddHours(-3), customTitle: "role");

        Assert.False(MagicCandidates.IsCold(titled, null, NoneLive, Now));
        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(titled, null, NoneLive));
        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(titled, new LibraryEntry(Name: "x"), NoneLive));
    }

    [Fact]
    public void ASessionAlreadyMagicNamedIsNotColdAgain()
    {
        Assert.False(MagicCandidates.IsCold(Cold(), Magic, NoneLive, Now));
    }

    [Fact]
    public void ASessionWithNoLastMessageIsNotACandidate()
    {
        var unknown = Digest(null);

        Assert.False(MagicCandidates.IsCold(unknown, null, NoneLive, Now));
        Assert.Equal(MagicCandidates.RenameKind.None, MagicCandidates.KindFor(unknown, null, NoneLive));
    }

    [Fact]
    public void ASingleRenameIgnoresTheTwoHourRule()
    {
        var recent = Digest(Now.AddMinutes(-5));

        Assert.False(MagicCandidates.IsCold(recent, null, NoneLive, Now));
        Assert.Equal(MagicCandidates.RenameKind.Fresh, MagicCandidates.KindFor(recent, null, NoneLive));
    }

    [Fact]
    public void AMagicNameCanAlwaysBeRolledAgain()
    {
        var unchanged = Digest(Now.AddHours(-3));
        var changed = Digest(Now.AddHours(-1));

        Assert.Equal(MagicCandidates.RenameKind.Again, MagicCandidates.KindFor(unchanged, Magic, NoneLive));
        Assert.Equal(MagicCandidates.RenameKind.Again, MagicCandidates.KindFor(changed, Magic, NoneLive));
    }

    [Fact]
    public void ColdInOrderKeepsTheOrderTheCallerGave()
    {
        var older = Digest(Now.AddHours(-9), "older");
        var newer = Digest(Now.AddHours(-3), "newer");
        var marks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);

        var listed = MagicCandidates.ColdInOrder([older, newer], marks, NoneLive, Now);

        Assert.Equal(["older", "newer"], listed.Select(d => d.SessionId));
    }

    [Fact]
    public void AnOwnersNameIsReplacedOnlyWithAConfirm()
    {
        var manual = new LibraryEntry(Name: "My name");

        Assert.Equal(MagicCandidates.RenameKind.ReplaceManual, MagicCandidates.KindFor(Cold(), manual, NoneLive));
    }

    [Fact]
    public void TheColdSessionsAreListedMostRecentFirst()
    {
        var older = Digest(Now.AddHours(-9), "older");
        var newer = Digest(Now.AddHours(-3), "newer");
        var tooRecent = Digest(Now.AddMinutes(-20), "recent");
        var marks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);

        var cold = MagicCandidates.Cold([older, newer, tooRecent], marks, NoneLive, Now);

        Assert.Equal(["newer", "older"], cold.Select(d => d.SessionId));
    }

    [Fact]
    public void NamesOfAFolderAreShownWithoutRepeats()
    {
        var history = new[]
        {
            Digest(Now.AddDays(-1), "a", folder: "proj"),
            Digest(Now.AddDays(-2), "b", folder: "proj"),
            Digest(Now.AddDays(-3), "c", folder: "other"),
            Digest(Now.AddDays(-4), "d", folder: "proj"),
        };
        var marks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal)
        {
            ["a"] = new(Name: "Fix login"),
            ["b"] = new(Name: "fix LOGIN"),
            ["c"] = new(Name: "Elsewhere"),
            ["d"] = new(Name: "Review queue"),
        };

        Assert.Equal(["Fix login", "Review queue"], MagicCandidates.NamesInFolder(history, marks, "proj"));
    }

    [Fact]
    public void TheNamesNeverRepeatAndTheNewFolderNamesAreCapped()
    {
        var history = Enumerable.Range(0, 40).Select(i => Digest(Now.AddDays(-i), i.ToString(), folder: "proj")).ToList();
        var marks = Enumerable.Range(0, 40).ToDictionary(i => i.ToString(), i => new LibraryEntry(Name: "Name " + i), StringComparer.Ordinal);

        Assert.Equal(MagicName.MaxUsedNames, MagicCandidates.NamesInFolder(history, marks, "proj").Count);
    }

    [Fact]
    public void TheTakenNamesLeaveOutTheSessionsBeingNamed()
    {
        var marks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal)
        {
            ["a"] = new(Name: "Fix Login"),
            ["b"] = new(Name: "Review queue"),
            ["c"] = new(Pinned: true),
        };

        var taken = MagicCandidates.TakenNames(marks, ["a"]);

        Assert.Contains("review QUEUE", taken);
        Assert.DoesNotContain("Fix login", taken);
        Assert.Single(taken);
    }

    [Fact]
    public void TheMagicNamesGivenInAnHourAreCountedByTheirTime()
    {
        var marks = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal)
        {
            ["a"] = new(Name: "Recent", MagicAt: Now.AddHours(-3), MagicNamedAt: Now.AddMinutes(-20)),
            ["b"] = new(Name: "Old", MagicAt: Now.AddHours(-9), MagicNamedAt: Now.AddHours(-5)),
            ["c"] = new(Name: "Owners"),
        };

        Assert.Equal(1, MagicCandidates.NamedSince(marks, Now.AddHours(-1)));
        Assert.Equal(2, MagicCandidates.NamedSince(marks, Now.AddHours(-6)));
    }
}

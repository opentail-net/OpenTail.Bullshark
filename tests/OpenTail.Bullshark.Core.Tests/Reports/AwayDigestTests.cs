using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Reports;

/// <summary>
/// "While you were away" (backlog D1): the period's rows, quiet sessions, events and limit hits, and the rules for when the page opens.
/// Pure: the transcripts, records and listings are built in memory.
/// </summary>
public sealed class AwayDigestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = Now.AddHours(-2);

    private static SessionRecord Record(string name, string sessionId, DateTimeOffset? startedAt = null, SessionRole role = SessionRole.Coder) =>
        new(role, sessionId, name, startedAt ?? Now.AddDays(-1), 0, [], "working", null);

    /// <summary>A main transcript whose last record is at <paramref name="last"/>.</summary>
    private static SessionDigest Digest(string sessionId, DateTimeOffset? last, string? text = null, IReadOnlyList<string>? files = null,
        int failed = 0, bool subagent = false) =>
        new(sessionId, Path.Combine("projects", "C--Git", sessionId + ".jsonl"), "C--Git", subagent,
            last?.AddMinutes(-5), last, 1, 1, 1, 0, new Dictionary<string, int>(), files ?? [], text, null, 0, TokenUsage.Zero,
            FailedToolCalls: failed);

    private static AgentInfo Listed(string sessionId, string state) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: "listed", Kind: "background", State: state, Status: null, Cwd: null, Pid: null,
            StartedAt: null);

    private static SupervisorEvent Event(DateTimeOffset at, string kind, string session = "alpha") =>
        new(at, session, kind, "detail");

    private static LimitHit Hit(DateTimeOffset at) => new(at, "five_hour", at.AddHours(5), TokenUsage.Zero);

    [Fact]
    public void ASessionActiveInThePeriodIsARowAndAQuietOneIsOnlyNamed()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var beta = Record("beta", "bbbbbbbb-0000-0000-0000-000000000002");
        var digests = new[]
        {
            Digest(alpha.SessionId, Since.AddMinutes(10)),
            Digest(beta.SessionId, Since.AddHours(-1)),
        };

        var digest = AwayDigest.Build([alpha, beta], digests, [], [], [], [], Since, Now);

        Assert.Equal(["alpha"], digest.Rows.Select(row => row.Name));
        Assert.Equal([new QuietSession("beta", NeedsYou: false)], digest.QuietSessions);
        Assert.True(digest.HasNews);
    }

    [Fact]
    public void RowsAreShownNewestActivityFirst()
    {
        var first = Record("first", "11111111-0000-0000-0000-000000000001");
        var second = Record("second", "22222222-0000-0000-0000-000000000002");
        var third = Record("third", "33333333-0000-0000-0000-000000000003");
        var digests = new[]
        {
            Digest(first.SessionId, Since.AddMinutes(5)),
            Digest(second.SessionId, Since.AddMinutes(50)),
            Digest(third.SessionId, Since.AddMinutes(20)),
        };

        var digest = AwayDigest.Build([first, second, third], digests, [], [], [], [], Since, Now);

        Assert.Equal(["second", "third", "first"], digest.Rows.Select(row => row.Name));
    }

    [Fact]
    public void ASessionWithNoRecordIsIgnored()
    {
        var orphan = Digest("cccccccc-0000-0000-0000-000000000009", Since.AddMinutes(10));

        var digest = AwayDigest.Build([], [orphan], [], [], [], [], Since, Now);

        Assert.Empty(digest.Rows);
        Assert.Empty(digest.QuietSessions);
        Assert.False(digest.HasNews);
    }

    [Fact]
    public void ASubagentTranscriptIsNotASession()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var digests = new[]
        {
            Digest(alpha.SessionId, Since.AddHours(-1)),
            Digest(alpha.SessionId, Since.AddMinutes(10), subagent: true),
        };

        var digest = AwayDigest.Build([alpha], digests, [], [], [], [], Since, Now);

        Assert.Empty(digest.Rows);
        Assert.Equal(["alpha"], digest.QuietSessions.Select(quiet => quiet.Name));
    }

    [Fact]
    public void TheLastTextIsCutTo300CharactersAndItsWhiteSpaceCollapsed()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var longText = string.Concat(Enumerable.Repeat("word ", 200));
        var digests = new[] { Digest(alpha.SessionId, Since.AddMinutes(10), text: longText) };

        var row = AwayDigest.Build([alpha], digests, [], [], [], [], Since, Now).Rows.Single();

        Assert.Equal(300, row.LastText!.Length);
        Assert.EndsWith("…", row.LastText);
        Assert.Equal("a b", AwayDigest.TrimmedText("a\n\n   b"));
        Assert.Equal("short reply", AwayDigest.TrimmedText("  short\r\nreply  "));
        Assert.Null(AwayDigest.TrimmedText("   "));
    }

    [Fact]
    public void EventsAndLimitHitsAreFilteredByThePeriodAndOrdered()
    {
        var events = new[]
        {
            Event(Since.AddMinutes(-1), "alert:Blocked"),          // before the period
            Event(Since.AddMinutes(40), "resume"),
            Event(Since.AddMinutes(20), "alert:RestartLoop"),       // out of order in the log
            Event(Since.AddMinutes(30), "state"),                    // a state change is not listed
            Event(Now.AddMinutes(5), "start"),                       // after the period
        };
        var hits = new[] { Hit(Since.AddMinutes(90)), Hit(Since.AddHours(-3)), Hit(Since.AddMinutes(10)) };

        var digest = AwayDigest.Build([], [], [], events, hits, [], Since, Now);

        Assert.Equal(["alert:RestartLoop", "resume"], digest.Events.Select(evt => evt.Event));
        Assert.Equal([Since.AddMinutes(10), Since.AddMinutes(90)], digest.LimitHits.Select(hit => hit.Time));
        Assert.True(digest.HasNews);
    }

    [Fact]
    public void NothingInThePeriodMeansNoNews()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var quietDigests = new[] { Digest(alpha.SessionId, Since.AddHours(-4)) };
        var outside = new[] { Event(Since.AddHours(-1), "resume") };

        var digest = AwayDigest.Build([alpha], quietDigests, [], outside, [Hit(Since.AddHours(-1))], [], Since, Now);

        Assert.False(digest.HasNews);
        Assert.Empty(digest.Rows);
        Assert.Empty(digest.Events);
        Assert.Empty(digest.LimitHits);
        Assert.Equal(["alpha"], digest.QuietSessions.Select(quiet => quiet.Name));
    }

    [Fact]
    public void ANeedsYouSessionIsMarkedInItsRowAndItsQuietLine()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var beta = Record("beta", "bbbbbbbb-0000-0000-0000-000000000002");
        var digests = new[]
        {
            Digest(alpha.SessionId, Since.AddMinutes(10)),
            Digest(beta.SessionId, Since.AddHours(-2)),
        };
        var attention = new[]
        {
            new AttentionItem("alpha", alpha.SessionId, AttentionReason.Blocked, "waiting for input", alpha.StartedAt),
            new AttentionItem("beta", beta.SessionId, AttentionReason.Stopped, "stopped", beta.StartedAt),
        };

        var digest = AwayDigest.Build([alpha, beta], digests, [], [], [], attention, Since, Now);

        Assert.True(digest.Rows.Single().NeedsYou);
        Assert.Equal([new QuietSession("beta", NeedsYou: true)], digest.QuietSessions);
    }

    [Fact]
    public void ASessionWithNoTranscriptShowsItsStateOnly()
    {
        var delta = Record("delta", "dddddddd-0000-0000-0000-000000000004", startedAt: Since.AddMinutes(15), role: SessionRole.Planner);
        var epsilon = Record("epsilon", "eeeeeeee-0000-0000-0000-000000000005", startedAt: Since.AddHours(-6));
        var agents = new[] { Listed(delta.SessionId, "working") };

        var digest = AwayDigest.Build([delta, epsilon], [], agents, [], [], [], Since, Now);

        var row = digest.Rows.Single();
        Assert.Equal("delta", row.Name);
        Assert.Equal(SessionRole.Planner, row.Role);
        Assert.Equal("working", row.State);
        Assert.Null(row.LastActivity);
        Assert.Equal(0, row.FilesWritten);
        Assert.Empty(row.FirstFiles);
        Assert.Null(row.LastText);
        Assert.Equal(["epsilon"], digest.QuietSessions.Select(quiet => quiet.Name));
    }

    [Fact]
    public void AListedStateIsShownAndAnUnlistedSessionIsNotListed()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var beta = Record("beta", "bbbbbbbb-0000-0000-0000-000000000002");
        var digests = new[]
        {
            Digest(alpha.SessionId, Since.AddMinutes(10)),
            Digest(beta.SessionId, Since.AddMinutes(20)),
        };

        var digest = AwayDigest.Build([alpha, beta], digests, [Listed(alpha.SessionId, "blocked")], [], [], [], Since, Now);

        Assert.Equal(["beta", "alpha"], digest.Rows.Select(row => row.Name));
        Assert.Equal(["not listed", "blocked"], digest.Rows.Select(row => row.State));
    }

    [Fact]
    public void FilesAreCountedOverTheWholeSessionAndFiveNamesAreShown()
    {
        var alpha = Record("alpha", "aaaaaaaa-0000-0000-0000-000000000001");
        var files = Enumerable.Range(1, 7).Select(n => Path.Combine("C:", "Git", "repo", "src", $"file{n}.cs")).ToList();
        var digests = new[] { Digest(alpha.SessionId, Since.AddMinutes(10), files: files, failed: 2) };

        var row = AwayDigest.Build([alpha], digests, [], [], [], [], Since, Now).Rows.Single();

        Assert.Equal(7, row.FilesWritten);
        Assert.Equal(["file1.cs", "file2.cs", "file3.cs", "file4.cs", "file5.cs"], row.FirstFiles);
        Assert.Equal(2, row.FailedToolCalls);
    }

    [Fact]
    public void ThePeriodStartsAtTheLastSeenTimeOrAtLeastThirtyMinutesBack()
    {
        var threeHoursAgo = Now.AddHours(-3);
        var fiveMinutesAgo = Now.AddMinutes(-5);

        Assert.Equal(Now.AddMinutes(-30), AwayDigest.PeriodStart(null, Now));
        Assert.Equal(Now.AddMinutes(-30), AwayDigest.PeriodStart(fiveMinutesAgo, Now));
        Assert.Equal(Now.AddMinutes(-30), AwayDigest.PeriodStart(Now.AddMinutes(10), Now));
        Assert.Equal(threeHoursAgo, AwayDigest.PeriodStart(threeHoursAgo, Now));
    }

    [Fact]
    public void TheWindowIsBackOnlyAfterTheReturnGap()
    {
        Assert.False(AwayDigest.IsReturn(null, Now));
        Assert.False(AwayDigest.IsReturn(Now.AddMinutes(-29), Now));
        Assert.True(AwayDigest.IsReturn(Now.AddMinutes(-30), Now));
        Assert.True(AwayDigest.IsReturn(Now.AddDays(-2), Now));
    }
}

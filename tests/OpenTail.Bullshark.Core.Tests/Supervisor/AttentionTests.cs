using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Which sessions need the owner (item 1 of the additions plan): blocked sessions, sessions the supervisor has stopped
/// bringing back, and starts that failed. Also the "gained" rule the window uses to announce a session, and the notify setting.
/// </summary>
public sealed class AttentionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private const string SessionA = "11111111-2222-3333-4444-555555555555";
    private const string SessionB = "66666666-7777-8888-9999-000000000000";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-attention-" + Guid.NewGuid().ToString("N")[..8]);

    public AttentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SessionRecord Record(string name, string sessionId, DateTimeOffset startedAt, string? lastAlert = null, int restarts = 0) =>
        new(SessionRole.Coder, sessionId, name, startedAt, restarts, [], null, lastAlert);

    private static AgentInfo Agent(string sessionId, string state) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: "x", Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    private static AttentionItem Item(string name, AttentionReason reason, DateTimeOffset startedAt) =>
        new(name, "s-" + name, reason, "detail", startedAt);

    [Fact]
    public void ABlockedListedSessionNeedsTheOwner()
    {
        var items = AttentionList.From([Record("coder", SessionA, Now.AddHours(-1))], [Agent(SessionA, "blocked")]);

        var item = Assert.Single(items);
        Assert.Equal("coder", item.Name);
        Assert.Equal(SessionA, item.SessionId);
        Assert.Equal(AttentionReason.Blocked, item.Reason);
    }

    [Fact]
    public void AWorkingOrFinishedSessionDoesNotNeedTheOwner()
    {
        foreach (var state in new[] { "working", "done", "busy" })
        {
            var items = AttentionList.From([Record("coder", SessionA, Now)], [Agent(SessionA, state)]);
            Assert.Empty(items);
        }
    }

    [Fact]
    public void ASessionIsMatchedByItsIdWithoutRegardToCase()
    {
        var items = AttentionList.From([Record("coder", SessionA, Now)], [Agent(SessionA.ToUpperInvariant(), "blocked")]);
        Assert.Equal(AttentionReason.Blocked, Assert.Single(items).Reason);
    }

    [Fact]
    public void AFailedStartWithNoSessionNeedsTheOwnerWithItsReason()
    {
        var failed = Record("coder", string.Empty, Now, lastAlert: "start failed: model not available");

        var item = Assert.Single(AttentionList.From([failed], []));

        Assert.Equal(AttentionReason.StartFailed, item.Reason);
        Assert.Null(item.SessionId);
        Assert.Equal("start failed: model not available", item.Detail);
    }

    [Fact]
    public void ALostSessionWithARestartLoopAlertNeedsTheOwnerWhileItIsGone()
    {
        var lost = Record("coder", SessionA, Now, lastAlert: "RestartLoop: 3 restarts in the last hour; not restarting until someone looks.");

        var item = Assert.Single(AttentionList.From([lost], []));

        Assert.Equal(AttentionReason.RestartLoop, item.Reason);
        Assert.Equal(SessionA, item.SessionId);
    }

    [Fact]
    public void AStaleRestartLoopAlertIsIgnoredOnceTheSessionIsListed()
    {
        var back = Record("coder", SessionA, Now, lastAlert: "RestartLoop: old");

        Assert.Empty(AttentionList.From([back], [Agent(SessionA, "working")]));
    }

    [Fact]
    public void AFailedResumeNeedsTheOwnerWhileTheSessionIsGone()
    {
        var lost = Record("coder", SessionA, Now, lastAlert: AttentionList.ResumeFailedPrefix + "exit 1");

        var item = Assert.Single(AttentionList.From([lost], []));

        Assert.Equal(AttentionReason.StartFailed, item.Reason);
    }

    [Fact]
    public void AStoppedSessionThatIsNotBroughtBackNeedsTheOwner()
    {
        var stopped = Record("coder", SessionA, Now, lastAlert: AttentionList.StoppedAlertPrefix + "the session was stopped.");

        var item = Assert.Single(AttentionList.From([stopped], []));

        Assert.Equal(AttentionReason.Stopped, item.Reason);
        Assert.Equal(SessionA, item.SessionId);
    }

    [Fact]
    public void AFailedSessionThatIsNotBroughtBackNeedsTheOwner()
    {
        var failed = Record("coder", SessionA, Now, lastAlert: AttentionList.FailedAlertPrefix + "the session failed.");

        var item = Assert.Single(AttentionList.From([failed], []));

        Assert.Equal(AttentionReason.Failed, item.Reason);
    }

    [Fact]
    public void ASessionListedAsStoppedOrFailedNeedsTheOwner()
    {
        Assert.Equal(AttentionReason.Stopped, Assert.Single(AttentionList.From([Record("coder", SessionA, Now)], [Agent(SessionA, "stopped")])).Reason);
        Assert.Equal(AttentionReason.Failed, Assert.Single(AttentionList.From([Record("coder", SessionA, Now)], [Agent(SessionA, "failed")])).Reason);
    }

    [Fact]
    public void AStaleStoppedAlertIsIgnoredOnceTheSessionIsWorkingAgain()
    {
        var back = Record("coder", SessionA, Now, lastAlert: AttentionList.StoppedAlertPrefix + "old");

        Assert.Empty(AttentionList.From([back], [Agent(SessionA, "working")]));
    }

    [Fact]
    public void AGoneSessionWithNoAlertIsNotWaitingForAnyone()
    {
        // Lost, but the supervisor is resuming it on the next pass: nothing for the owner to do yet.
        Assert.Empty(AttentionList.From([Record("coder", SessionA, Now)], []));
    }

    [Fact]
    public void TheOldestSessionComesFirstAndTiesAreSortedByName()
    {
        var records = new[]
        {
            Record("zeta", SessionA, Now.AddHours(-1)),
            Record("beta", SessionB, Now.AddHours(-3)),
            Record("alpha", "aaaaaaaa-0000-0000-0000-000000000000", Now.AddHours(-3)),
        };
        var agents = new[] { Agent(SessionA, "blocked"), Agent(SessionB, "blocked"), Agent("aaaaaaaa-0000-0000-0000-000000000000", "blocked") };

        var names = AttentionList.From(records, agents).Select(i => i.Name).ToList();

        Assert.Equal(["alpha", "beta", "zeta"], names);
    }

    [Fact]
    public void AMixedListHoldsEachSessionOnceWithItsOwnReason()
    {
        var records = new[]
        {
            Record("planner", SessionA, Now.AddHours(-2)),
            Record("coder", SessionB, Now.AddHours(-1), lastAlert: "RestartLoop: looping"),
            Record("helper", string.Empty, Now, lastAlert: "start failed: no"),
        };

        var items = AttentionList.From(records, [Agent(SessionA, "blocked")]);

        Assert.Equal(
            [AttentionReason.Blocked, AttentionReason.RestartLoop, AttentionReason.StartFailed],
            items.Select(i => i.Reason).ToList());
    }

    [Fact]
    public void GainedHoldsOnlyTheSessionsThatWerentWaitingBefore()
    {
        var before = new[] { Item("planner", AttentionReason.Blocked, Now) };
        var after = new[] { Item("planner", AttentionReason.Blocked, Now), Item("coder", AttentionReason.Blocked, Now) };

        var gained = AttentionList.Gained(before, after);

        Assert.Equal("coder", Assert.Single(gained).Name);
    }

    [Fact]
    public void AChangeOfReasonForASessionAlreadyWaitingIsNotANewArrival()
    {
        var before = new[] { Item("coder", AttentionReason.Blocked, Now) };
        var after = new[] { Item("coder", AttentionReason.RestartLoop, Now) };

        Assert.Empty(AttentionList.Gained(before, after));
    }

    [Theory]
    [InlineData(AttentionReason.Blocked)]
    [InlineData(AttentionReason.Stopped)]
    [InlineData(AttentionReason.Failed)]
    [InlineData(AttentionReason.StartFailed)]
    public void AWaitThatComesAfterAnOverlapNoticeIsAnnounced(AttentionReason reason)
    {
        // The session already had an overlap notice; its first wait is news, so it sounds once (round 5 item 6).
        var overlap = new AttentionItem("coder", "s-coder", AttentionReason.Overlap, "clash", Now, @"C:\work\a.cs");
        var wait = Item("coder", reason, Now);

        var gained = Assert.Single(AttentionList.Gained([overlap], [overlap, wait]));

        Assert.Equal(reason, gained.Reason);
    }

    [Fact]
    public void ASessionThatWaitsAgainAfterAnAnswerIsNewAgain()
    {
        var waiting = new[] { Item("coder", AttentionReason.Blocked, Now) };

        Assert.Empty(AttentionList.Gained(waiting, waiting));
        Assert.Single(AttentionList.Gained([], waiting));
    }

    [Fact]
    public void NotificationsAreOnUnlessTheSettingSaysFalse()
    {
        Assert.True(AttentionList.NotificationsOn(null));
        Assert.True(AttentionList.NotificationsOn(JsonValue.Create(true)));
        Assert.False(AttentionList.NotificationsOn(JsonValue.Create(false)));
        // Not a boolean is not an answer: the default applies.
        Assert.True(AttentionList.NotificationsOn(JsonValue.Create("no")));
    }

    [Fact]
    public async Task APassThatLosesAResumeLeavesTheSessionUnderNeedsYou()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = new FixedClock(Now);
        var spec = TestSpecs.Planner(@"C:\W", "brief");
        // Restarted twice more than an hour ago, so this pass resumes it (below the loop limit).
        store.Save(new Dictionary<string, SessionRecord>
        {
            [spec.Name] = new(SessionRole.Planner, SessionA, spec.Name, Now.AddHours(-5), 2, [Now.AddHours(-2), Now.AddHours(-2)], null, null),
        });
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Fail("cannot resume"),
            _ => FakeCliRunner.Ok(),
        });

        await new SupervisorRunner(new ClaudeCli(cli), store, clock, resolveTimeout: TimeSpan.FromSeconds(1))
            .CheckOnceAsync([spec]);

        var saved = store.Load()[spec.Name];
        Assert.StartsWith(AttentionList.ResumeFailedPrefix, saved.LastAlert);
        var item = Assert.Single(AttentionList.From(store.Load().Values, []));
        Assert.Equal(AttentionReason.StartFailed, item.Reason);
    }

    [Fact]
    public async Task ASuccessfulResumeClearsTheAlertOfTheLossItRecovered()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = new FixedClock(Now);
        var spec = TestSpecs.Planner(@"C:\W", "brief");
        store.Save(new Dictionary<string, SessionRecord>
        {
            [spec.Name] = new(SessionRole.Planner, SessionA, spec.Name, Now.AddHours(-5), 2, [Now.AddHours(-2), Now.AddHours(-2)], null,
                AttentionList.ResumeFailedPrefix + "earlier"),
        });
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Ok("backgrounded · 11111111 · planner\n"),
            _ => FakeCliRunner.Ok(),
        });

        var performed = await new SupervisorRunner(new ClaudeCli(cli), store, clock, resolveTimeout: TimeSpan.FromSeconds(1))
            .CheckOnceAsync([spec]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        var saved = store.Load()[spec.Name];
        Assert.Null(saved.LastAlert);
        Assert.Equal(3, saved.Restarts);
        Assert.Empty(AttentionList.From(store.Load().Values, []));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

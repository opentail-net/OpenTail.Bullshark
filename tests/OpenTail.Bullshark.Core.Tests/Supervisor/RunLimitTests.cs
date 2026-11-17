using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Limits per run (additions item 4): the setup fields, the spend of a run, the budget fractions, and the policy's rules at 80 % and
/// 100 %. A notice is raised once per level, and at the limit nothing new is started or resumed.
/// </summary>
public sealed class RunLimitTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Created.AddMinutes(30);
    private const string Coder = "bullshark-coder-abcd";
    private const string CoderId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OutsideId = "bbbbbbbb-0000-0000-0000-000000000002";

    private static SessionSpec Spec() => new(SessionRole.Coder, Coder, @"C:\work", "haiku", "medium", "brief", "auto");

    private static RunRecord Run(RunLimits? limits = null) =>
        new("abcd", "solo", @"C:\work", "auto", Created, [Spec()], null, limits);

    private static SessionRecord Record(string? lastAlert = null, string sessionId = CoderId, string? limitLevel = null) =>
        new(SessionRole.Coder, sessionId, Coder, Created, 0, [], null, lastAlert, limitLevel);

    private static AgentInfo Working() =>
        new(Id: CoderId[..8], SessionId: CoderId, Name: Coder, Kind: "background", State: "working", Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    private static SessionWrites Usage(string sessionId, long output) =>
        new(sessionId, [], new Dictionary<string, DateTimeOffset>(), new TokenUsage(0, output, 0, 0));

    [Fact]
    public void TheSetupFieldsAreOptionalAndRefuseWhatIsNotAPositiveNumber()
    {
        var blank = RunLimits.Parse("", " ");
        Assert.Null(blank.Limits);
        Assert.Null(blank.Error);

        var (limits, error) = RunLimits.Parse("5000", "30");
        Assert.Null(error);
        Assert.Equal(5000L, limits!.MaxOutputTokens);
        Assert.Equal(TimeSpan.FromMinutes(30), limits.MaxDuration);

        Assert.NotNull(RunLimits.Parse("0", "").Error);
        Assert.NotNull(RunLimits.Parse("lots", "").Error);
        Assert.NotNull(RunLimits.Parse("", "-5").Error);
    }

    [Fact]
    public void ASpendCountsOnlyTheRunsOwnSessionsOutputAndItsTime()
    {
        var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal) { [Coder] = Record() };
        var writes = new[] { Usage(CoderId, 700), Usage(OutsideId, 9_000) };

        var spend = RunSpend.Of(Run(), records, writes, Now);

        Assert.Equal(700L, spend.OutputTokens);
        Assert.Equal(TimeSpan.FromMinutes(30), spend.Elapsed);
    }

    [Fact]
    public void ABudgetIsTheLargerOfTheTokenAndTimeFractions()
    {
        var limits = new RunLimits(1000, TimeSpan.FromMinutes(60));

        var budget = RunBudget.For(limits, new RunSpend(800, TimeSpan.FromMinutes(30)));

        Assert.Equal(0.8, budget!.Fraction, 6);
        Assert.True(budget.Near);
        Assert.False(budget.Reached);
        Assert.True(RunBudget.For(limits, new RunSpend(1000, TimeSpan.Zero))!.Reached);
        Assert.Null(RunBudget.For(null, new RunSpend(1, TimeSpan.Zero)));
    }

    [Fact]
    public void ABudgetBelowEightyPercentGivesNoNotice()
    {
        var budget = new RunBudget(0.5);
        var actions = SupervisorPolicy.Decide(Spec(), Record(), [Working()], Now, SupervisorRules.Default, budget);

        Assert.Empty(actions);
    }

    [Fact]
    public void NearTheLimitTheOwnerIsToldOnceAndNotAgain()
    {
        var budget = new RunBudget(0.85);

        var first = SupervisorPolicy.Decide(Spec(), Record(), [Working()], Now, SupervisorRules.Default, budget);
        var alert = Assert.Single(first);
        Assert.Equal(AlertKind.LimitNear, Assert.IsType<SupervisorAction.Alert>(alert).Kind);

        // The record now says so, so the next pass stays quiet.
        var again = SupervisorPolicy.Decide(Spec(), Record("LimitNear: the run has used 85% of its limit.", limitLevel: RunBudget.NearLevel),
            [Working()], Now, SupervisorRules.Default, budget);
        Assert.Empty(again);
    }

    [Fact]
    public void AtTheLimitALostSessionIsNotResumedAndTheOwnerIsToldOnce()
    {
        var budget = new RunBudget(1.0);

        // The session is gone from the listing, so without a limit the policy would resume it.
        var actions = SupervisorPolicy.Decide(Spec(), Record(), [], Now, SupervisorRules.Default, budget);

        Assert.DoesNotContain(actions, a => a is SupervisorAction.Resume);
        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(actions));
        Assert.Equal(AlertKind.LimitReached, alert.Kind);

        var again = SupervisorPolicy.Decide(Spec(), Record("LimitReached: the run reached its limit.", limitLevel: RunBudget.ReachedLevel),
            [], Now, SupervisorRules.Default, budget);
        Assert.Empty(again);
    }

    [Fact]
    public void AtTheLimitARunThatHasNotStartedIsNotStarted()
    {
        var actions = SupervisorPolicy.Decide(Spec(), null, [], Now, SupervisorRules.Default, new RunBudget(1.2));

        Assert.Empty(actions);
    }

    private static RunRecord WaitingRun(DateTimeOffset? startAt, RunLimits? limits) =>
        new("abcd", "solo", @"C:\work", "auto", Created, [Spec()], startAt, limits);

    [Fact]
    public void AScheduledRunWithAShortDurationLimitStartsWhenItIsDue()
    {
        // Created at 09:00, due at 13:00, with a 60-minute limit. The four hours before it is due do not count against the limit.
        var due = Created.AddHours(4);
        var run = WaitingRun(due, new RunLimits(null, TimeSpan.FromMinutes(60)));
        var none = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        // The session started when the run came due, so its time counts from then (round 6 item 3).
        var started = SessionStartedAt(due);

        var atDue = RunBudget.For(run.Limits, RunSpend.Of(run, started, [], due));
        Assert.Equal(0, atDue!.Fraction, 6);
        Assert.IsType<SupervisorAction.Start>(Assert.Single(SupervisorPolicy.Decide(Spec(), null, [], due, SupervisorRules.Default, atDue)));

        // Half an hour into the run is half the limit, and the run still starts.
        var halfway = RunBudget.For(run.Limits, RunSpend.Of(run, started, [], due.AddMinutes(30)));
        Assert.Equal(0.5, halfway!.Fraction, 6);
        Assert.IsType<SupervisorAction.Start>(Assert.Single(SupervisorPolicy.Decide(Spec(), null, [], due.AddMinutes(30), SupervisorRules.Default, halfway)));

        // Past the limit from the start time, nothing new starts.
        var over = RunBudget.For(run.Limits, RunSpend.Of(run, started, [], due.AddMinutes(61)));
        Assert.True(over!.Reached);
        Assert.Empty(SupervisorPolicy.Decide(Spec(), null, [], due.AddMinutes(61), SupervisorRules.Default, over));
    }

    /// <summary>A records map in which the run's one coder started at <paramref name="startedAt"/>.</summary>
    private static Dictionary<string, SessionRecord> SessionStartedAt(DateTimeOffset startedAt) =>
        new(StringComparer.Ordinal) { [Coder] = new(SessionRole.Coder, CoderId, Coder, startedAt, 0, [], null, null) };

    [Fact]
    public void AWindowOpenedHoursAfterAScheduledStartDoesNotCountAsReached()
    {
        // Due at 13:00 with a 60-minute limit, and the window opens at 16:00. Nothing has started, so the run has run for nothing: the
        // first pass starts it, as it would have at 13:00.
        var run = WaitingRun(Created.AddHours(4), new RunLimits(null, TimeSpan.FromMinutes(60)));
        var opened = Created.AddHours(7);
        var none = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);

        var first = RunBudget.For(run.Limits, RunSpend.Of(run, none, [], opened));
        Assert.Equal(0, first!.Fraction, 6);
        Assert.IsType<SupervisorAction.Start>(Assert.Single(SupervisorPolicy.Decide(Spec(), null, [], opened, SupervisorRules.Default, first)));

        // Once it has started at 16:00, the limit counts from 16:00: half an hour later is half the limit, and an hour later is reached.
        var started = SessionStartedAt(opened);
        Assert.Equal(0.5, RunBudget.For(run.Limits, RunSpend.Of(run, started, [], opened.AddMinutes(30)))!.Fraction, 6);
        Assert.True(RunBudget.For(run.Limits, RunSpend.Of(run, started, [], opened.AddMinutes(61)))!.Reached);
    }

    [Fact]
    public void ARunsTimeCountsFromItsFirstSessionStartNotFromItsCreation()
    {
        // Created at 09:00, no start time. The first session started at 11:00 and the second at 11:30: the run has run for an hour at 12:00.
        var run = new RunRecord("abcd", "plan-code", @"C:\work", "auto", Created, [Spec(), Spec() with { Name = "bullshark-coder-zzzz" }]);
        var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal)
        {
            [Coder] = new(SessionRole.Coder, CoderId, Coder, Created.AddHours(2), 0, [], null, null),
            ["bullshark-coder-zzzz"] = new(SessionRole.Coder, OutsideId, "bullshark-coder-zzzz", Created.AddHours(2).AddMinutes(30), 0, [], null, null),
        };

        Assert.Equal(TimeSpan.FromHours(1), RunSpend.ElapsedOf(run, records, Created.AddHours(3)));
    }

    [Fact]
    public void ANoSessionStartedMeansTheRunHasRunForNothing()
    {
        // A run created three hours ago, whose sessions have not started (or whose starts failed, with no session id): no time has passed for it.
        var run = new RunRecord("abcd", "solo", @"C:\work", "auto", Created, [Spec()]);
        var failedStart = new Dictionary<string, SessionRecord>(StringComparer.Ordinal)
        {
            [Coder] = new(SessionRole.Coder, string.Empty, Coder, Created, 0, [], null, "start failed"),
        };

        Assert.Equal(TimeSpan.Zero, RunSpend.ElapsedOf(run, new Dictionary<string, SessionRecord>(StringComparer.Ordinal), Created.AddHours(3)));
        Assert.Equal(TimeSpan.Zero, RunSpend.ElapsedOf(run, failedStart, Created.AddHours(3)));
    }

    [Fact]
    public void ARunThatHasNotStartedNeverCountsAsReached()
    {
        var run = WaitingRun(Created.AddHours(4), new RunLimits(null, TimeSpan.FromMinutes(60)));

        // Three and a half hours after creation, and half an hour before the start: the run has run for nothing.
        var elapsed = RunSpend.ElapsedOf(run, Created.AddHours(3).AddMinutes(30));

        Assert.Equal(TimeSpan.Zero, elapsed);
    }

    [Fact]
    public void StartingARunNowCountsItsLimitFromNow()
    {
        // The run waits until 13:00. Started at 12:00, its 60-minute limit runs from 12:00, not from its creation at 09:00.
        var now = Created.AddHours(3);
        var started = RunStore.StartingNow([WaitingRun(Created.AddHours(4), null)], "abcd", now);

        var run = Assert.Single(started);
        Assert.Equal(now, run.StartAt);
        Assert.Equal(TimeSpan.FromMinutes(30), RunSpend.ElapsedOf(run, now.AddMinutes(30)));
    }

    [Fact]
    public void ANoticeIsNotRepeatedWhenAnotherAlertReplacedTheLastAlert()
    {
        // The owner was told the run is near its limit, and a blocked alert has since replaced the last alert. The level still says so.
        var actions = SupervisorPolicy.Decide(Spec(), Record("Blocked: waiting for input.", limitLevel: RunBudget.NearLevel),
            [Working()], Now, SupervisorRules.Default, new RunBudget(0.85));

        Assert.Empty(actions);
    }

    [Fact]
    public void ReachingTheLimitAfterANearNoticeIsNoticedOnce()
    {
        var actions = SupervisorPolicy.Decide(Spec(), Record("LimitNear: the run has used 85% of its limit.", limitLevel: RunBudget.NearLevel),
            [Working()], Now, SupervisorRules.Default, new RunBudget(1.0));

        Assert.Equal(AlertKind.LimitReached, Assert.IsType<SupervisorAction.Alert>(Assert.Single(actions)).Kind);
    }

    [Fact]
    public void WhenTheOwnerRaisesTheLimitTheLevelGoesDownWithoutANotice()
    {
        var lowered = SupervisorPolicy.Decide(Spec(), Record("LimitReached: the run reached its limit.", limitLevel: RunBudget.ReachedLevel),
            [Working()], Now, SupervisorRules.Default, new RunBudget(0.85));

        Assert.Equal(RunBudget.NearLevel, Assert.IsType<SupervisorAction.LimitLevelSet>(Assert.Single(lowered)).Level);

        var cleared = SupervisorPolicy.Decide(Spec(), Record(limitLevel: RunBudget.NearLevel), [Working()], Now, SupervisorRules.Default,
            new RunBudget(0.5));

        Assert.Null(Assert.IsType<SupervisorAction.LimitLevelSet>(Assert.Single(cleared)).Level);
    }

    [Fact]
    public void ASessionTheBudgetDoesNotNotifyIsHeldToTheLimitButNotTold()
    {
        // A lost session of a run at its limit is not resumed, and it gets no notice: the lead session is the one that is told.
        var held = new RunBudget(1.0, Notify: false);
        Assert.Empty(SupervisorPolicy.Decide(Spec(), Record(), [], Now, SupervisorRules.Default, held));

        Assert.Empty(SupervisorPolicy.Decide(Spec(), Record(), [Working()], Now, SupervisorRules.Default, new RunBudget(0.9, Notify: false)));
    }

    [Fact]
    public void TheFirstSessionWithAnIdLeadsTheRunAndEverySessionIsHeldToTheSameBudget()
    {
        var planner = new SessionSpec(SessionRole.Planner, "bullshark-planner-abcd", @"C:\work", "opus", "high", "brief", "auto");
        var run = new RunRecord("abcd", "plan-code", @"C:\work", "auto", Created, [planner, Spec()]);
        var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal)
        {
            // The planner's start failed, so it has no session id, and the coder leads.
            [planner.Name] = new(SessionRole.Planner, string.Empty, planner.Name, Created, 0, [], null, "start failed"),
            [Coder] = Record(),
        };

        var held = RunBudget.ForSessions(run, records, new RunBudget(0.9));

        Assert.False(held[planner.Name].Notify);
        Assert.True(held[Coder].Notify);
        Assert.Equal(0.9, held[planner.Name].Fraction);
        Assert.Equal(0.9, held[Coder].Fraction);
    }

    private static AgentInfo Listed(string state) =>
        new(Id: CoderId[..8], SessionId: CoderId, Name: Coder, Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    [Fact]
    public void ARunThatReachedItsLimitShowsItsLeadUnderNeedsYou()
    {
        var item = Assert.Single(AttentionList.From([Record(limitLevel: RunBudget.ReachedLevel)], [Listed("working")]));

        Assert.Equal(AttentionReason.LimitReached, item.Reason);
        Assert.Equal(Coder, item.Name);
    }

    [Fact]
    public void ANearLimitIsNotInNeedsYouAndABlockedSessionKeepsItsBlock()
    {
        Assert.Empty(AttentionList.From([Record(limitLevel: RunBudget.NearLevel)], [Listed("working")]));

        // The session's own reason comes first: one row per session.
        var item = Assert.Single(AttentionList.From([Record(limitLevel: RunBudget.ReachedLevel)], [Listed("blocked")]));
        Assert.Equal(AttentionReason.Blocked, item.Reason);
    }

    [Fact]
    public void ARunWithoutLimitsKeepsItsOldBehaviour()
    {
        // No budget at all: a lost session is resumed, as before this item.
        var actions = SupervisorPolicy.Decide(Spec(), Record(), [], Now, SupervisorRules.Default);

        Assert.Contains(actions, a => a is SupervisorAction.Resume);
    }

    [Fact]
    public void AStateRecordFromBeforeTheLimitLevelLoadsWithNoLevel()
    {
        // The shape of state.json before round 5 item 8: eight fields, no LimitLevel.
        var path = Path.Combine(Path.GetTempPath(), "bullshark-state-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            File.WriteAllText(path, """
                [{"Role":"Coder","SessionId":"aaaaaaaa-0000-0000-0000-000000000001","Name":"bullshark-coder-abcd",
                  "StartedAt":"2026-11-17T09:00:00+00:00","Restarts":0,"RestartTimes":[],
                  "LastState":"working","LastAlert":"LimitNear: the run has used 85% of its limit."}]
                """);

            var loaded = Assert.Single(new StateStore(path).Load().Values);

            Assert.Null(loaded.LimitLevel);
            Assert.Equal("LimitNear: the run has used 85% of its limit.", loaded.LastAlert);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ARunFromBeforeTheLimitsStillLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), "bullshark-limits-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            File.WriteAllText(path, """
                [{"RunId":"abcd","PatternKey":"solo","WorkingDirectory":"C:\\work","PermissionMode":"auto",
                  "CreatedAt":"2026-11-01T09:00:00+00:00",
                  "Specs":[{"Role":"Planner","Name":"bullshark-planner-abcd","WorkingDirectory":"C:\\work","Model":"opus",
                            "Effort":"high","Brief":"b","PermissionMode":"auto","ResumePrompt":"r"}]}]
                """);

            var loaded = Assert.Single(new RunStore(path).Load());

            Assert.Null(loaded.Limits);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

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

    private static SessionRecord Record(string? lastAlert = null, string sessionId = CoderId) =>
        new(SessionRole.Coder, sessionId, Coder, Created, 0, [], null, lastAlert);

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
        var again = SupervisorPolicy.Decide(Spec(), Record("LimitNear: the run has used 85% of its limit."), [Working()], Now, SupervisorRules.Default, budget);
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

        var again = SupervisorPolicy.Decide(Spec(), Record("LimitReached: the run reached its limit."), [], Now, SupervisorRules.Default, budget);
        Assert.Empty(again);
    }

    [Fact]
    public void AtTheLimitARunThatHasNotStartedIsNotStarted()
    {
        var actions = SupervisorPolicy.Decide(Spec(), null, [], Now, SupervisorRules.Default, new RunBudget(1.2));

        Assert.Empty(actions);
    }

    [Fact]
    public void ARunWithoutLimitsKeepsItsOldBehaviour()
    {
        // No budget at all: a lost session is resumed, as before this item.
        var actions = SupervisorPolicy.Decide(Spec(), Record(), [], Now, SupervisorRules.Default);

        Assert.Contains(actions, a => a is SupervisorAction.Resume);
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

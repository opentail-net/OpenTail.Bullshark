using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class SupervisorPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private static readonly SessionSpec Planner = TestSpecs.Planner(@"C:\Work", "brief");

    private static SessionRecord Record(string? lastState = null, int restarts = 0, params DateTimeOffset[] restartTimes) =>
        new(SessionRole.Planner, SessionId, "planner", Now.AddHours(-5), restarts, restartTimes, lastState, null);

    private static AgentInfo Agent(string state, string sessionId = SessionId) =>
        new(Id: "abcdef12", SessionId: sessionId, Name: "planner", Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    [Fact]
    public void FirstStartLaunchesTheRole()
    {
        var actions = SupervisorPolicy.Decide(Planner, null, [], Now, SupervisorRules.Default);

        var start = Assert.IsType<SupervisorAction.Start>(Assert.Single(actions));
        Assert.Equal(SessionRole.Planner, start.Role);
        Assert.Same(Planner, start.Spec);
    }

    [Fact]
    public void AMissingSessionIsResumedUnderTheSameId()
    {
        var actions = SupervisorPolicy.Decide(Planner, Record(), [], Now, SupervisorRules.Default);

        var resume = Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
        Assert.Equal(SessionId, resume.SessionId);
    }

    [Fact]
    public void ARunningSessionNeedsNoAction()
    {
        foreach (var state in new[] { "working", "done", "busy" })
        {
            var actions = SupervisorPolicy.Decide(Planner, Record(), [Agent(state)], Now, SupervisorRules.Default);
            Assert.Empty(actions);
        }
    }

    [Fact]
    public void AnUnknownStateIsLeftAlone()
    {
        var actions = SupervisorPolicy.Decide(Planner, Record(), [Agent("something-new")], Now, SupervisorRules.Default);
        Assert.Empty(actions);
    }

    [Fact]
    public void ABlockedSessionRaisesOneAlertWhenItEnters()
    {
        var first = SupervisorPolicy.Decide(Planner, Record(lastState: "working"), [Agent("blocked")], Now, SupervisorRules.Default);
        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(first));
        Assert.Equal(AlertKind.Blocked, alert.Kind);

        // Still blocked on the next pass: no repeat alert.
        var second = SupervisorPolicy.Decide(Planner, Record(lastState: "blocked"), [Agent("blocked")], Now, SupervisorRules.Default);
        Assert.Empty(second);
    }

    [Fact]
    public void RepeatedLossesStopRestartingAndAlert()
    {
        var recent = Enumerable.Range(1, 3).Select(i => Now.AddMinutes(-10 * i)).ToArray();
        var actions = SupervisorPolicy.Decide(Planner, Record(restarts: 3, restartTimes: recent), [], Now, SupervisorRules.Default);

        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(actions));
        Assert.Equal(AlertKind.RestartLoop, alert.Kind);
    }

    [Fact]
    public void OldRestartsDoNotCountTowardTheLimit()
    {
        var old = Enumerable.Range(1, 5).Select(i => Now.AddHours(-2 - i)).ToArray();
        var actions = SupervisorPolicy.Decide(Planner, Record(restarts: 5, restartTimes: old), [], Now, SupervisorRules.Default);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
    }

    private static SessionRecord RecordWithAlert(string? lastAlert, params DateTimeOffset[] restartTimes) =>
        new(SessionRole.Planner, SessionId, "test-planner", Now.AddHours(-5), restartTimes.Length, restartTimes, null, lastAlert);

    [Fact]
    public void ARestartLoopIsAlertedOnceWhileTheLimitHolds()
    {
        var recent = Enumerable.Range(1, 3).Select(i => Now.AddMinutes(-10 * i)).ToArray();

        var first = SupervisorPolicy.Decide(Planner, RecordWithAlert(null, recent), [], Now, SupervisorRules.Default);
        Assert.Equal(AlertKind.RestartLoop, Assert.IsType<SupervisorAction.Alert>(Assert.Single(first)).Kind);

        // The alert stands on the next pass: nothing is added, and the session is not resumed.
        var again = SupervisorPolicy.Decide(Planner,
            RecordWithAlert("RestartLoop: 3 restarts in the last hour; not restarting until someone looks.", recent), [], Now, SupervisorRules.Default);
        Assert.Empty(again);
    }

    [Fact]
    public void ARestartLoopAlertDoesNotBlockTheResumeOnceTheRestartsAgeOut()
    {
        // All three restarts are older than an hour, so the session is under the limit again and is resumed.
        var old = Enumerable.Range(1, 3).Select(i => Now.AddHours(-1 - i)).ToArray();

        var actions = SupervisorPolicy.Decide(Planner, RecordWithAlert("RestartLoop: old", old), [], Now, SupervisorRules.Default);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
    }

    [Fact]
    public void AgentsAreMatchedBySessionIdNotByName()
    {
        // A session with the same name but another id is not our session: it counts as missing.
        var actions = SupervisorPolicy.Decide(Planner, Record(), [Agent("working", sessionId: "other")], Now, SupervisorRules.Default);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
    }

    private static AgentInfo NamedAgent(string name, string state, string sessionId) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: name, Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    [Fact]
    public void AStoppedSessionIsAlertedOnceAndNeverResumed()
    {
        var first = SupervisorPolicy.Decide(Planner, Record(lastState: "working"), [Agent("stopped")], Now, SupervisorRules.Default);
        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(first));
        Assert.Equal(AlertKind.Stopped, alert.Kind);

        // Still stopped on the next pass: no repeat alert, and no resume.
        var second = SupervisorPolicy.Decide(Planner, Record(lastState: "stopped"), [Agent("stopped")], Now, SupervisorRules.Default);
        Assert.Empty(second);
    }

    [Fact]
    public void AFailedSessionIsAlertedOnceAndNeverResumed()
    {
        var first = SupervisorPolicy.Decide(Planner, Record(lastState: "working"), [Agent("failed")], Now, SupervisorRules.Default);
        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(first));
        Assert.Equal(AlertKind.Failed, alert.Kind);

        var second = SupervisorPolicy.Decide(Planner, Record(lastState: "failed"), [Agent("failed")], Now, SupervisorRules.Default);
        Assert.Empty(second);
    }

    [Fact]
    public void ADoneSessionIsNeitherResumedNorAlerted()
    {
        // A completed session is listed with the completed ones. It is not "gone", so nothing resumes it.
        var actions = SupervisorPolicy.Decide(Planner, Record(lastState: "working"), [Agent("done")], Now, SupervisorRules.Default);
        Assert.Empty(actions);
    }

    [Fact]
    public void AFinishedSessionOfTheSameNameIsNotAdopted()
    {
        // An earlier run's session of this name has ended. It is not running, so the role is started again.
        foreach (var state in new[] { "done", "stopped", "failed" })
        {
            var actions = SupervisorPolicy.Decide(Planner, null,
                [NamedAgent(TestSpecs.PlannerName, state, "99999999-aaaa-bbbb-cccc-dddddddddddd")], Now, SupervisorRules.Default);
            Assert.IsType<SupervisorAction.Start>(Assert.Single(actions));
        }
    }

    [Fact]
    public void ARunningSessionOfTheSameNameIsAdoptedRatherThanAFinishedOne()
    {
        var finished = NamedAgent(TestSpecs.PlannerName, "done", "99999999-aaaa-bbbb-cccc-dddddddddddd");
        var running = NamedAgent(TestSpecs.PlannerName, "working", SessionId);

        var actions = SupervisorPolicy.Decide(Planner, null, [finished, running], Now, SupervisorRules.Default);

        var adopt = Assert.IsType<SupervisorAction.Adopt>(Assert.Single(actions));
        Assert.Equal(SessionId, adopt.SessionId);
    }

    [Fact]
    public void DefaultRolesMatchTheReadmeDecisions()
    {
        var planner = TestSpecs.Planner(@"C:\W", "build X");
        var coder = TestSpecs.Coder(@"C:\W");

        Assert.Equal("opus", planner.Model);
        Assert.Equal("haiku", coder.Model);
        Assert.Equal(TestSpecs.PlannerName, planner.Name);
        Assert.Equal(TestSpecs.CoderName, coder.Name);
    }

    [Fact]
    public void BothRolesRunInTheSamePermissionModeSoMessagesAreNotHeld()
    {
        // Finding F22: a message between sessions in different permission modes waits for approval.
        Assert.Equal(
            TestSpecs.Planner(@"C:\W", "t").PermissionMode,
            TestSpecs.Coder(@"C:\W").PermissionMode);
    }

    [Fact]
    public void EachBriefNamesThePeerAndTheHandoffAndKeepsQuestionsToThePeer()
    {
        var planner = TestSpecs.Planner(@"C:\W", "build X").Brief;
        var coder = TestSpecs.Coder(@"C:\W").Brief;

        Assert.Contains(TestSpecs.CoderName, planner);
        Assert.Contains("SendMessage", planner);
        Assert.Contains(TestSpecs.PlannerName, coder);
        Assert.Contains("SendMessage", coder);
        // The planner asks nothing, and the coder may ask its peer one short question but never waits for the answer (owner, 2026-11-15).
        Assert.Contains("Do not ask questions", planner);
        Assert.Contains("one short question with SendMessage", coder);
        Assert.Contains("Do not stop to wait for the answer", coder);
        Assert.Contains("build X", planner);
    }
}

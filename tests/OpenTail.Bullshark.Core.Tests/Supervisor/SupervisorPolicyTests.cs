using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class SupervisorPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private static readonly SessionSpec Planner = SessionSpec.DefaultPlanner(@"C:\Work", "brief");

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

    [Fact]
    public void AgentsAreMatchedBySessionIdNotByName()
    {
        // A session with the same name but another id is not our session: it counts as missing.
        var actions = SupervisorPolicy.Decide(Planner, Record(), [Agent("working", sessionId: "other")], Now, SupervisorRules.Default);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
    }

    [Fact]
    public void DefaultRolesMatchTheReadmeDecisions()
    {
        var planner = SessionSpec.DefaultPlanner(@"C:\W", "build X");
        var coder = SessionSpec.DefaultCoder(@"C:\W");

        Assert.Equal("opus", planner.Model);
        Assert.Equal("haiku", coder.Model);
        Assert.Equal("ovc-planner", planner.Name);
        Assert.Equal("ovc-coder", coder.Name);
    }

    [Fact]
    public void BothRolesRunInTheSamePermissionModeSoMessagesAreNotHeld()
    {
        // Finding F22: a message between sessions in different permission modes waits for approval.
        Assert.Equal(
            SessionSpec.DefaultPlanner(@"C:\W", "t").PermissionMode,
            SessionSpec.DefaultCoder(@"C:\W").PermissionMode);
    }

    [Fact]
    public void EachBriefNamesThePeerAndTheHandoffAndKeepsQuestionsToThePeer()
    {
        var planner = SessionSpec.DefaultPlanner(@"C:\W", "build X").Brief;
        var coder = SessionSpec.DefaultCoder(@"C:\W").Brief;

        Assert.Contains("ovc-coder", planner);
        Assert.Contains("SendMessage", planner);
        Assert.Contains("ovc-planner", coder);
        Assert.Contains("SendMessage", coder);
        // The planner asks nothing, and the coder may ask its peer one short question but never waits for the answer (owner, 2026-11-15).
        Assert.Contains("Do not ask questions", planner);
        Assert.Contains("one short question with SendMessage", coder);
        Assert.Contains("Do not stop to wait for the answer", coder);
        Assert.Contains("build X", planner);
    }
}

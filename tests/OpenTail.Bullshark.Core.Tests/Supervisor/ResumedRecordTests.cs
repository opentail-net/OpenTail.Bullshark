using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class ResumedRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private const string Name = "bullshark-resume-abc123";

    [Fact]
    public void AResumedSessionWithItsIdIsRecordedWithIt()
    {
        var record = RunPlan.ResumedRecord(Name, SessionId, Now);

        Assert.Equal(SessionId, record.SessionId);
        Assert.Equal(Name, record.Name);
        Assert.Null(record.LastAlert);
    }

    [Fact]
    public void AResumedSessionWithoutItsIdKeepsItsNameAndSaysSo()
    {
        var record = RunPlan.ResumedRecord(Name, null, Now);

        Assert.Equal(string.Empty, record.SessionId);
        Assert.Equal(Name, record.Name);
        Assert.Equal(RunPlan.PendingIdAlert, record.LastAlert);
    }

    [Fact]
    public void TheNextPassAdoptsTheSessionByItsNameInsteadOfStartingItAgain()
    {
        var run = RunPlan.ResumedRun(Name, @"C:\Work", "opus", "high", Now);
        var record = RunPlan.ResumedRecord(Name, null, Now);
        var listed = new AgentInfo(Id: SessionId[..8], SessionId: SessionId, Name: Name, Kind: "background", State: "working",
            Status: null, Cwd: null, Pid: null, StartedAt: null);

        var actions = SupervisorPolicy.Decide(run.Specs.Single(), record, [listed], Now, SupervisorRules.Default);

        var adopt = Assert.IsType<SupervisorAction.Adopt>(Assert.Single(actions));
        Assert.Equal(SessionId, adopt.SessionId);
    }
}

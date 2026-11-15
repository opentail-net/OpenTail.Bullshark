using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>A session resumed in full is kept alive: with a spec and its record, a missing session is resumed under its own id.</summary>
public sealed class ResumedSessionTests
{
    [Fact]
    public void AResumedSessionIsResumedUnderItsIdWhenItGoes()
    {
        var spec = new SessionSpec(SessionRole.Planner, "bullshark-resume-ab12", @"C:\W", "opus", "high", "Continue.", "auto");
        var record = new SessionRecord(SessionRole.Planner, "11111111-aaaa-4bbb-8ccc-000000000001", spec.Name,
            DateTimeOffset.UnixEpoch, 0, [], null, null);

        var actions = SupervisorPolicy.Decide(spec, record, [], DateTimeOffset.UtcNow, SupervisorRules.Default);

        var resume = Assert.IsType<SupervisorAction.Resume>(Assert.Single(actions));
        Assert.Equal(record.SessionId, resume.SessionId);
    }
}

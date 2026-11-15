using OpenTail.Bullshark.Core.Nudge;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Nudge;

public sealed class NudgePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Idle = NudgePolicy.DefaultOwnerIdle;

    [Fact]
    public void AnIdleSessionWithNoRecentTypingMayBeNudged()
    {
        var decision = NudgePolicy.Evaluate(Now, Now.AddHours(-1), "done", Idle);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void TheOwnerTypingRecentlyBlocksTheNudge()
    {
        var decision = NudgePolicy.Evaluate(Now, Now.AddMinutes(-3), "done", Idle);
        Assert.False(decision.Allowed);
        Assert.Contains("typed in this tab", decision.Reason);
    }

    [Fact]
    public void TheIdleRuleIsExactlyTenMinutesByDefault()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), NudgePolicy.DefaultOwnerIdle);
        Assert.True(NudgePolicy.Evaluate(Now, Now.AddMinutes(-10), "done", Idle).Allowed);
        Assert.False(NudgePolicy.Evaluate(Now, Now.AddMinutes(-9), "done", Idle).Allowed);
    }

    [Fact]
    public void ANeverTypedTabCanBeNudgedWhenIdle()
    {
        Assert.True(NudgePolicy.Evaluate(Now, null, "done", Idle).Allowed);
    }

    [Fact]
    public void ABlockedSessionIsNeverNudged()
    {
        // Typing into a permission prompt would answer it on the owner's behalf.
        var decision = NudgePolicy.Evaluate(Now, null, "blocked", Idle);
        Assert.False(decision.Allowed);
        Assert.Contains("permission", decision.Reason);
    }

    [Fact]
    public void AWorkingSessionIsNotNudgedMidTurn()
    {
        Assert.False(NudgePolicy.Evaluate(Now, null, "working", Idle).Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mystery")]
    public void AnUnknownStateIsNotNudged(string? state)
    {
        Assert.False(NudgePolicy.Evaluate(Now, null, state, Idle).Allowed);
    }
}

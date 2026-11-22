using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// The clock rule for ages from stored times (R5, <c>state.clampClockBackwards</c>): a time ahead of now is an age of zero while the setting is on
/// (the default), and the raw negative age while it is off.
/// </summary>
public sealed class StoredAgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AStoredTimeInThePastGivesItsAge()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), StoredAge.Of(Now, Now.AddMinutes(-5)));
        Assert.Equal(TimeSpan.FromMinutes(5), StoredAge.Of(Now, Now.AddMinutes(-5), clampBackwards: false));
    }

    [Fact]
    public void AStoredTimeAheadOfNowIsZeroWhenClamped()
    {
        Assert.Equal(TimeSpan.Zero, StoredAge.Of(Now, Now.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, StoredAge.Of(Now, Now.AddMinutes(5), clampBackwards: true));
    }

    [Fact]
    public void AStoredTimeAheadOfNowIsTheRawNegativeAgeWhenNotClamped()
    {
        Assert.Equal(TimeSpan.FromMinutes(-5), StoredAge.Of(Now, Now.AddMinutes(5), clampBackwards: false));
        Assert.Equal(TimeSpan.FromMinutes(-5), StoredAge.Clamp(TimeSpan.FromMinutes(-5), clampBackwards: false));
    }

    [Fact]
    public void TheDefaultSettingClamps()
    {
        Assert.True(BehaviourSettings.Defaults.State.ClampClockBackwards);
        Assert.Equal(0, BehaviourSettings.Defaults.State.WorkingDecayMinutes);
    }

    [Fact]
    public void TheRunsElapsedTimeFollowsTheSetting()
    {
        // The run's first session start is ahead of now: the clock went back.
        var spec = new SessionSpec(SessionRole.Coder, "bullshark-coder-zzzz", @"C:\work", "haiku", "medium", "brief", "auto");
        var run = new RunRecord("abcd", "solo", @"C:\work", "auto", Now.AddHours(-1), [spec], null, null);
        var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal)
        {
            [spec.Name] = new(SessionRole.Coder, "s1", spec.Name, Now.AddMinutes(5), 0, [], null, null),
        };

        Assert.Equal(TimeSpan.Zero, RunSpend.ElapsedOf(run, records, Now));
        Assert.Equal(TimeSpan.FromMinutes(-5), RunSpend.ElapsedOf(run, records, Now, clampBackwards: false));
    }
}

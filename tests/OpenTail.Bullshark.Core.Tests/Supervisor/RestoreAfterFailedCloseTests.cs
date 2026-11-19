using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Reliability batch R6: a failed close puts back only the session that failed. The runs are read as they are now; a session closed by
/// another close meanwhile stays closed (the old code put back the whole run as it was before the close).
/// </summary>
public sealed class RestoreAfterFailedCloseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    private static SessionSpec Spec(string name) => new(
        SessionRole.Coder, name, @"C:\repo", "opus", "high", "brief", "auto");

    private static RunRecord Run(string id, params string[] names) =>
        new(id, "pattern", @"C:\repo", "auto", T0, names.Select(Spec).ToList(), FirstStartedAt: T0.AddMinutes(-30), ClosedOutputTokens: 1000);

    [Fact]
    public void TheFailedSessionComesBackInItsPlace()
    {
        var before = new[] { Run("r1", "a", "b", "c") };
        var closedAt = T0.AddMinutes(-10);
        var current = RunStore.WithoutSession(before, "b", closedAt, 300);

        var restored = RunStore.WithSessionRestored(current, before, "b", closedAt, 300);

        Assert.Equal(["a", "b", "c"], restored.Single().Specs.Select(s => s.Name));
    }

    [Fact]
    public void ASessionClosedMeanwhileStaysClosed()
    {
        // The failed close is of "a"; "b" closed in between. The old code put "b" back with the whole run.
        var before = new[] { Run("r1", "a", "b") };
        var closedAt = T0.AddMinutes(-10);
        var afterA = RunStore.WithoutSession(before, "a", closedAt, 200);
        var current = RunStore.WithoutSession(afterA, "b", T0.AddMinutes(-5), 100);

        var restored = RunStore.WithSessionRestored(current, before, "a", closedAt, 200);

        // "a" is back; "b" is gone, and the run was dropped by the second close, so it comes back with "a" alone.
        Assert.Equal(["a"], restored.Single().Specs.Select(s => s.Name));
        Assert.DoesNotContain(restored.SelectMany(r => r.Specs), s => s.Name == "b");
    }

    [Fact]
    public void ASiblingClosedMeanwhileIsNotBroughtBackInARunThatStillHasSessions()
    {
        var before = new[] { Run("r1", "a", "b", "c") };
        var closedAt = T0.AddMinutes(-10);
        var afterA = RunStore.WithoutSession(before, "a", closedAt, 200);
        var current = RunStore.WithoutSession(afterA, "b", T0.AddMinutes(-5), 100);

        var restored = RunStore.WithSessionRestored(current, before, "a", closedAt, 200);

        Assert.Equal(["a", "c"], restored.Single().Specs.Select(s => s.Name));
    }

    [Fact]
    public void TheClosedTokensAndStartAreTakenBack()
    {
        var before = new[] { Run("r1", "a", "b") };
        var earlier = T0.AddMinutes(-50); // earlier than the run's own FirstStartedAt (T0 - 30 min): the close moved the run's start to it
        var current = RunStore.WithoutSession(before, "a", earlier, 400);
        Assert.Equal(earlier, current.Single().FirstStartedAt);

        var restored = RunStore.WithSessionRestored(current, before, "a", earlier, 400).Single();

        Assert.Equal(T0.AddMinutes(-30), restored.FirstStartedAt);
        Assert.Equal(1000, restored.ClosedOutputTokens);
    }

    [Fact]
    public void ARunThatStillHoldsTheSessionIsLeftAsItIs()
    {
        var before = new[] { Run("r1", "a", "b") };

        var restored = RunStore.WithSessionRestored(before, before, "a", null, 0);

        Assert.Equal(before, restored);
    }

    [Fact]
    public void RunsOfOtherSessionsAreKept()
    {
        var before = new[] { Run("r1", "a"), Run("r2", "x", "y") };
        var current = RunStore.WithoutSession(before, "a", null, 0);

        var restored = RunStore.WithSessionRestored(current, before, "a", null, 0);

        Assert.Equal(2, restored.Count);
        Assert.Equal(["a"], restored.Single(r => r.RunId == "r1").Specs.Select(s => s.Name));
        Assert.Equal(["x", "y"], restored.Single(r => r.RunId == "r2").Specs.Select(s => s.Name));
    }
}

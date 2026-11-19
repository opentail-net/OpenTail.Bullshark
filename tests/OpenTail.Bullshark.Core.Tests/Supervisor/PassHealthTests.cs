using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>Reliability batch R9: a failed pass shows once, and clears when it works again; its reason names no folder.</summary>
public sealed class PassHealthTests
{
    [Fact]
    public void AWorkingPassFromTheStartChangesNothing()
    {
        var health = new PassHealth();

        Assert.Equal(PassHealthChange.None, health.Observe(null));
        Assert.Equal(PassHealthChange.None, health.Observe(null));
        Assert.Null(health.Problem);
    }

    [Fact]
    public void AFailureStartsOnceAndDoesNotRepeat()
    {
        var health = new PassHealth();

        Assert.Equal(PassHealthChange.Failed, health.Observe("runs.json cannot be read"));
        Assert.Equal(PassHealthChange.StillFailed, health.Observe("runs.json cannot be read"));
        Assert.Equal(PassHealthChange.StillFailed, health.Observe("runs.json cannot be read"));
        Assert.Equal("runs.json cannot be read", health.Problem);
    }

    [Fact]
    public void AFailureThatChangesItsReasonIsUpdatedWithoutANewStart()
    {
        var health = new PassHealth();
        health.Observe("claude could not start");

        Assert.Equal(PassHealthChange.StillFailed, health.Observe("runs.json cannot be read"));
        Assert.Equal("runs.json cannot be read", health.Problem);
    }

    [Fact]
    public void ARecoveredPassClearsTheProblemOnce()
    {
        var health = new PassHealth();
        health.Observe("state.json cannot be read");

        Assert.Equal(PassHealthChange.Recovered, health.Observe(null));
        Assert.Equal(PassHealthChange.None, health.Observe(null));
        Assert.Null(health.Problem);
    }

    [Fact]
    public void AFailureAfterRecoveryStartsAgain()
    {
        var health = new PassHealth();
        health.Observe("state.json cannot be read");
        health.Observe(null);

        Assert.Equal(PassHealthChange.Failed, health.Observe("state.json cannot be read"));
    }

    [Fact]
    public void AFullPathIsCutToItsFileName()
    {
        var reason = PassHealth.Short(@"C:\Users\Dmitri\AppData\Local\OpenTail.Bullshark\runs.json cannot be read (The process cannot access the file).");

        Assert.Equal("runs.json cannot be read (The process cannot access the file).", reason);
        Assert.DoesNotContain("Users", reason);
        Assert.DoesNotContain("Dmitri", reason);
    }

    [Fact]
    public void APathInBracketsOrQuotesIsCutToo()
    {
        var reason = PassHealth.Short(@"Could not start 'claude' in ""D:\Git\repo"" (see (C:\Git\x\.claude.json)).");

        Assert.DoesNotContain("Git", reason);
        Assert.Contains(".claude.json", reason);
        Assert.Contains("repo", reason.Split(' ', '"').Select(w => w.TrimEnd('.')));
    }

    [Fact]
    public void AUncPathKeepsOnlyItsLastPart()
    {
        var reason = PassHealth.Short(@"share \\server\folder\state.json is gone");

        Assert.Equal(@"share state.json is gone", reason);
    }

    [Fact]
    public void ALongReasonIsShortened()
    {
        var reason = PassHealth.Short(new string('x', 400));

        Assert.True(reason.Length <= PassHealth.MaxReason + 1);
        Assert.EndsWith("…", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpacesAreCollapsed()
    {
        Assert.Equal("a b c", PassHealth.Short("a   b\r\n c"));
    }
}

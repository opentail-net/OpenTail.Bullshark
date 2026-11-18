using OpenTail.Bullshark.Core.Cache;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Cache;

/// <summary>The window's cache watch (tags plan, Part C1): a session that goes from warm or cooling to cold is seen once. Pure: no transcript.</summary>
public sealed class CacheWatchTests
{
    private static (string, CacheState) S(string id, CacheState state) => (id, state);

    [Fact]
    public void TheFirstUpdateOnlyRecordsTheStates()
    {
        var watch = new CacheWatch();

        Assert.Empty(watch.Update([S("a", CacheState.Warm), S("b", CacheState.Cooling), S("c", CacheState.Cold)]));
    }

    [Fact]
    public void AWarmSessionThatGoesColdIsReturned()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Warm)]);

        Assert.Equal(["a"], watch.Update([S("a", CacheState.Cold)]));
    }

    [Fact]
    public void ACoolingSessionThatGoesColdIsReturned()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Cooling)]);

        Assert.Equal(["a"], watch.Update([S("a", CacheState.Cold)]));
    }

    [Fact]
    public void AColdSessionStaysColdWithNothingReturned()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Cold)]);

        Assert.Empty(watch.Update([S("a", CacheState.Cold)]));
        Assert.Empty(watch.Update([S("a", CacheState.Cold)]));
    }

    [Fact]
    public void ColdThenWarmThenColdIsReturnedAgain()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Cold)]);
        Assert.Empty(watch.Update([S("a", CacheState.Warm)]));

        Assert.Equal(["a"], watch.Update([S("a", CacheState.Cold)]));
    }

    [Fact]
    public void AnUnknownStateIsIgnoredAndTheLastKnownStaysInForce()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Warm)]);

        // Unknown tells nothing: the session is still Warm as far as the watch knows, so the next Cold is a change.
        Assert.Empty(watch.Update([S("a", CacheState.Unknown)]));
        Assert.Equal(["a"], watch.Update([S("a", CacheState.Cold)]));
    }

    [Fact]
    public void AnUnknownFirstStateRecordsNothing()
    {
        var watch = new CacheWatch();
        Assert.Empty(watch.Update([S("a", CacheState.Unknown)]));

        // It was never seen as Warm, so its first known state is only recorded.
        Assert.Empty(watch.Update([S("a", CacheState.Cold)]));
    }

    [Fact]
    public void ASessionThatAppearsAlreadyColdIsNotReturned()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Warm)]);

        Assert.Empty(watch.Update([S("b", CacheState.Cold)]));
    }

    [Fact]
    public void ASessionThatDisappearsIsForgottenAndComesBackAsNew()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Warm)]);
        Assert.Empty(watch.Update([S("b", CacheState.Warm)]));

        // "a" was forgotten when it was left out, so its return as Cold is its first state, not a change.
        Assert.Empty(watch.Update([S("a", CacheState.Cold), S("b", CacheState.Warm)]));
    }

    [Fact]
    public void SeveralSessionsInOneUpdateAreReturnedInTheOrderGiven()
    {
        var watch = new CacheWatch();
        watch.Update([S("a", CacheState.Warm), S("b", CacheState.Cooling), S("c", CacheState.Warm), S("d", CacheState.Cold)]);

        var went = watch.Update([S("b", CacheState.Cold), S("a", CacheState.Cold), S("c", CacheState.Warm), S("d", CacheState.Cold)]);

        Assert.Equal(["b", "a"], went);
    }
}

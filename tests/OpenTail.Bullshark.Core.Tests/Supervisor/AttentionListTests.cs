using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The session the owner goes to next (Ctrl+Shift+N, keys-and-find plan step 3): the walk over the waiting sessions in list order, its wrap at the
/// end, and the cases that give nothing. The existing attention rules are in <see cref="AttentionTests"/>.
/// </summary>
public sealed class AttentionListTests
{
    private const string One = "11111111-2222-3333-4444-555555555555";
    private const string Two = "66666666-7777-8888-9999-000000000000";
    private const string Three = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A waiting item for a session. A null session id is a start that failed (no session).</summary>
    private static AttentionItem Waiting(string? sessionId, AttentionReason reason = AttentionReason.Blocked) =>
        new(sessionId is null ? "start" : "coder-" + sessionId[..1], sessionId, reason, "detail", Now);

    [Fact]
    public void WithNothingWaitingThereIsNoNextSession()
    {
        Assert.Null(AttentionList.NextWaiting([], One));
        Assert.Null(AttentionList.NextWaiting([], null));
    }

    [Fact]
    public void WithNoCurrentSessionTheFirstWaitingSessionIsTheNext()
    {
        var items = new[] { Waiting(Two), Waiting(One) };

        Assert.Equal(Two, AttentionList.NextWaiting(items, null));
    }

    [Fact]
    public void AfterTheFirstTheSecondIsNext()
    {
        var items = new[] { Waiting(One), Waiting(Two), Waiting(Three) };

        Assert.Equal(Two, AttentionList.NextWaiting(items, One));
        Assert.Equal(Three, AttentionList.NextWaiting(items, Two));
    }

    [Fact]
    public void AfterTheLastTheListWrapsToTheFirst()
    {
        var items = new[] { Waiting(One), Waiting(Two), Waiting(Three) };

        Assert.Equal(One, AttentionList.NextWaiting(items, Three));
    }

    [Fact]
    public void ALoneWaitingSessionThatIsCurrentGivesNull()
    {
        Assert.Null(AttentionList.NextWaiting([Waiting(One)], One));
    }

    [Fact]
    public void ACurrentSessionThatIsNotWaitingGivesTheFirst()
    {
        var items = new[] { Waiting(One), Waiting(Two) };

        Assert.Equal(One, AttentionList.NextWaiting(items, Three));
    }

    [Fact]
    public void ASessionWithSeveralItemsCountsOnce()
    {
        // One session holds two items (a blocked wait and an overlap), so the walk is over two sessions, not three items.
        var items = new[] { Waiting(One), Waiting(Two), Waiting(One, AttentionReason.Overlap) };

        Assert.Equal(Two, AttentionList.NextWaiting(items, One));
        Assert.Equal(One, AttentionList.NextWaiting(items, Two));
    }

    [Fact]
    public void AStartThatFailedHasNoSessionAndIsSkipped()
    {
        var items = new[] { Waiting(null, AttentionReason.StartFailed), Waiting(Two) };

        Assert.Equal(Two, AttentionList.NextWaiting(items, null));
        Assert.Equal(Two, AttentionList.NextWaiting(items, One));
        Assert.Null(AttentionList.NextWaiting([Waiting(null, AttentionReason.StartFailed)], null));
    }

    [Fact]
    public void SessionIdsMatchWithoutRegardToCase()
    {
        var items = new[] { Waiting(One), Waiting(Two) };

        Assert.Equal(Two, AttentionList.NextWaiting(items, One.ToUpperInvariant()));
        Assert.Null(AttentionList.NextWaiting([Waiting(One)], One.ToUpperInvariant()));
    }
}

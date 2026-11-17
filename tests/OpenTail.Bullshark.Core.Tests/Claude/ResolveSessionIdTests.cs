using System.Diagnostics;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// The session-id wait, run on a clock the test moves itself. A wait of several seconds takes no real time.
/// </summary>
public sealed class ResolveSessionIdTests
{
    private const string ShortId = "abcdef12";
    private const string FullId = "full-session-id";

    private static readonly string Listed =
        """[{"id":"abcdef12","kind":"background","sessionId":"full-session-id","name":"planner","state":"working"}]""";

    /// <summary>
    /// A clock that moves only when it is told to, and whose timers fire at once. A wait therefore moves the clock forward by
    /// the wait and returns straight away. It counts the waits.
    /// </summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public int Waits { get; private set; }

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits++;
            _now += dueTime;
            callback(state);
            return new FiredTimer();
        }

        private sealed class FiredTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AListedSessionIsReturnedWithoutAnyWait()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Listed));
        var clock = new ManualClock(Start);

        var sessionId = await new ClaudeCli(runner, clock).ResolveSessionIdAsync(ShortId, TimeSpan.FromSeconds(30));

        Assert.Equal(FullId, sessionId);
        Assert.Equal(0, clock.Waits);
    }

    [Fact]
    public async Task ASessionThatAppearsLaterIsReturnedAfterOneWaitPerMissedListing()
    {
        var listings = 0;
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(++listings < 3 ? "[]" : Listed));
        var clock = new ManualClock(Start);

        var sessionId = await new ClaudeCli(runner, clock).ResolveSessionIdAsync(ShortId, TimeSpan.FromSeconds(30));

        Assert.Equal(FullId, sessionId);
        Assert.Equal(3, listings);
        Assert.Equal(2, clock.Waits);
    }

    [Fact]
    public async Task ASessionThatNeverAppearsTimesOutOnTheClockAfterTheTimeoutWithNoRealWait()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("[]"));
        var clock = new ManualClock(Start);
        var wall = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() =>
            new ClaudeCli(runner, clock).ResolveSessionIdAsync(ShortId, TimeSpan.FromSeconds(5)));

        // The clock moved by the timeout, one second per wait, and the wall clock did not wait at all.
        Assert.Contains("within 5s", error.Message);
        Assert.Equal(Start.AddSeconds(5), clock.GetUtcNow());
        Assert.Equal(5, clock.Waits);
        Assert.True(wall.Elapsed < TimeSpan.FromSeconds(4), $"the wait took {wall.Elapsed.TotalSeconds:0.0}s of real time");
    }

    [Fact]
    public async Task TheDefaultClockStillWorksForAListedSession()
    {
        // Callers that do not pass a clock (the window, the supervisor) keep the system clock.
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Listed));

        Assert.Equal(FullId, await new ClaudeCli(runner).ResolveSessionIdAsync(ShortId, TimeSpan.FromSeconds(5)));
    }
}

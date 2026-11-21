using System.Globalization;
using System.IO;
using System.Text;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The usage-limit probe with its three outcomes kept apart (third review, 2026-11-21): a stop found, a read with no stop, and a read that could not be
/// made. A failed read keeps the stop known before, and with none it holds the session (fail closed) until a read succeeds. Each test writes its own
/// transcript into a temp folder that it removes. The lines are synthetic: no text of any real transcript.
/// </summary>
public sealed class LimitProbeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private const string Name = "planner-a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-limit-probe-" + Guid.NewGuid().ToString("N"));
    private readonly List<FileStream> _locks = [];

    public LimitProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Unlock();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static SessionRecord Record() =>
        new(SessionRole.Planner, SessionId, Name, Now.AddHours(-5), 0, [], "working", null);

    private static string Stamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A usage-limit entry written at <paramref name="hit"/> that resets at <paramref name="resets"/>, in the shape the transcripts have.</summary>
    private static string Limit(DateTimeOffset hit, DateTimeOffset resets) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(hit) + "\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":" + resets.ToUnixTimeSeconds() +
        ",\"rateLimitType\":\"five_hour\"}},\"message\":{\"model\":\"<synthetic>\",\"content\":[{\"type\":\"text\",\"text\":\"synthetic limit\"}]}}";

    /// <summary>An ordinary assistant reply. It ends a stop.</summary>
    private static string Reply(DateTimeOffset at) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(at) + "\",\"message\":{\"id\":\"msg-1\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"synthetic reply\"}]}}";

    /// <summary>Writes the session's transcript with the given lines and returns its path.</summary>
    private string Transcript(params string[] lines)
    {
        var path = Path.Combine(_root, SessionId + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    /// <summary>Holds the file open with no sharing, so no reader can open it, until <see cref="Unlock"/>.</summary>
    private void Lock(string path) => _locks.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));

    private void Unlock()
    {
        foreach (var held in _locks) held.Dispose();
        _locks.Clear();
    }

    /// <summary>One probe of the session, with its transcript known (or not, when <paramref name="path"/> is null).</summary>
    private SessionWindow.LimitProbe Probe(string? path, IReadOnlyDictionary<string, LimitStop?> previous, DateTimeOffset? now = null)
    {
        var pathOf = path is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [SessionId] = path };
        return SessionWindow.ProbeLimits([SessionId], pathOf, new HashSet<string>(), _root, now ?? Now, readInForce: false, previous);
    }

    private static IReadOnlyDictionary<string, DateTimeOffset> Holds(SessionWindow.LimitProbe probe, DateTimeOffset? now = null) =>
        SessionWindow.LimitHoldsOf(new Dictionary<string, SessionRecord> { [Name] = Record() }, probe.Stops, now ?? Now, probe.Unknown);

    [Fact]
    public void ATranscriptFileThatIsGoneIsNotUnknownAndHoldsNothingWithoutAKnownStop()
    {
        var gone = Path.Combine(_root, "gone.jsonl");

        var probe = Probe(gone, new Dictionary<string, LimitStop?>());

        Assert.Empty(probe.Unknown);
        Assert.Empty(Holds(probe));
    }

    [Fact]
    public void ATranscriptFileThatIsGoneKeepsAStopKnownBefore()
    {
        var hit = Now.AddMinutes(-30);
        var reset = Now.AddHours(3);
        var path = Transcript(Limit(hit, reset));
        var first = Probe(path, new Dictionary<string, LimitStop?>());
        File.Delete(path);

        var second = Probe(path, first.Stops);

        Assert.Equal(reset, Assert.Single(Holds(second)).Value);
    }

    [Fact]
    public void AFailedReadKeepsTheStopKnownBeforeAndHoldsTheSession()
    {
        var hit = Now.AddMinutes(-30);
        var reset = Now.AddHours(3);
        var path = Transcript(Limit(hit, reset));
        var first = Probe(path, new Dictionary<string, LimitStop?>());
        Assert.Equal(new LimitStop(hit, reset, "five_hour"), first.Stops[SessionId]);

        Lock(path);
        var second = Probe(path, first.Stops);

        Assert.Empty(second.Unknown);
        Assert.Equal(first.Stops[SessionId], second.Stops[SessionId]);
        Assert.Equal(reset, Assert.Single(Holds(second)).Value);
    }

    [Fact]
    public void AFirstReadThatFailsFailsClosedAndTheHoldRollsWhileItKeepsFailing()
    {
        var path = Transcript(Limit(Now.AddMinutes(-30), Now.AddHours(3)));
        Lock(path);

        var first = Probe(path, new Dictionary<string, LimitStop?>());

        Assert.Contains(SessionId, first.Unknown);
        Assert.Equal(Now.AddMinutes(15), Holds(first)[Name]);

        // Ten minutes later the read still fails: the hold is renewed from that pass, so it is still held.
        var later = Probe(path, first.Stops, Now.AddMinutes(10));

        Assert.Contains(SessionId, later.Unknown);
        Assert.Equal(Now.AddMinutes(25), Holds(later, Now.AddMinutes(10))[Name]);
    }

    [Fact]
    public void AReadThatWorksWithNoStopClearsAKeptHold()
    {
        var previous = new Dictionary<string, LimitStop?> { [SessionId] = new LimitStop(Now.AddMinutes(-30), Now.AddHours(3), "five_hour") };

        // The limit entry is followed by an ordinary reply: the stop has ended.
        var path = Transcript(Limit(Now.AddMinutes(-30), Now.AddHours(3)), Reply(Now.AddMinutes(-10)));
        var ended = Probe(path, previous);

        Assert.Null(ended.Stops[SessionId]);
        Assert.Empty(ended.Unknown);
        Assert.Empty(Holds(ended));

        // A transcript with no limit entry at all also reads as no stop, and clears the hold the same way.
        var plain = Transcript(Reply(Now.AddMinutes(-10)));
        var none = Probe(plain, previous);

        Assert.Null(none.Stops[SessionId]);
        Assert.Empty(none.Unknown);
        Assert.Empty(Holds(none));
    }

    [Fact]
    public void AKnownStopIsKeptWhenTheTranscriptPathIsTemporarilyUnknown()
    {
        var stop = new LimitStop(Now.AddMinutes(-30), Now.AddHours(3), "five_hour");
        var previous = new Dictionary<string, LimitStop?> { [SessionId] = stop };

        // No transcript under the root and no known path: the stop kept before stands, and the session stays held.
        var probe = Probe(null, previous);

        Assert.Equal(stop, probe.Stops[SessionId]);
        Assert.Empty(probe.Unknown);
        Assert.Equal(stop.ResetsAt, Assert.Single(Holds(probe)).Value);
    }

    [Fact]
    public void ANewSessionWithNoTranscriptYetHasNoStopAndNoHold()
    {
        var probe = Probe(null, new Dictionary<string, LimitStop?>());

        Assert.False(probe.Stops.ContainsKey(SessionId));
        Assert.Empty(probe.Unknown);
        Assert.Empty(Holds(probe));
    }

    [Fact]
    public void TheFirstGoodReadAfterAFailureClearsTheUnknownHold()
    {
        // The transcript holds a limit entry that an ordinary reply has since ended, so the good read has no stop.
        var path = Transcript(Limit(Now.AddMinutes(-30), Now.AddHours(3)), Reply(Now.AddMinutes(-10)));
        Lock(path);
        var failed = Probe(path, new Dictionary<string, LimitStop?>());
        Assert.Equal(Now.AddMinutes(15), Holds(failed)[Name]);

        Unlock();
        var read = Probe(path, failed.Stops, Now.AddMinutes(1));

        Assert.Empty(read.Unknown);
        Assert.Empty(Holds(read, Now.AddMinutes(1)));
    }

    [Fact]
    public void TheFirstLossPassHoldsTheSessionAcrossAnUnreadableTranscript()
    {
        // The planner is lost: its record is working, and no listing shows it. Decide resumes it unless a limit hold stands.
        var spec = new SessionSpec(SessionRole.Planner, Name, @"C:\Work", "opus", "high", "brief", "auto");
        var hit = Now.AddMinutes(-30);
        var reset = Now.AddHours(3);
        bool ResumeIsHeld(IReadOnlyDictionary<string, DateTimeOffset> holds, DateTimeOffset now)
        {
            DateTimeOffset? until = holds.TryGetValue(Name, out var found) ? found : null;
            var actions = SupervisorPolicy.Decide(spec, Record(), [], now, SupervisorRules.Default, limitHoldUntil: until);
            return actions.Count == 0;
        }

        // The pass reads the stop while the transcript is readable: the lost session is held until the reset.
        var path = Transcript(Limit(hit, reset));
        var first = Probe(path, new Dictionary<string, LimitStop?>());
        Assert.True(ResumeIsHeld(Holds(first), Now));

        // The session disappears and its transcript cannot be read: the probe keeps the known stop, so it is still held.
        Lock(path);
        var unreadable = Probe(path, first.Stops);
        Assert.True(ResumeIsHeld(Holds(unreadable), Now));

        // The transcript is readable again and the session has replied since: the hold is gone and the session is resumed.
        Unlock();
        Transcript(Limit(hit, reset), Reply(Now.AddMinutes(-5)));
        var readable = Probe(path, unreadable.Stops);
        Assert.Empty(Holds(readable));
        Assert.False(ResumeIsHeld(Holds(readable), Now));
        var resumed = SupervisorPolicy.Decide(spec, Record(), [], Now, SupervisorRules.Default, limitHoldUntil: null);
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(resumed));
    }
}

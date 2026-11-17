using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The timeline's log (additions item 5): appends in order, rotation at the size limit, a line cut off at the end, two writers at once,
/// and the runner writing its actions to the sink.
/// </summary>
public sealed class EventLogTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-events-" + Guid.NewGuid().ToString("N")[..8]);

    public EventLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Path_(string name = "events.jsonl") => Path.Combine(_dir, name);

    private static SupervisorEvent Event(int n) => new(At.AddSeconds(n), "bullshark-coder-abcd", "state", $"working -> blocked {n}");

    [Fact]
    public void EventsAreAppendedOneLineEachAndReadBackInOrder()
    {
        var log = new EventLog(Path_());

        log.Write(Event(1));
        log.Write(Event(2));

        var read = log.Read();
        Assert.Equal(2, read.Count);
        Assert.Equal(Event(1), read[0]);
        Assert.Equal(Event(2), read[1]);
        Assert.Equal(2, File.ReadAllLines(Path_()).Length);
    }

    [Fact]
    public void WhenTheFileReachesTheLimitItIsMovedAndTheOrderIsKept()
    {
        // Each line is about 115 bytes, so the file is moved on the fourth write, after three lines. Only one older file is kept,
        // so five writes leave one rotated file and two current lines, and all five are read back in order.
        var log = new EventLog(Path_(), rotateBytes: 300);

        for (var i = 1; i <= 5; i++) log.Write(Event(i));

        Assert.True(File.Exists(log.RotatedFilePath));
        Assert.Equal(Path_("events.1.jsonl"), log.RotatedFilePath);
        Assert.Equal(Enumerable.Range(1, 5).Select(Event), log.Read());
    }

    [Fact]
    public void ALineCutOffAtTheEndIsSkippedAndTheRestAreKept()
    {
        var log = new EventLog(Path_());
        log.Write(Event(1));
        log.Write(Event(2));
        File.AppendAllText(Path_(), "{\"At\":\"2026-11-17T09:0");   // a write that did not finish

        var read = log.Read();

        Assert.Equal([Event(1), Event(2)], read);
    }

    [Fact]
    public void AnEventWrittenAfterACutOffLineIsReadBack()
    {
        var log = new EventLog(Path_());
        log.Write(Event(1));
        File.AppendAllText(Path_(), "{\"At\":\"2026-11-17T09:0");   // a write that did not finish, with no newline after it

        log.Write(Event(2));

        // The cut-off line is ended before the new event, so only that line is skipped and the new event is kept.
        Assert.Equal([Event(1), Event(2)], log.Read());
    }

    [Fact]
    public async Task TwoWritersAtOnceLoseNoLines()
    {
        var path = Path_();
        var first = new EventLog(path);
        var second = new EventLog(path);

        var a = Task.Run(() => { for (var i = 0; i < 40; i++) first.Write(Event(i)); });
        var b = Task.Run(() => { for (var i = 100; i < 140; i++) second.Write(Event(i)); });
        await Task.WhenAll(a, b);

        var read = new EventLog(path).Read();
        Assert.Equal(80, read.Count);
        Assert.Equal(80, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task TheRunnerWritesItsStartFailureToTheSink()
    {
        // The CLI answers the launch with nothing, so the start fails and the runner writes the alert to the sink.
        var sink = new ListSink();
        var runner = new SupervisorRunner(new ClaudeCli(new FakeCliRunner()), new StateStore(Path_("state.json")), TimeProvider.System,
            resolveTimeout: TimeSpan.FromMilliseconds(1))
        {
            Events = sink,
        };
        var spec = new SessionSpec(SessionRole.Coder, "bullshark-coder-abcd", @"C:\work", "haiku", "medium", "brief", "auto");

        await runner.CheckOnceAsync([spec], agents: []);

        var alert = Assert.Single(sink.Items);
        Assert.Equal("bullshark-coder-abcd", alert.Session);
        Assert.Equal("alert:StartFailed", alert.Event);
    }

    [Fact]
    public void AMissingLogReadsAsEmpty()
    {
        Assert.Empty(new EventLog(Path_()).Read());
    }

    [Fact]
    public void AFolderTheUserCannotOpenIsAnErrorNotAnEmptyTimeline()
    {
        if (!OperatingSystem.IsWindows()) return;
        var log = new EventLog(Path_());
        log.Write(Event(1));

        using (Unreadable.FolderWithoutMe(_dir))
        {
            // File.Exists is false here. The old read skipped the file and showed an empty timeline with no error.
            var error = Record.Exception(() => log.Read());

            Assert.True(error is UnauthorizedAccessException or IOException,
                $"expected an access error, got {error?.GetType().Name ?? "no error"}");
        }
    }

    /// <summary>Keeps what the runner writes, in memory.</summary>
    private sealed class ListSink : IEventSink
    {
        public List<SupervisorEvent> Items { get; } = new();

        public void Write(SupervisorEvent evt) => Items.Add(evt);
    }
}

using System.Globalization;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The run timeline's rules (round 5 item 10): a row shows the date and the time, the events of a run are those that name it (and, for
/// older lines, those that have its session name), and only the newest rows are drawn.
/// </summary>
public sealed class TimelineRowsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private const string CoderName = "bullshark-coder-abcd";
    private const string OtherCoderName = "bullshark-coder-zzzz";

    private static RunRecord Run(string runId, params string[] names) =>
        new(runId, "plan-code", @"C:\work", "auto", At.AddHours(-1),
            names.Select(name => new SessionSpec(SessionRole.Coder, name, @"C:\work", "haiku", "medium", "brief", "auto")).ToList());

    private static SupervisorEvent Event(string session, string? runId, int minute) =>
        new(At.AddMinutes(minute), session, "state", $"working -> blocked {minute}", runId);

    [Fact]
    public void EachRowShowsTheLocalDateAndTheTime()
    {
        var evt = Event(CoderName, "abcd", 5);

        var line = TimelineRows.Line(evt);

        Assert.StartsWith(evt.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), line);
        Assert.Contains(CoderName, line);
        Assert.EndsWith("working -> blocked 5", line);
    }

    [Fact]
    public void ARunsOwnEventsAreThoseThatNameIt()
    {
        var run = Run("abcd", CoderName);
        var events = new[]
        {
            Event(CoderName, "abcd", 1),
            Event(CoderName, "zzzz", 2),   // another run that reused the session name
            Event(OtherCoderName, "abcd", 3),
        };

        var shown = TimelineRows.ForRun(events, run, session: null);

        Assert.Equal([events[0], events[2]], shown);
    }

    [Fact]
    public void AnOlderEventWithoutARunIdBelongsToTheRunThatHasItsName()
    {
        var run = Run("abcd", CoderName);
        var older = Event(CoderName, runId: null, 1);
        var stranger = Event(OtherCoderName, runId: null, 2);

        var shown = TimelineRows.ForRun([older, stranger], run, session: null);

        Assert.Equal([older], shown);
    }

    [Fact]
    public void ASessionChosenInTheListNarrowsTheRunToThatSession()
    {
        var run = Run("abcd", CoderName, OtherCoderName);
        var mine = Event(CoderName, "abcd", 1);
        var theirs = Event(OtherCoderName, "abcd", 2);

        Assert.Equal([theirs], TimelineRows.ForRun([mine, theirs], run, session: OtherCoderName));
        Assert.Equal([mine, theirs], TimelineRows.ForRun([mine, theirs], run, session: null));
    }

    [Fact]
    public void TheNewestRowsAreDrawnAndTheRestAreCountedAsOlder()
    {
        var events = Enumerable.Range(1, 5).Select(i => Event(CoderName, "abcd", i)).ToList();

        var page = TimelineRows.Newest(events, limit: 2);

        Assert.Equal(3, page.Older);
        Assert.Equal([events[3], events[4]], page.Rows);
    }

    [Fact]
    public void ALimitLargerThanTheLogDrawsEverythingAndNothingIsOlder()
    {
        var events = Enumerable.Range(1, 3).Select(i => Event(CoderName, "abcd", i)).ToList();

        var page = TimelineRows.Newest(events, TimelineRows.DefaultLimit);

        Assert.Equal(0, page.Older);
        Assert.Equal(events, page.Rows);
    }

    [Fact]
    public void ALineWrittenBeforeRunIdsStillReadsAsAnEventWithNoRunId()
    {
        var path = Path.Combine(Path.GetTempPath(), "bullshark-timeline-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
        try
        {
            // The shape of a line from before round 5 item 10: no RunId property.
            File.WriteAllText(path, """{"At":"2026-11-17T09:00:00+00:00","Session":"bullshark-coder-abcd","Event":"start","Detail":""}""" + "\n");

            var read = Assert.Single(new EventLog(path).Read());

            Assert.Equal("bullshark-coder-abcd", read.Session);
            Assert.Null(read.RunId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

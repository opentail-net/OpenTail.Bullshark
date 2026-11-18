using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The "Ready for review" lane (ready-for-review-2026-11-18): which finished coders are ready, the reviewed marks in ui.json, and their order.
/// The list is pure, so these tests need no window and no files.
/// </summary>
public sealed class ReviewQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private const string CoderA = "11111111-2222-3333-4444-555555555555";
    private const string CoderB = "66666666-7777-8888-9999-000000000000";
    private const string PlannerId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private static SessionRecord Record(string name, string sessionId, SessionRole role = SessionRole.Coder) =>
        new(role, sessionId, name, Now.AddHours(-2), 0, [], null, null);

    private static AgentInfo Agent(string sessionId, string state) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: "x", Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    private static ReviewSource Source(int files, DateTimeOffset lastActivity) => new(files, lastActivity);

    private static Dictionary<string, ReviewSource> Sources(params (string Id, ReviewSource Source)[] pairs) =>
        pairs.ToDictionary(p => p.Id, p => p.Source, StringComparer.Ordinal);

    private static Dictionary<string, DateTimeOffset> Marks(params (string Id, DateTimeOffset At)[] pairs) =>
        pairs.ToDictionary(p => p.Id, p => p.At, StringComparer.Ordinal);

    [Fact]
    public void ADoneCoderWithFilesIsReadyForReview()
    {
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done")],
            Sources((CoderA, Source(3, Now))),
            Marks());

        var item = Assert.Single(items);
        Assert.Equal("coder", item.Name);
        Assert.Equal(CoderA, item.SessionId);
        Assert.Equal(3, item.FilesWritten);
        Assert.Equal(Now, item.FinishedAt);
    }

    [Fact]
    public void ADoneSessionThatIsNotACoderIsNotReady()
    {
        var items = ReviewQueue.From(
            [Record("planner", PlannerId, SessionRole.Planner)],
            [Agent(PlannerId, "done")],
            Sources((PlannerId, Source(2, Now))),
            Marks());

        Assert.Empty(items);
    }

    [Fact]
    public void ADoneCoderWithNoFilesWrittenIsNotReady()
    {
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done")],
            Sources((CoderA, Source(0, Now))),
            Marks());

        Assert.Empty(items);
    }

    [Fact]
    public void ASessionThatIsNotDoneIsNotReady()
    {
        foreach (var state in new[] { "working", "blocked", "stopped", "failed" })
        {
            var items = ReviewQueue.From(
                [Record("coder", CoderA)],
                [Agent(CoderA, state)],
                Sources((CoderA, Source(4, Now))),
                Marks());
            Assert.Empty(items);
        }
    }

    [Fact]
    public void ASessionNotListedIsNotReady()
    {
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [],
            Sources((CoderA, Source(4, Now))),
            Marks());

        Assert.Empty(items);
    }

    [Fact]
    public void AMarkAtTheLastActivityHidesTheCoder()
    {
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done")],
            Sources((CoderA, Source(3, Now))),
            Marks((CoderA, Now)));

        Assert.Empty(items);
    }

    [Fact]
    public void ALaterActivityAfterTheMarkMakesTheCoderReadyAgain()
    {
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done")],
            Sources((CoderA, Source(5, Now.AddMinutes(30)))),
            Marks((CoderA, Now)));

        var item = Assert.Single(items);
        Assert.Equal(Now.AddMinutes(30), item.FinishedAt);
        Assert.Equal(5, item.FilesWritten);
    }

    [Fact]
    public void ACoderWithNoSourceIsNotReady()
    {
        // No transcript read for the session (the pass has not read it yet): it is not shown, rather than shown with no facts.
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done")],
            Sources((CoderB, Source(2, Now))),
            Marks());

        Assert.Empty(items);
    }

    [Fact]
    public void ASourceWithNoRecordIsIgnored()
    {
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done"), Agent(CoderB, "done")],
            Sources((CoderA, Source(1, Now)), (CoderB, Source(1, Now))),
            Marks());

        var item = Assert.Single(items);
        Assert.Equal(CoderA, item.SessionId);
    }

    [Fact]
    public void TheOldestFinishIsListedFirst()
    {
        var items = ReviewQueue.From(
            [Record("zeta", CoderA), Record("alpha", CoderB)],
            [Agent(CoderA, "done"), Agent(CoderB, "done")],
            Sources((CoderA, Source(1, Now.AddMinutes(10))), (CoderB, Source(1, Now.AddMinutes(-10)))),
            Marks());

        Assert.Equal(new[] { "alpha", "zeta" }, items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void TheSameFinishTimeIsOrderedByName()
    {
        var items = ReviewQueue.From(
            [Record("zeta", CoderA), Record("alpha", CoderB)],
            [Agent(CoderA, "done"), Agent(CoderB, "done")],
            Sources((CoderA, Source(1, Now)), (CoderB, Source(1, Now))),
            Marks());

        Assert.Equal(new[] { "alpha", "zeta" }, items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void ABlockedCoderIsNotReadyWhateverItsFiles()
    {
        // The lane depends on the listing and the sources only. A coder that waits for an answer is in Needs you, not here.
        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "blocked")],
            Sources((CoderA, Source(9, Now))),
            Marks());

        Assert.Empty(items);
    }

    [Fact]
    public void AMissingMarkValueGivesNoMarks()
    {
        Assert.Empty(ReviewedMarks.Parse(null));
        Assert.Empty(ReviewedMarks.Parse(JsonValue.Create("not an object")));
        Assert.Empty(ReviewedMarks.Parse(new JsonArray(JsonValue.Create("2026-11-18T10:00:00Z"))));
    }

    [Fact]
    public void ABadEntryInTheMarksIsSkippedAndTheRestKept()
    {
        var value = JsonNode.Parse(
            "{ \"good\": \"2026-11-18T10:00:00.0000000+00:00\", \"notATime\": \"soon\", \"number\": 42, \"nested\": { \"x\": 1 }, \"empty\": \"\" }");

        var marks = ReviewedMarks.Parse(value);

        var only = Assert.Single(marks);
        Assert.Equal("good", only.Key);
        Assert.Equal(Now, only.Value);
    }

    [Fact]
    public void MarksRoundTripThroughTheJsonObject()
    {
        var later = Now.AddSeconds(1).AddTicks(7);
        var read = ReviewedMarks.Parse(ReviewedMarks.Write(Marks((CoderA, Now), (CoderB, later))));

        Assert.Equal(Now, read[CoderA]);
        Assert.Equal(later, read[CoderB]);
    }

    [Fact]
    public void AMarkRoundTripKeepsTheCoderHiddenAtItsActivityTime()
    {
        // The mark is the activity time. After a write and a read (sub-second part included) the coder at exactly that time stays hidden.
        var at = new DateTimeOffset(2026, 11, 18, 10, 0, 0, 123, TimeSpan.Zero).AddTicks(4567);
        var mark = ReviewedMarks.Parse(ReviewedMarks.Write(Marks((CoderA, at))));

        var items = ReviewQueue.From(
            [Record("coder", CoderA)],
            [Agent(CoderA, "done")],
            Sources((CoderA, Source(1, at))),
            mark);

        Assert.Empty(items);
    }
}

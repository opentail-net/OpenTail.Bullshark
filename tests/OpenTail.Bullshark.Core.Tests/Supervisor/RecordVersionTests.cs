using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The version of a stored session record (R5): the store alone bumps it, once per change; a write made from an older read is ignored and
/// counted; a file without a version loads as 0. The state files are temporary ones; the owner's file is never touched.
/// </summary>
public sealed class RecordVersionTests : IDisposable
{
    private const string Name = "bullshark-coder-ab12";
    private const string PlannerSession = "11111111-2222-3333-4444-555555555555";
    private const string ShortId = "abcdef12";
    private static readonly DateTimeOffset When = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-version-" + Guid.NewGuid().ToString("N")[..8]);

    public RecordVersionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SessionRecord Coder(string? lastState = null) =>
        new(SessionRole.Coder, "s1", Name, When, 0, [], lastState, null);

    private StateStore Store() => new(Path.Combine(_dir, "state.json"));

    /// <summary>A store holding one coder record, which is at version 1.</summary>
    private static StateStore Seeded(StateStore store)
    {
        store.Save(new Dictionary<string, SessionRecord> { [Name] = Coder() });
        return store;
    }

    [Fact]
    public void ARecordWithoutAVersionLoadsAsVersionZero()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, """
            [
              {
                "Role": "Coder",
                "SessionId": "s1",
                "Name": "bullshark-coder-ab12",
                "StartedAt": "2026-11-15T10:00:00+00:00",
                "Restarts": 0,
                "RestartTimes": [],
                "LastState": "working",
                "LastAlert": null
              }
            ]
            """);

        var loaded = new StateStore(path).Load()[Name];

        Assert.Equal(0, loaded.Version);
        Assert.Equal("working", loaded.LastState);
    }

    [Fact]
    public void AVersionInTheFileLoadsAsWritten()
    {
        var path = Path.Combine(_dir, "versioned.json");
        File.WriteAllText(path, """[{"Role":"Coder","SessionId":"s1","Name":"bullshark-coder-ab12","StartedAt":"2026-11-15T10:00:00+00:00","Restarts":0,"RestartTimes":[],"LastState":null,"LastAlert":null,"Version":7}]""");

        Assert.Equal(7, new StateStore(path).Load()[Name].Version);
    }

    [Fact]
    public void TheFirstSaveOfARecordGivesVersionOne()
    {
        var store = Seeded(Store());

        Assert.Equal(1, store.Load()[Name].Version);
    }

    [Fact]
    public void EachChangeBumpsTheVersionByExactlyOne()
    {
        var store = Seeded(Store());

        store.Update(records => records[Name] = records[Name] with { LastState = "working" });
        Assert.Equal(2, store.Load()[Name].Version);

        store.Update(records => records[Name] = records[Name] with { LastState = "idle" });
        Assert.Equal(3, store.Load()[Name].Version);
    }

    [Fact]
    public void TwoChangesInOneUpdateBumpTheVersionOnce()
    {
        var store = Seeded(Store());

        store.Update(records =>
        {
            records[Name] = records[Name] with { LastState = "working" };
            records[Name] = records[Name] with { LastAlert = "Blocked: a question" };
        });

        Assert.Equal(2, store.Load()[Name].Version);
    }

    [Fact]
    public void AnUpdateThatChangesNothingKeepsTheVersion()
    {
        var store = Seeded(Store());

        store.Update(_ => { });
        // The version a caller sets is not content: the same content keeps the stored version.
        store.Update(records => records[Name] = records[Name] with { Version = 99 });

        Assert.Equal(1, store.Load()[Name].Version);
    }

    [Fact]
    public void AWriteMadeFromTheCurrentReadIsApplied()
    {
        var store = Seeded(Store());
        var read = store.Load()[Name];

        store.Update(records => store.PutFrom(records, Name, read with { LastState = "idle" }, read.Version));

        var after = store.Load()[Name];
        Assert.Equal("idle", after.LastState);
        Assert.Equal(2, after.Version);
        Assert.Equal(0, store.StaleWrites);
    }

    [Fact]
    public void AWriteMadeFromAnOlderReadIsIgnoredAndCounted()
    {
        var store = Seeded(Store());
        var older = store.Load()[Name];
        store.Update(records => records[Name] = records[Name] with { LastState = "working" });

        // The write's version would be 2, which is equal to the stored version 2: it is ignored.
        var result = false;
        store.Update(records => result = store.PutFrom(records, Name, older with { LastState = "idle" }, older.Version));

        Assert.False(result);
        Assert.Equal(1, store.StaleWrites);
        var stored = store.Load()[Name];
        Assert.Equal("working", stored.LastState);
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public void AWriteOlderThanTheStoredRecordIsIgnored()
    {
        var store = Seeded(Store());
        store.Update(records => records[Name] = records[Name] with { LastState = "working" });
        store.Update(records => records[Name] = records[Name] with { LastState = "idle" });

        store.Update(records => store.PutFrom(records, Name, Coder("done"), 0));

        Assert.Equal(1, store.StaleWrites);
        var stored = store.Load()[Name];
        Assert.Equal("idle", stored.LastState);
        Assert.Equal(3, stored.Version);
    }

    [Fact]
    public void ARestoreAfterAForgetDoesNotRollTheVersionBack()
    {
        var store = Seeded(Store());
        store.Update(records => records[Name] = records[Name] with { LastState = "working" });
        store.Update(records => records[Name] = records[Name] with { LastState = "idle" });
        var previous = store.Load()[Name];
        store.Forget(Name);

        store.Update(records => store.PutFrom(records, Name, previous, previous.Version));

        Assert.Equal(4, store.Load()[Name].Version);
        Assert.Equal(0, store.StaleWrites);
    }

    [Fact]
    public void AWriteToAnAbsentRecordStartsAtVersionOne()
    {
        var store = Store();

        store.Update(records => store.PutFrom(records, Name, Coder(), 0));

        Assert.Equal(1, store.Load()[Name].Version);
    }

    private static SupervisorRunner Runner(FakeCliRunner cli, StateStore store, TimeProvider clock) =>
        new(new ClaudeCli(cli), store, clock, resolveTimeout: TimeSpan.FromSeconds(3));

    /// <summary>The planner record the owner had before the pass: a session that the listing no longer shows, so the pass resumes it.</summary>
    private static StateStore SeededPlanner(string dir)
    {
        var store = new StateStore(Path.Combine(dir, "state.json"));
        store.Save(new Dictionary<string, SessionRecord>
        {
            [TestSpecs.PlannerName] = new(SessionRole.Planner, PlannerSession, TestSpecs.PlannerName, When, 0, [], null, null),
        });
        return store;
    }

    [Fact]
    public async Task APassThatResumesASessionBumpsItsRecordOnce()
    {
        var store = SeededPlanner(_dir);
        var lost = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            "--bg" => FakeCliRunner.Ok($"backgrounded · {ShortId} · planner\n"),
            _ => FakeCliRunner.Ok(),
        });

        var performed = await Runner(lost, store, new FixedClock(When)).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "brief")]);

        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        var kept = store.Load()[TestSpecs.PlannerName];
        Assert.Equal(2, kept.Version);
        Assert.Equal(1, kept.Restarts);
        Assert.Equal(0, store.StaleWrites);
    }

    [Fact]
    public async Task APassChangeMadeFromAnOlderReadIsIgnoredWhenTheOwnerWroteFirst()
    {
        var store = SeededPlanner(_dir);
        var lost = new FakeCliRunner(args =>
        {
            if (args[0] == "agents") return FakeCliRunner.Ok("[]");
            if (args[0] != "--bg") return FakeCliRunner.Ok();
            // The owner's change lands while the pass is resuming the session: a newer record for the same name.
            store.Update(records => records[TestSpecs.PlannerName] = records[TestSpecs.PlannerName] with { LastAlert = "owner note" });
            return FakeCliRunner.Ok($"backgrounded · {ShortId} · planner\n");
        });

        var performed = await Runner(lost, store, new FixedClock(When)).CheckOnceAsync([TestSpecs.Planner(@"C:\W", "brief")]);

        // The resume happened, but its record copy was made from the read before the owner's change, so it is not stored.
        Assert.IsType<SupervisorAction.Resume>(Assert.Single(performed));
        var kept = store.Load()[TestSpecs.PlannerName];
        Assert.Equal(2, kept.Version);
        Assert.Equal("owner note", kept.LastAlert);
        Assert.Equal(0, kept.Restarts);
        Assert.Equal(1, store.StaleWrites);
    }
}

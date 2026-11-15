using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-state-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// The state file as the app wrote it before records were keyed by name: a JSON list, Role and Name in every record.
    /// The ids are synthetic. The shape is copied from the real file (checked 2026-11-15).
    /// </summary>
    private const string LegacyFile = """
        [
          {
            "Role": "Planner",
            "SessionId": "11111111-aaaa-4bbb-8ccc-000000000001",
            "Name": "ovc-planner",
            "StartedAt": "2026-11-14T18:20:47.9759967+00:00",
            "Restarts": 0,
            "RestartTimes": [],
            "LastState": "blocked",
            "LastAlert": "Blocked: Waiting for input: a permission answer, an answer to a question, or an idle session. Open its tab to see which."
          },
          {
            "Role": "Coder",
            "SessionId": "22222222-aaaa-4bbb-8ccc-000000000002",
            "Name": "ovc-coder",
            "StartedAt": "2026-11-14T18:20:47.9759967+00:00",
            "Restarts": 0,
            "RestartTimes": [],
            "LastState": "done",
            "LastAlert": null
          }
        ]
        """;

    public StateStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void ForgetRemovesOneRecordAndKeepsTheOthers()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var when = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        store.Save(new Dictionary<string, SessionRecord>
        {
            ["bullshark-coder-ab12"] = new(SessionRole.Coder, "s1", "bullshark-coder-ab12", when, 0, [], null, null),
            ["bullshark-planner-ab12"] = new(SessionRole.Planner, "s2", "bullshark-planner-ab12", when, 0, [], null, null),
        });

        store.Forget("bullshark-coder-ab12");

        var left = store.Load();
        Assert.False(left.ContainsKey("bullshark-coder-ab12"));
        Assert.Equal("s2", left["bullshark-planner-ab12"].SessionId);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MissingFileLoadsAsEmpty()
    {
        var store = new StateStore(Path.Combine(_dir, "none.json"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void RoundTripKeepsEveryFieldUnderTheSessionName()
    {
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var when = new DateTimeOffset(2026, 11, 14, 12, 30, 0, TimeSpan.Zero);
        var record = new SessionRecord(SessionRole.Coder, "aaaaaaaa-0000-0000-0000-000000000001", "coder", when,
            Restarts: 2, RestartTimes: [when.AddMinutes(-5), when], LastState: "done", LastAlert: "Blocked: x");

        store.Save(new Dictionary<string, SessionRecord> { ["coder"] = record });
        var loaded = new StateStore(store.Path).Load()["coder"];

        Assert.Equal(record.SessionId, loaded.SessionId);
        Assert.Equal(record.Name, loaded.Name);
        Assert.Equal(record.Role, loaded.Role);
        Assert.Equal(record.StartedAt, loaded.StartedAt);
        Assert.Equal(2, loaded.Restarts);
        Assert.Equal(record.RestartTimes, loaded.RestartTimes);
        Assert.Equal("done", loaded.LastState);
        Assert.Equal("Blocked: x", loaded.LastAlert);
    }

    [Fact]
    public void SaveLeavesNoTemporaryFileBehind()
    {
        var path = Path.Combine(_dir, "state.json");
        var store = new StateStore(path);
        store.Save(new Dictionary<string, SessionRecord>());

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void TheFileStoresRoleNamesNotNumbers()
    {
        var path = Path.Combine(_dir, "state.json");
        new StateStore(path).Save(new Dictionary<string, SessionRecord>
        {
            ["planner"] = new(SessionRole.Planner, "id", "planner", DateTimeOffset.UnixEpoch, 0, [], null, null),
        });

        Assert.Contains("\"Planner\"", File.ReadAllText(path));
    }

    [Fact]
    public void TheFileBeforeTheNameKeyLoadsUnderItsNames()
    {
        var path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, LegacyFile);

        var records = new StateStore(path).Load();

        Assert.Equal(2, records.Count);
        Assert.Equal(SessionRole.Planner, records["ovc-planner"].Role);
        Assert.Equal("11111111-aaaa-4bbb-8ccc-000000000001", records["ovc-planner"].SessionId);
        Assert.Equal(SessionRole.Coder, records["ovc-coder"].Role);
        Assert.Equal("done", records["ovc-coder"].LastState);
    }

    [Fact]
    public void TwoCodersWithDifferentNamesLoadAndSaveWithoutAnError()
    {
        var path = Path.Combine(_dir, "state.json");
        var when = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        var store = new StateStore(path);

        store.Save(new Dictionary<string, SessionRecord>
        {
            ["bullshark-coder-ab12-1"] = new(SessionRole.Coder, "s1", "bullshark-coder-ab12-1", when, 0, [], null, null),
            ["bullshark-coder-ab12-2"] = new(SessionRole.Coder, "s2", "bullshark-coder-ab12-2", when, 0, [], null, null),
        });

        var loaded = new StateStore(path).Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("s2", loaded["bullshark-coder-ab12-2"].SessionId);
    }

    [Fact]
    public void TwoRecordsWithOneNameKeepTheLaterStartAndLogTheOther()
    {
        var path = Path.Combine(_dir, "state.json");
        var earlier = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var later = earlier.AddHours(3);
        File.WriteAllText(path, $$"""
            [
              {"Role":"Coder","SessionId":"old-session","Name":"ovc-coder","StartedAt":"{{earlier:O}}","Restarts":0,"RestartTimes":[],"LastState":null,"LastAlert":null},
              {"Role":"Coder","SessionId":"new-session","Name":"ovc-coder","StartedAt":"{{later:O}}","Restarts":0,"RestartTimes":[],"LastState":null,"LastAlert":null}
            ]
            """);
        var logged = new List<string>();

        var loaded = new StateStore(path, logged.Add).Load();

        Assert.Equal("new-session", loaded["ovc-coder"].SessionId);
        var line = Assert.Single(logged);
        Assert.Contains("old-session", line);
        Assert.Contains("new-session", line);
    }
}

using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>F9: a state file whose records have no name is read without a crash; the damaged entries are skipped and logged.</summary>
public sealed class SessionRecordDamageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-state-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _path;
    private readonly List<string> _log = new();

    public SessionRecordDamageTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Good = """{"Role":"Planner","SessionId":"11111111-2222-3333-4444-555555555555","Name":"planner","StartedAt":"2026-11-14T12:00:00+00:00","Restarts":0,"RestartTimes":[]}""";

    [Fact]
    public void ARecordWithANullNameIsSkippedAndTheOthersAreKept()
    {
        File.WriteAllText(_path, "[{\"Role\":\"Planner\",\"SessionId\":\"x\",\"Name\":null,\"StartedAt\":\"2026-11-14T12:00:00+00:00\"}," + Good + "]");

        var records = new StateStore(_path, _log.Add).Load();

        Assert.Equal(["planner"], records.Keys.ToArray());
        Assert.Single(_log);
    }

    [Fact]
    public void AnEmptyNameAndANullEntryAreSkippedToo()
    {
        File.WriteAllText(_path, "[null,{\"Role\":\"Planner\",\"SessionId\":\"y\",\"Name\":\"\",\"StartedAt\":\"2026-11-14T12:00:00+00:00\"}," + Good + "]");

        var records = new StateStore(_path, _log.Add).Load();

        Assert.Equal(["planner"], records.Keys.ToArray());
        Assert.Equal(2, _log.Count);
    }
}

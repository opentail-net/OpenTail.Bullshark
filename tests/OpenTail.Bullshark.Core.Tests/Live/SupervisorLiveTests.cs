using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Live;

/// <summary>
/// Live tests against the real `claude` command. They start small Haiku background sessions and remove them.
/// They run only when OPENTAIL_BULLSHARK_LIVE=1 is set, so the normal test run never spends tokens.
/// Only sessions started by these tests are touched; the owner's sessions are never stopped or removed.
/// </summary>
[Trait("Category", "Live")]
public sealed class SupervisorLiveTests : IDisposable
{
    private static bool LiveEnabled => Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_LIVE") == "1";

    private readonly ClaudeCli _cli = new(new ProcessCliRunner());
    private readonly List<string> _shortIds = new();
    private readonly string _work = Path.Combine(Path.GetTempPath(), "bullshark-live-" + Guid.NewGuid().ToString("N")[..8]);

    public SupervisorLiveTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        foreach (var id in _shortIds)
        {
            try
            {
                _cli.StopAsync(id).GetAwaiter().GetResult();
                WaitForShortId(id, "done", TimeSpan.FromSeconds(30));
                _cli.RemoveAsync(id).GetAwaiter().GetResult();
            }
            catch (Exception) { /* cleanup is best effort; the test already reported what it found */ }
        }
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    private void WaitForShortId(string shortId, string state, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var agent = _cli.ListAgentsAsync().GetAwaiter().GetResult().FirstOrDefault(a => a.Id == shortId);
            if (agent?.EffectiveState == state) return;
            Thread.Sleep(2000);
        }
    }

    /// <summary>The last part of a session's output, with escape codes removed. Used to explain a failed wait.</summary>
    private string TailOf(string shortId)
    {
        var raw = _cli.LogsAsync(shortId).GetAwaiter().GetResult();
        var plain = System.Text.RegularExpressions.Regex.Replace(raw, @"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07|\x1B[@-Z\\-_]", " ");
        var compact = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ");
        return compact.Length <= 800 ? compact : compact[^800..];
    }

    private AgentInfo? FindBySessionId(string sessionId) =>
        _cli.ListAgentsAsync().GetAwaiter().GetResult().FirstOrDefault(a => a.SessionId == sessionId);

    private void WaitForSession(string sessionId, string state, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (FindBySessionId(sessionId)?.EffectiveState == state) return;
            Thread.Sleep(2000);
        }
    }

    [Fact]
    public async Task ListingAgentsWorksAgainstTheRealCli()
    {
        if (!LiveEnabled) return;

        var agents = await _cli.ListAgentsAsync();

        Assert.NotNull(agents);
    }

    [Fact]
    public async Task ALostSessionIsResumedUnderTheSameIdAgainstTheRealCli()
    {
        if (!LiveEnabled) return;

        var store = new StateStore(Path.Combine(_work, "state.json"));
        var name = "bullshark-live-" + Guid.NewGuid().ToString("N")[..6];
        // The batch arrives in the brief itself. No file is used to pass messages between sessions.
        var spec = new SessionSpec(SessionRole.Coder, name, _work, "haiku", "low",
            "Batch 1 - Live test. Reply with exactly the words LIVE OK and nothing else.", "auto");
        var runner = new SupervisorRunner(_cli, store, TimeProvider.System, resolveTimeout: TimeSpan.FromSeconds(60));

        // 1. The first pass starts the session. --bg assigns its id, and the supervisor reads it back.
        var started = await runner.CheckOnceAsync([spec]);
        Assert.IsType<SupervisorAction.Start>(Assert.Single(started));
        var record = store.Load()[name];
        _shortIds.Add(FindBySessionId(record.SessionId)!.Id!);
        WaitForSession(record.SessionId, "done", TimeSpan.FromSeconds(120));
        var firstState = FindBySessionId(record.SessionId)?.EffectiveState;
        Assert.True(firstState == "done", $"first turn ended in '{firstState}'. Output tail: {TailOf(_shortIds[^1])}");

        // 2. The daemon loses it: remove it from the list. The transcript stays on disk.
        await _cli.RemoveAsync(_shortIds[^1]);
        Assert.Null(FindBySessionId(record.SessionId));

        // 3. The next pass resumes it under the same session id.
        var resumed = await runner.CheckOnceAsync([spec]);
        var resume = Assert.IsType<SupervisorAction.Resume>(Assert.Single(resumed));
        Assert.Equal(record.SessionId, resume.SessionId);

        WaitForSession(record.SessionId, "done", TimeSpan.FromSeconds(120));
        Assert.Equal("done", FindBySessionId(record.SessionId)?.EffectiveState);
        _shortIds.Add(FindBySessionId(record.SessionId)!.Id!);
        Assert.Equal(1, store.Load()[name].Restarts);
    }
}

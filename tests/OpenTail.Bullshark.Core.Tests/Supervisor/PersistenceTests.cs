using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// The window and the supervisor share the state, so a pass must not undo the owner's changes, and a fault must not start a
/// second session (review of 2026-11-15, Phase 1).
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private const string SessionId = "11111111-aaaa-4bbb-8ccc-000000000001";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-persist-" + Guid.NewGuid().ToString("N")[..8]);

    public PersistenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string StatePath => Path.Combine(_dir, "state.json");

    private static SessionSpec Spec(string name) =>
        new(SessionRole.Coder, name, @"C:\W", "haiku", "medium", "Work.", "auto", "Continue.");

    private static SessionRecord Record(string name, string sessionId) =>
        new(SessionRole.Coder, sessionId, name, DateTimeOffset.UnixEpoch, 0, [], null, null);

    private static CliResult Backgrounded(string name) => FakeCliRunner.Ok("backgrounded · 0123abcd · " + name);

    [Fact]
    public async Task ARecordForgottenDuringAPassStaysForgotten()
    {
        var store = new StateStore(StatePath);
        store.Save(new Dictionary<string, SessionRecord> { ["coder-a"] = Record("coder-a", SessionId) });

        // Nothing is listed, so the pass resumes the session. While it does, the owner forgets the session.
        var cli = new FakeCliRunner(args => args[0] switch
        {
            "agents" => FakeCliRunner.Ok("[]"),
            _ when args.Contains("--resume") => ForgetDuring(store, "coder-a"),
            _ => FakeCliRunner.Ok(),
        });
        var runner = new SupervisorRunner(new ClaudeCli(cli), store, TimeProvider.System);

        await runner.CheckOnceAsync([Spec("coder-a")]);

        Assert.False(store.Load().ContainsKey("coder-a"));
    }

    private static CliResult ForgetDuring(StateStore store, string name)
    {
        store.Forget(name);
        return Backgrounded(name);
    }

    [Fact]
    public async Task ACorruptStateFileIsLeftAloneAndNothingIsStarted()
    {
        File.WriteAllText(StatePath, "{ not json");
        var cli = new FakeCliRunner(_ => FakeCliRunner.Ok("[]"));
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(StatePath), TimeProvider.System);

        await Assert.ThrowsAsync<StoreUnreadableException>(() => runner.CheckOnceAsync([Spec("coder-a")]));

        Assert.Equal("{ not json", File.ReadAllText(StatePath));
        Assert.DoesNotContain(cli.Calls, call => call.Args.Contains("--bg"));
    }

    [Fact]
    public async Task APassSurvivesAStartThatThrowsAnyException()
    {
        var cli = new FakeCliRunner(args => args[0] == "agents" ? FakeCliRunner.Ok("[]")
            : args.Contains("--bg") ? throw new System.ComponentModel.Win32Exception(2)
            : FakeCliRunner.Ok());
        var runner = new SupervisorRunner(new ClaudeCli(cli), new StateStore(StatePath), TimeProvider.System);

        var actions = await runner.CheckOnceAsync([Spec("coder-a"), Spec("coder-b")]);

        Assert.Equal(2, actions.Count(action => action is SupervisorAction.Alert { Kind: AlertKind.StartFailed }));
    }

    [Fact]
    public async Task AListedNameIsAdoptedNotStartedTwice()
    {
        var agents = "[{\"id\":\"11111111\",\"kind\":\"background\",\"sessionId\":\"" + SessionId +
                     "\",\"name\":\"coder-a\",\"state\":\"working\"}]";
        var cli = new FakeCliRunner(args => args[0] == "agents" ? FakeCliRunner.Ok(agents) : FakeCliRunner.Ok());
        var store = new StateStore(StatePath);
        var runner = new SupervisorRunner(new ClaudeCli(cli), store, TimeProvider.System);

        await runner.CheckOnceAsync([Spec("coder-a")]);

        Assert.DoesNotContain(cli.Calls, call => call.Args.Contains("--bg"));
        Assert.Equal(SessionId, store.Load()["coder-a"].SessionId);
    }

    [Fact]
    public void AFullResumeGetsTheSoloRestartPrompt()
    {
        var run = RunPlan.ResumedRun("bullshark-resume-ab12", @"C:\W", "opus", "high", DateTimeOffset.UnixEpoch);

        var spec = Assert.Single(run.Specs);
        Assert.Equal("bullshark-resume-ab12", spec.Name);
        Assert.Equal(RunPlan.SoloResumePrompt, spec.ResumePrompt);
    }
}

using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// R4: while the launch is refused, a pass drops its starts and resumes, and records nothing for them. A launch that is not refused goes out as before.
/// </summary>
public sealed class LaunchRefusalTests : IDisposable
{
    private const string Launched = "backgrounded · abcdef12 · coder\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-refusal-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SessionSpec Coder(string name) =>
        new(SessionRole.Coder, name, @"C:\Work", "haiku", "medium", "fix the thing", "auto");

    [Fact]
    public async Task ARefusedStartIsDroppedAndNothingIsRecorded()
    {
        var runner = new FakeCliRunner(args => args[0] == "--bg" ? FakeCliRunner.Ok(Launched) : FakeCliRunner.Ok());
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var asked = 0;
        var supervisor = new SupervisorRunner(new ClaudeCli(runner), store, TimeProvider.System, resolveTimeout: TimeSpan.FromSeconds(1))
        {
            LaunchRefusal = _ =>
            {
                asked++;
                return Task.FromResult<string?>("refused for the test");
            },
        };

        var performed = await supervisor.CheckOnceAsync([Coder("coder")]);

        Assert.Empty(performed);
        Assert.Equal(1, asked);
        Assert.DoesNotContain(runner.Calls, c => c.Args[0] == "--bg");
        Assert.False(store.Load().ContainsKey("coder"));
    }

    [Fact]
    public async Task AStartThatIsNotRefusedGoesOut()
    {
        var runner = new FakeCliRunner(args => args[0] == "--bg" ? FakeCliRunner.Ok(Launched) : FakeCliRunner.Ok());
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var supervisor = new SupervisorRunner(new ClaudeCli(runner), store, TimeProvider.System, resolveTimeout: TimeSpan.FromSeconds(1))
        {
            LaunchRefusal = _ => Task.FromResult<string?>(null),
        };

        // The listing never shows the new session here, so the pass records a failed start after the launch; the launch itself went out.
        await supervisor.CheckOnceAsync([Coder("coder")]);

        Assert.Contains(runner.Calls, c => c.Args[0] == "--bg");
    }
}

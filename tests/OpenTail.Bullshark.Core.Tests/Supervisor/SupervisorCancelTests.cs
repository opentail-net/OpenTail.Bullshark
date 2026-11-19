using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// F5: a pass that is cancelled (the window closed while it ran) starts nothing and writes no record. The CLI wrapper honours the token the
/// way the process runner does: a call made after cancellation does not run.
/// </summary>
public sealed class SupervisorCancelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-cancel-" + Guid.NewGuid().ToString("N")[..8]);

    public SupervisorCancelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class TokenCli(ICliRunner inner) : ICliRunner
    {
        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.RunAsync(arguments, workingDirectory, timeout, cancellationToken, standardInput);
        }
    }

    [Fact]
    public async Task APassCancelledBeforeItStartsRunsNothingAndWritesNothing()
    {
        var fake = new FakeCliRunner(args => FakeCliRunner.Ok(args[0] == "agents" ? "[]" : string.Empty));
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var clock = TimeProvider.System;
        var spec = TestSpecs.Planner(@"C:\W", "Read the README and write the first batch.");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var runner = new SupervisorRunner(new ClaudeCli(new TokenCli(fake)), store, clock, resolveTimeout: TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.CheckOnceAsync([spec], cancel.Token));

        Assert.DoesNotContain(fake.Calls, call => call.Args[0] == "--bg");
        Assert.Empty(store.Load());
    }
}

using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// A brief the command line cannot carry is refused before `claude` runs. The other launches go out exactly as before.
/// </summary>
public sealed class LaunchLimitsTests : IDisposable
{
    private const string Launched = "backgrounded · abcdef12 · planner\n";

    private static readonly string[] FixedArguments =
        ["--bg", "--model", "opus", "--effort", "high", "--name", "planner", "--permission-mode", "auto"];

    /// <summary>The joined length of the arguments that are not the brief, as the launch builds them.</summary>
    private static readonly int FixedLength = string.Join(' ', FixedArguments).Length;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-launch-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static BackgroundLaunch Launch(string prompt, string name = "planner") =>
        new(Name: name, WorkingDirectory: @"C:\Work", Model: "opus", Effort: "high", Prompt: prompt, PermissionMode: "auto");

    [Fact]
    public async Task ANormalBriefGoesOutAsOneUnchangedArgument()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Launched));
        var brief = "Read the README first. Then - list the batches, with dashes inside the sentence.";

        await new ClaudeCli(runner).StartBackgroundAsync(Launch(brief));

        Assert.Equal([.. FixedArguments, brief], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task ABriefThatStartsWithADashIsRefusedBeforeAnythingRuns()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Launched));

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() =>
            new ClaudeCli(runner).StartBackgroundAsync(Launch("-p read the file", name: "coder-a")));

        Assert.Empty(runner.Calls);
        Assert.Contains("'coder-a'", error.Message);
        Assert.Contains("starts with '-'", error.Message);
        Assert.Contains("(16 characters)", error.Message);
    }

    [Fact]
    public async Task ALaunchOneCharacterOverTheLimitIsRefusedAndTheMessageSaysHowLong()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Launched));
        // The joined arguments are the fixed ones, a space, then the brief: this brief makes the joined text one over the limit.
        var brief = new string('x', ClaudeCli.MaxLaunchCharacters - FixedLength);

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => new ClaudeCli(runner).StartBackgroundAsync(Launch(brief)));

        Assert.Empty(runner.Calls);
        Assert.Contains("'planner'", error.Message);
        Assert.Contains($"{ClaudeCli.MaxLaunchCharacters + 1:N0} characters", error.Message);
        Assert.Contains($"at most {ClaudeCli.MaxLaunchCharacters:N0}", error.Message);
    }

    [Fact]
    public async Task ALaunchExactlyAtTheLimitIsSent()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Launched));
        // The joined text is the fixed ones, a space, and the brief: one character less than the brief above, so it fits.
        var brief = new string('y', ClaudeCli.MaxLaunchCharacters - FixedLength - 1);

        await new ClaudeCli(runner).StartBackgroundAsync(Launch(brief));

        Assert.Equal(brief, runner.Calls.Single().Args[^1]);
    }

    [Fact]
    public async Task AResumeWithoutABriefIsNotAffected()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(Launched));

        await new ClaudeCli(runner).StartBackgroundAsync(new BackgroundLaunch("coder", @"C:\Work", "haiku", "medium",
            ResumeSessionId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task ARefusedBriefIsRecordedAsAFailedStartWithItsReason()
    {
        // Through the runner: the refusal is recorded as a failed start with no session, and the reason is the alert's detail.
        var runner = new FakeCliRunner(args => args[0] == "--bg" ? FakeCliRunner.Ok(Launched) : FakeCliRunner.Ok());
        var store = new StateStore(Path.Combine(_dir, "state.json"));
        var dashed = new SessionSpec(SessionRole.Coder, "dash-coder", @"C:\Work", "haiku", "medium", "-x brief", "auto");

        var performed = await new SupervisorRunner(new ClaudeCli(runner), store, TimeProvider.System, resolveTimeout: TimeSpan.FromSeconds(1))
            .CheckOnceAsync([dashed]);

        var alert = Assert.IsType<SupervisorAction.Alert>(Assert.Single(performed));
        Assert.Equal(AlertKind.StartFailed, alert.Kind);
        Assert.Contains("starts with '-'", alert.Detail);
        Assert.Equal(string.Empty, store.Load()["dash-coder"].SessionId);
        Assert.DoesNotContain(runner.Calls, c => c.Args[0] == "--bg");
    }
}

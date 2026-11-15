using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

public sealed class AgentInfoAndCliTests
{
    // Shape copied from `claude agents --json` on 2026-11-14 (finding F2). Ids and names are synthetic.
    private const string SampleJson = """
        [
          { "id": "0b6cfaa9", "cwd": "C:\\Git\\X", "kind": "background", "startedAt": 1784881559887,
            "sessionId": "0b6cfaa9-ba37-4d3b-8b79-59642061b70f", "name": "Review", "state": "blocked" },
          { "pid": 15732, "cwd": "C:\\Git", "kind": "interactive", "startedAt": 1794672408677,
            "sessionId": "c71e8ad0-6308-41be-b119-46235935a059", "name": "git-86", "status": "busy" }
        ]
        """;

    [Fact]
    public void ParsesBackgroundAndInteractiveEntries()
    {
        var agents = AgentInfo.ParseList(SampleJson);

        Assert.Equal(2, agents.Count);
        Assert.Equal("0b6cfaa9", agents[0].Id);
        Assert.True(agents[0].IsBackground);
        Assert.Equal("blocked", agents[0].EffectiveState);
        Assert.Null(agents[1].Id);
        Assert.Equal("busy", agents[1].EffectiveState);
        Assert.Equal(15732, agents[1].Pid);
    }

    [Fact]
    public void ToleratesTextBeforeTheJson()
    {
        var agents = AgentInfo.ParseList("Starting background service…\n" + SampleJson);
        Assert.Equal(2, agents.Count);
    }

    [Fact]
    public void EmptyOrMissingOutputGivesNoAgents()
    {
        Assert.Empty(AgentInfo.ParseList(string.Empty));
        Assert.Empty(AgentInfo.ParseList("[]"));
    }

    [Fact]
    public void ParsesTheStartedAtTimestamp()
    {
        var agent = AgentInfo.ParseList(SampleJson)[0];
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1784881559887), agent.StartedAt);
    }

    [Fact]
    public void ReadsBackgroundIdFromLaunchOutput()
    {
        Assert.Equal("4431d697", ClaudeCli.ParseBackgroundId("backgrounded · 4431d697 · bullshark-probe-1\n  claude agents"));
        Assert.Null(ClaudeCli.ParseBackgroundId("something else"));
    }

    [Fact]
    public async Task ListAgentsRunsTheAgentsCommand()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(SampleJson));
        var cli = new ClaudeCli(runner);

        var agents = await cli.ListAgentsAsync();

        Assert.Equal(2, agents.Count);
        Assert.Equal(["agents", "--json"], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task StartBackgroundBuildsTheLaunchArgumentsInOrder()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("backgrounded · abcdef12 · planner\n"));
        var cli = new ClaudeCli(runner);

        var id = await cli.StartBackgroundAsync(new BackgroundLaunch(
            Name: "planner",
            WorkingDirectory: @"C:\Work",
            Model: "opus",
            Effort: "high",
            Prompt: "Read the README first.",
            PermissionMode: "auto"));

        Assert.Equal("abcdef12", id);
        var call = runner.Calls.Single();
        Assert.Equal(
            ["--bg", "--model", "opus", "--effort", "high", "--name", "planner",
             "--permission-mode", "auto", "Read the README first."],
            call.Args);
        Assert.Equal(@"C:\Work", call.WorkingDirectory);
    }

    [Fact]
    public async Task ResolveSessionIdReadsTheFullIdBackFromTheListing()
    {
        var listing = """[{"id":"abcdef12","kind":"background","sessionId":"full-session-id","name":"planner","state":"working"}]""";
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(listing));
        var cli = new ClaudeCli(runner);

        var sessionId = await cli.ResolveSessionIdAsync("abcdef12", TimeSpan.FromSeconds(5));

        Assert.Equal("full-session-id", sessionId);
    }

    [Fact]
    public async Task ResumeLaunchUsesTheResumeFlagAndNoPrompt()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("backgrounded · 9f8e7d6c · coder\n"));
        var cli = new ClaudeCli(runner);

        await cli.StartBackgroundAsync(new BackgroundLaunch("coder", @"C:\Work", "haiku", "medium",
            ResumeSessionId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        var args = runner.Calls.Single().Args;
        Assert.Contains("--resume", args);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", args[Array.IndexOf(args.ToArray(), "--resume") + 1]);
        Assert.DoesNotContain("--session-id", args);
    }

    [Fact]
    public async Task AFailedCommandThrowsWithTheReason()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Fail("connect ENOENT pipe"));
        var cli = new ClaudeCli(runner);

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => cli.ListAgentsAsync());
        Assert.Contains("connect ENOENT pipe", error.Message);
    }

    [Fact]
    public async Task StopAndRemoveUseTheIdAndDoNotThrowOnFailure()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Fail("No job matching 'x'"));
        var cli = new ClaudeCli(runner);

        var stop = await cli.StopAsync("abcdef12");
        var remove = await cli.RemoveAsync("abcdef12");

        Assert.False(stop.Succeeded);
        Assert.False(remove.Succeeded);
        Assert.Equal(["stop", "abcdef12"], runner.Calls[0].Args);
        Assert.Equal(["rm", "abcdef12"], runner.Calls[1].Args);
    }
}

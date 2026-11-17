using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;

namespace OpenTail.Bullshark.Core.Tests.Claude;

public sealed class ListAgentsTests
{
    [Fact]
    public async Task AListingWithNoListIsAFailureNotNoSessions()
    {
        var cli = new ClaudeCli(new FakeCliRunner(_ => FakeCliRunner.Ok("the daemon is starting")));

        await Assert.ThrowsAsync<ClaudeCliException>(() => cli.ListAgentsAsync());
    }

    [Fact]
    public async Task AnEmptyListingIsNoSessions()
    {
        var agents = await new ClaudeCli(new FakeCliRunner(_ => FakeCliRunner.Ok("[]"))).ListAgentsAsync();

        Assert.Empty(agents);
    }

    [Fact]
    public async Task TheDefaultListingTakesNoAllFlagAndTheCompletedOneDoes()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("[]"));
        var cli = new ClaudeCli(runner);

        await cli.ListAgentsAsync();
        await cli.ListAgentsAsync(includeCompleted: false);
        await cli.ListAgentsAsync(includeCompleted: true);

        Assert.Equal(["agents", "--json"], runner.Calls[0].Args);
        Assert.Equal(["agents", "--json"], runner.Calls[1].Args);
        Assert.Equal(["agents", "--json", "--all"], runner.Calls[2].Args);
    }
}

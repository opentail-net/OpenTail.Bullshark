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
}

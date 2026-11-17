using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// The argument lists of the agent listing with an optional folder (additions item 6). The listing with no folder must be the same
/// command it was before: the window relies on it for the full list.
/// </summary>
public sealed class ListAgentsCwdTests
{
    private const string Folder = @"C:\Git\split\some-repo";

    private static (ClaudeCli Cli, FakeCliRunner Runner) Cli()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("[]"));
        return (new ClaudeCli(runner), runner);
    }

    [Fact]
    public async Task TheFullListingIsTheSameCommandAsBefore()
    {
        var (cli, runner) = Cli();

        await cli.ListAgentsAsync();
        await cli.ListAgentsAsync(includeCompleted: true);

        Assert.Equal(["agents", "--json"], runner.Calls[0].Args);
        Assert.Equal(["agents", "--json", "--all"], runner.Calls[1].Args);
    }

    [Fact]
    public async Task AFolderAddsTheCwdOption()
    {
        var (cli, runner) = Cli();

        await cli.ListAgentsAsync(Folder, includeCompleted: false);

        Assert.Equal(["agents", "--json", "--cwd", Folder], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task AFolderWithCompletedSessionsKeepsBothOptions()
    {
        var (cli, runner) = Cli();

        await cli.ListAgentsAsync(Folder, includeCompleted: true);

        Assert.Equal(["agents", "--json", "--all", "--cwd", Folder], runner.Calls.Single().Args);
    }

    [Fact]
    public async Task ANullOrBlankFolderGivesTheFullListing()
    {
        var (cli, runner) = Cli();

        await cli.ListAgentsAsync(null, includeCompleted: false);
        await cli.ListAgentsAsync("   ", includeCompleted: false);

        Assert.Equal(["agents", "--json"], runner.Calls[0].Args);
        Assert.Equal(["agents", "--json"], runner.Calls[1].Args);
    }
}

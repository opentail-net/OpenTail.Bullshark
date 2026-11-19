using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// F4: a session id reaches a command line only as an 8-hex short id, and `claude logs`, `stop` and `rm` never take an id that
/// starts with '-' (it would be read as an option). The runner is a fake: no `claude` process is started.
/// </summary>
public sealed class ShortIdGuardTests
{
    private const string FullId = "0123abcd-1111-2222-3333-444444444444";

    [Fact]
    public void TheShortIdIsTheFirstEightCharacters()
    {
        Assert.Equal("0123abcd", ClaudeCli.ShortIdOf(FullId));
        Assert.Equal("abc", ClaudeCli.ShortIdOf("abc"));
    }

    [Theory]
    [InlineData("0123abcd", true)]
    [InlineData("0123ABCD", true)]
    [InlineData("0123abc", false)]
    [InlineData("0123abcg", false)]
    [InlineData("x;rm -rf", false)]
    [InlineData("0123abc ", false)]
    [InlineData("-123abc", false)]
    [InlineData("", false)]
    public void OnlyEightHexCharactersAreAShortId(string id, bool valid)
    {
        Assert.Equal(valid, ClaudeCli.IsShortId(id));
    }

    [Fact]
    public void NullIsNotAShortId()
    {
        Assert.False(ClaudeCli.IsShortId(null));
    }

    [Fact]
    public void AValidSessionGivesTheAttachLine()
    {
        Assert.Equal("claude attach 0123abcd", ClaudeCli.AttachCommandLineOf(FullId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("x&calc.exe 1")]
    [InlineData("-option-id-")]
    [InlineData("0123 abcd-x")]
    public void AnInvalidSessionIdGivesNoAttachLine(string sessionId)
    {
        Assert.Null(ClaudeCli.AttachCommandLineOf(sessionId));
    }

    [Fact]
    public async Task LogsRefusesAnIdThatStartsWithADashAndRunsNothing()
    {
        var runner = new FakeCliRunner();
        var cli = new ClaudeCli(runner);

        await Assert.ThrowsAsync<ClaudeCliException>(() => cli.LogsAsync("--help"));
        await Assert.ThrowsAsync<ClaudeCliException>(() => cli.LogsAsync(string.Empty));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task StopAndRemoveRefuseAnIdThatStartsWithADashAndRunNothing()
    {
        var runner = new FakeCliRunner();
        var cli = new ClaudeCli(runner);

        var stop = await cli.StopAsync("-x");
        var remove = await cli.RemoveAsync("-x");

        Assert.Equal(-1, stop.ExitCode);
        Assert.Equal(-1, remove.ExitCode);
        Assert.Contains("was not run", stop.StdErr);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task AValidIdIsStillSentToLogsStopAndRm()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("log text"));
        var cli = new ClaudeCli(runner);

        Assert.Equal("log text", await cli.LogsAsync("0123abcd"));
        await cli.StopAsync("0123abcd");
        await cli.RemoveAsync("0123abcd");

        Assert.Equal(["logs", "0123abcd"], runner.Calls[0].Args);
        Assert.Equal(["stop", "0123abcd"], runner.Calls[1].Args);
        Assert.Equal(["rm", "0123abcd"], runner.Calls[2].Args);
    }
}

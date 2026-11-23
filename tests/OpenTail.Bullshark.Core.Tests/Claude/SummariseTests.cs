using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;

namespace OpenTail.Bullshark.Core.Tests.Claude;

public sealed class SummariseTests
{
    [Fact]
    public async Task TheTranscriptGoesInOnStandardInputAndTheSummaryComesBack()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("A short summary."));

        var summary = await new ClaudeCli(runner).SummariseAsync("Summarise.", "you: hello");

        Assert.Equal("A short summary.", summary);
        Assert.Equal("you: hello", runner.Inputs.Single());
        var args = runner.Calls.Single().Args;
        Assert.Equal("-p", args[0]);
        Assert.Equal("haiku", args[Array.IndexOf(args.ToArray(), "--model") + 1]);
        // The summary call must not save a session: it would show in History and in the usage totals.
        Assert.Contains("--no-session-persistence", args);
    }

    [Fact]
    public async Task AFailedSummaryNamesItsExitCodeButNoOutput()
    {
        var cli = new ClaudeCli(new FakeCliRunner(_ => FakeCliRunner.Fail("CANARY-TRANSCRIPT-7731 no credit")));

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => cli.SummariseAsync("Summarise.", "you: hello"));

        Assert.Contains("exit", error.Message);
        Assert.DoesNotContain("CANARY", error.Message);
        Assert.DoesNotContain("no credit", error.Message);
    }

    [Fact]
    public async Task AnEmptySummaryIsAFailure()
    {
        var cli = new ClaudeCli(new FakeCliRunner(_ => FakeCliRunner.Ok("   ")));

        await Assert.ThrowsAsync<ClaudeCliException>(() => cli.SummariseAsync("Summarise.", "you: hello"));
    }
}
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>F8: what `claude` prints can reach app.log and events.jsonl only as one cut line, never a whole brief or transcript.</summary>
public sealed class CliErrorTextTests
{
    [Fact]
    public void OnlyTheFirstLineOfOutputIsKept()
    {
        Assert.Equal("first line", ClaudeCli.FirstLineOf("  first line\r\nsecret second line\n"));
        Assert.Equal(string.Empty, ClaudeCli.FirstLineOf("   \n"));
    }

    [Fact]
    public void ALongFirstLineIsCutAtTwoHundredCharacters()
    {
        var cut = ClaudeCli.FirstLineOf(new string('a', 500));

        Assert.Equal(ClaudeCli.MaxMessageCharacters + 3, cut.Length);
        Assert.EndsWith("...", cut);
    }

    [Fact]
    public async Task AFailedBackgroundStartNamesOnlyTheFirstLineOfWhatClaudePrinted()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("no id here: brief-secret-words\nthe rest of the brief, which is task text\n"));
        var cli = new ClaudeCli(runner);

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() =>
            cli.StartBackgroundAsync(new BackgroundLaunch("planner", @"C:\W", "opus", "high", "Do the thing.")));

        Assert.Contains("brief-secret-words", error.Message);
        Assert.DoesNotContain("the rest of the brief", error.Message);
    }
}

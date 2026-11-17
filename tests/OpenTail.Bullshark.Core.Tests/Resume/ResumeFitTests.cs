using System.Text;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>The start text of a resumed session is kept within the command line it is launched with (item 6 of the round-3 plan).</summary>
public sealed class ResumeFitTests
{
    private const string SourceId = "11111111-2222-3333-4444-555555555555";

    private static TranscriptLine Line(string kind, string text) => new(0, kind, null, text, false);

    /// <summary>Each message is a "you" line followed by a reply, "reply 0", "reply 1", and so on.</summary>
    private static List<TranscriptLine> Conversation(params string[] messages)
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < messages.Length; i++)
        {
            lines.Add(Line("you", messages[i]));
            lines.Add(Line("claude", "reply " + i));
        }
        return lines;
    }

    private static SetupRequest SoloRequest() =>
        new("solo", @"C:\Work\repo", "task", 1, "opus", "high", "haiku", "medium");

    [Fact]
    public void TextThatFitsIsNotCut()
    {
        Assert.Equal("short", ResumeText.CutMiddle("short", 100));
    }

    [Fact]
    public void ACutKeepsTheStartAndTheEndAndTheCountOfWhatWentOut()
    {
        var text = new string('a', 500) + new string('b', 500);

        var cut = ResumeText.CutMiddle(text, 200);

        Assert.True(cut.Length <= 200, $"cut is {cut.Length} characters");
        Assert.StartsWith("aaa", cut);
        Assert.EndsWith("bbb", cut);
        var counted = Regex.Match(cut, @"\[cut ([\d,]+) characters\]");
        Assert.True(counted.Success);
        var removed = int.Parse(counted.Groups[1].Value.Replace(",", ""));
        Assert.Equal(text.Length, removed + (cut.Length - ResumeText.CutMarker(removed).Length));
    }

    [Fact]
    public void ACutDoesNotSplitASurrogatePair()
    {
        // The emoji's high surrogate sits just before the cut point, where the head would otherwise end.
        var text = new string('x', 61) + "😀" + new string('y', 600);

        var cut = ResumeText.CutMiddle(text, 150);

        Assert.True(cut.Length <= 150, $"cut is {cut.Length} characters");
        Assert.Equal(cut, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(cut)));
    }

    [Fact]
    public void ATooSmallLimitGivesNothingRatherThanAnOverlongText()
    {
        Assert.Equal(string.Empty, ResumeText.CutMiddle(new string('z', 1000), 10));
    }

    [Fact]
    public void AHugeLatestTurnIsCutUnderTheCharacterCapAndKeepsItsEnd()
    {
        var huge = new string('y', 40_000);

        var tail = ResumeText.Tail(Conversation("older", huge), turns: 2, maxChars: 1000);

        Assert.True(tail.Length <= 1000, $"tail is {tail.Length} characters");
        Assert.Contains("[cut", tail);
        Assert.DoesNotContain("older", tail);
        Assert.EndsWith("claude: reply 1", tail);
    }

    [Fact]
    public void ANormalLatestTurnIsUnchanged()
    {
        var lines = Conversation("first", "second");

        Assert.Equal(ResumeText.Tail(lines, turns: 2), ResumeText.Tail(lines, turns: 2, maxChars: 100_000));
    }

    [Fact]
    public void AHugeLastTurnIsCutSoTheStartFitsTheLaunchLimit()
    {
        var lines = Conversation("first", new string('w', 40_000));

        var fitted = ResumeText.Fit(null, null, lines, 10, SourceId, text => text.Length + 300);

        Assert.True(fitted.Fits);
        Assert.True(fitted.Text.Length + 300 <= ClaudeCli.MaxLaunchCharacters);
        Assert.True(fitted.CutCharacters > 0);
        Assert.Contains("[cut", fitted.Text);
        Assert.StartsWith($"Continues session: {SourceId}", fitted.Text);
    }

    [Fact]
    public void ANormalStartTextIsUnchangedAndSaysNothingWasCut()
    {
        var lines = Conversation("first", "second");

        var fitted = ResumeText.Fit(null, null, lines, 10, SourceId, text => text.Length + 300);

        Assert.True(fitted.Fits);
        Assert.Equal(0, fitted.CutCharacters);
        Assert.Equal(ResumeText.Compose(null, null, ResumeText.Tail(lines, 10), 10, SourceId), fitted.Text);
    }

    [Fact]
    public void AStartTextThatStaysOverTheLimitIsReportedAsNotFitting()
    {
        var lines = Conversation("first", "second");

        var fitted = ResumeText.Fit(null, null, lines, 10, SourceId, _ => ClaudeCli.MaxLaunchCharacters + 1);

        Assert.False(fitted.Fits);
    }

    [Fact]
    public void TheRealLaunchOfASoloRunStaysUnderTheLimitAfterTheCut()
    {
        // The check uses the command line the supervisor's launch makes, through the same run specs as the run.
        var request = SoloRequest();
        var lines = Conversation("first", new string('q', 45_000));
        Assert.True(SetupPlan.LongestCommandLine(request with { Task = ResumeText.Compose(null, null, ResumeText.Tail(lines, 10), 10, SourceId) })
                    > ClaudeCli.MaxLaunchCharacters, "the test needs a start text that is too long to begin with");

        var fitted = ResumeText.Fit(null, null, lines, 10, SourceId, text => SetupPlan.LongestCommandLine(request with { Task = text }));

        Assert.True(fitted.Fits);
        Assert.True(SetupPlan.LongestCommandLine(request with { Task = fitted.Text }) <= ClaudeCli.MaxLaunchCharacters);
    }

    [Fact]
    public void TheCommandLineLengthIsWhatTheLaunchCountsAgainstTheLimit()
    {
        var launch = new BackgroundLaunch("bullshark-planner-abcd", @"C:\Work", "opus", "high", Prompt: "hello", PermissionMode: "auto");

        Assert.Equal("--bg --model opus --effort high --name bullshark-planner-abcd --permission-mode auto hello".Length,
            ClaudeCli.CommandLineLength(launch));
    }
}

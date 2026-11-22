using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>Stage G1.4 (S-D3): the summary input, built from synthetic messages. Pure.</summary>
public sealed class CopilotSummaryInputTests
{
    private static TranscriptLine Message(int number, string kind, string text) => new(number, kind, null, text, Collapsed: false);

    [Fact]
    public void AShortSessionIsTheMessagesAsKindAndTextWithABlankLineBetween()
    {
        var input = CopilotSummaryInput.Build([Message(1, "you", "Fix it"), Message(2, "copilot", "Done")]);

        Assert.Equal("you: Fix it\n\ncopilot: Done", input);
    }

    [Fact]
    public void WhenOverTheCapTheOldestMessagesGoAndTheNoticeIsAddedAndTheResultFitsTheCap()
    {
        var messages = new List<TranscriptLine>
        {
            Message(1, "you", new string('a', 400)),
            Message(2, "copilot", new string('b', 400)),
            Message(3, "you", "NEWEST"),
        };

        var input = CopilotSummaryInput.Build(messages, maxChars: 500);

        Assert.True(input.Length <= 500, $"{input.Length} characters for a cap of 500");
        Assert.StartsWith(CopilotSummaryInput.CutNotice + "\n\n", input);
        Assert.EndsWith("you: NEWEST", input);
        Assert.DoesNotContain("aaaa", input);
    }

    [Fact]
    public void TheNewestMessageAloneOverTheCapIsKeptCutInItsMiddle()
    {
        var newest = "start-" + new string('n', 1000) + "-end";

        var input = CopilotSummaryInput.Build([Message(1, "you", "old"), Message(2, "you", newest)], maxChars: 300);

        Assert.True(input.Length <= 300, $"{input.Length} characters for a cap of 300");
        Assert.StartsWith(CopilotSummaryInput.CutNotice, input);
        Assert.Contains("you: start-", input);
        Assert.EndsWith("-end", input);
        Assert.Contains(" [cut ", input);
    }

    [Fact]
    public void ACapTooSmallForTheNoticeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CopilotSummaryInput.Build([Message(1, "you", "x")], maxChars: 10));
    }
}

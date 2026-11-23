using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>
/// The split of a session's turns between the summary and the tail (RS-D20, RS-D23): every turn is in exactly one part, in order. A turn the token cap
/// drops is in the summary part. Pure: the lines are synthetic, and every message has its own text, so a turn that is lost or repeated shows.
/// </summary>
public sealed class ResumeSplitTests
{
    private const string NL = "\r\n";

    private static TranscriptLine Line(string kind, string text) => new(0, kind, null, text, Collapsed: false);

    /// <summary>Each turn is an owner's message and a reply. Each message starts with its turn number, so a lost or repeated turn shows.</summary>
    private static List<TranscriptLine> Session(int turnCount, int messageLength = 40)
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < turnCount; i++)
        {
            lines.Add(Line("you", $"turn {i} " + new string('q', messageLength)));
            lines.Add(Line("claude", $"turn {i} reply " + new string('r', messageLength)));
        }
        return lines;
    }

    /// <summary>The same shape for a provider's session: the owner's messages and the provider's replies, which the provider's tail labels.</summary>
    private static List<TranscriptLine> ProviderSession(int turnCount, int messageLength = 40)
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < turnCount; i++)
        {
            lines.Add(Line("you", $"turn {i} " + new string('q', messageLength)));
            lines.Add(Line("codex", $"turn {i} reply " + new string('r', messageLength)));
        }
        return lines;
    }

    /// <summary>The tokens of one turn as the Claude tail renders it. The test lines are only owner and Claude lines, so this is the tail's own count.</summary>
    private static int ClaudeTokens(IEnumerable<TranscriptLine> turn) =>
        ResumeText.EstimateTokens(string.Join("\n", turn.Select(l => (l.Kind == "you" ? "you: " : "claude: ") + l.Text)));

    private static int ProviderTokens(IEnumerable<TranscriptLine> turn) =>
        ResumeText.EstimateTokens(ProviderResumeText.Render(turn, "codex"));

    private static IEnumerable<string> TextsOf(IEnumerable<TranscriptLine> lines) => lines.Select(l => l.Text);

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(1, int.MaxValue)]
    [InlineData(3, int.MaxValue)]
    [InlineData(6, int.MaxValue)]
    [InlineData(10, int.MaxValue)]
    [InlineData(2, 60)]
    [InlineData(3, 100)]
    [InlineData(6, 120)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(0, 1)]
    public void EveryTurnIsInExactlyOnePartAndTheTailIsWhatTailWrites(int turns, int maxTokens)
    {
        var lines = Session(6);
        var all = ResumeText.SplitTurns(lines);

        var left = ResumeText.TurnsLeftOut(lines, turns, maxTokens, ClaudeTokens);
        var summaryPart = all.Take(left).SelectMany(t => t).ToList();
        var tailPart = all.Skip(left).SelectMany(t => t).ToList();

        Assert.Equal(TextsOf(lines), TextsOf(summaryPart).Concat(TextsOf(tailPart)));
        Assert.Equal(all.Count - left, ResumeText.TailWithTurns(lines, turns, maxTokens).Turns);
        Assert.Equal(ResumeText.Tail(tailPart, int.MaxValue, int.MaxValue), ResumeText.Tail(lines, turns, maxTokens));
    }

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(2, int.MaxValue)]
    [InlineData(6, 90)]
    [InlineData(3, 60)]
    [InlineData(6, 1)]
    public void TheProviderSplitCoversEveryTurnAndItsTailIsWhatTheProviderTailWrites(int turns, int maxTokens)
    {
        var lines = ProviderSession(6);
        var all = ResumeText.SplitTurns(lines);

        var left = ResumeText.TurnsLeftOut(lines, turns, maxTokens, ProviderTokens);
        var summaryPart = all.Take(left).SelectMany(t => t).ToList();
        var tailPart = all.Skip(left).SelectMany(t => t).ToList();

        Assert.Equal(TextsOf(lines), TextsOf(summaryPart).Concat(TextsOf(tailPart)));
        Assert.Equal(ProviderResumeText.Tail(tailPart, "codex", int.MaxValue, int.MaxValue), ProviderResumeText.Tail(lines, "codex", turns, maxTokens));
        Assert.Equal(all.Count - left, ProviderResumeText.TailWithTurns(lines, "codex", turns, maxTokens).Turns);
    }

    [Fact]
    public void ATurnTheTokenCapDropsLandsInTheSummaryPart()
    {
        var lines = Session(6);

        // Each turn is about 29 tokens, so a cap of 100 keeps three turns: the three oldest are dropped from the tail.
        var left = ResumeText.TurnsLeftOut(lines, 6, 100, ClaudeTokens);
        Assert.Equal(3, left);

        var summaryTexts = ResumeText.SplitTurns(lines).Take(left).SelectMany(t => t).Select(l => l.Text).ToList();
        Assert.Contains(summaryTexts, t => t.StartsWith("turn 0 ", StringComparison.Ordinal));
        Assert.Contains(summaryTexts, t => t.StartsWith("turn 2 reply", StringComparison.Ordinal));

        var tail = ResumeText.Tail(lines, 6, 100);
        Assert.Contains("turn 3 ", tail);
        Assert.DoesNotContain("turn 2 ", tail);
    }

    [Fact]
    public void NoTurnsKeptLeavesEveryTurnInTheSummaryPart()
    {
        var lines = Session(4);

        Assert.Equal(4, ResumeText.TurnsLeftOut(lines, 0, int.MaxValue, ClaudeTokens));
        Assert.Equal(4, ResumeText.TurnsLeftOut(lines, -1, int.MaxValue, ClaudeTokens));
        Assert.Equal(string.Empty, ResumeText.Tail(lines, 0));
        Assert.Equal(0, ResumeText.TailWithTurns(lines, 0).Turns);
    }

    [Fact]
    public void TurnsAtOrAboveTheCountLeaveNoneInTheSummaryPart()
    {
        var lines = Session(4);

        Assert.Equal(0, ResumeText.TurnsLeftOut(lines, 4, int.MaxValue, ClaudeTokens));
        Assert.Equal(0, ResumeText.TurnsLeftOut(lines, 50, int.MaxValue, ClaudeTokens));
        Assert.Equal(4, ResumeText.TailWithTurns(lines, 50).Turns);
    }

    [Fact]
    public void AnEmptySessionHasNoTurnsInEitherPart()
    {
        var lines = new List<TranscriptLine>();

        Assert.Equal(0, ResumeText.TurnsLeftOut(lines, 10, 100, ClaudeTokens));
        Assert.Equal(string.Empty, ResumeText.Tail(lines, 10));
    }

    [Fact]
    public void TheLatestTurnIsNeverLeftOutByTheCap()
    {
        var lines = Session(3, messageLength: 2000);

        // Even a cap far below one turn keeps the latest turn, as Tail does (the latest turn is cut only by the character cap).
        var left = ResumeText.TurnsLeftOut(lines, 3, 1, ClaudeTokens);
        Assert.Equal(2, left);
        Assert.Equal(1, ResumeText.TailWithTurns(lines, 3, 1).Turns);
    }
}

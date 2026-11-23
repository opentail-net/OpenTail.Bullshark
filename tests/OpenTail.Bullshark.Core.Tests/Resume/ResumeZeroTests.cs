using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>
/// The Zero level (RS-D25): no summary and no model call, so the start is a header and the tail. Also the line a command-line fit writes when it leaves out
/// whole turns (RS-D24). Pure: the lines are synthetic.
/// </summary>
public sealed class ResumeZeroTests
{
    private const string SourceId = "11111111-2222-3333-4444-555555555555";

    private const string Closing = "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    private const string ClaudeHeader =
        "This session continues an earlier Claude Code session that had gone cold. What follows is not the full conversation.";

    private const string ProviderHeader =
        "This session continues an earlier Codex CLI session that had gone cold. What follows is not the full conversation.";

    private static TranscriptLine Line(string kind, string text) => new(0, kind, null, text, Collapsed: false);

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

    /// <summary>The command line of these tests counts each platform newline as one character.</summary>
    private static int CommandLineOf(string text) => text.Replace(Environment.NewLine, "\n").Length;

    /// <summary>The text from the tail heading on, which every start text shares for the tail block and the closing line.</summary>
    private static string FromTheTail(string text) => text[text.IndexOf("## The last", StringComparison.Ordinal)..];

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact]
    public void ZeroHasTheTwoHeaderLinesTheTailAndTheClosingLineAndNothingElse()
    {
        const string tail = "you: hi\nclaude: reply 0";

        var text = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", null, null, tail, 3);

        Assert.Equal(
            Lines(
                "Turns from a Claude Code session",
                "This new Codex CLI session starts from the last 3 turns of that session. The Claude Code session itself is not continued.",
                string.Empty,
                "## The last 3 turns, word for word (messages only)",
                tail,
                string.Empty,
                Closing),
            text);
    }

    [Fact]
    public void ZeroHasTheSameTailBlockAndClosingLineAsASummaryStart()
    {
        const string tail = "you: hi\nclaude: reply 0";

        var summary = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", "A summary.", SummaryLevel.Normal, tail, 3);
        var zero = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", null, null, tail, 3);

        Assert.Equal(FromTheTail(summary), FromTheTail(zero));
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("you: hi", 0)]
    public void ZeroNeedsATailAndTurns(string tail, int turns) =>
        Assert.Throws<ArgumentException>(() => ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", null, null, tail, turns));

    [Fact]
    public void TheSamePlatformZeroHasTheHeaderTheTailAndTheClosingLineOnly()
    {
        const string tail = "you: hi\nclaude: reply 0";

        var text = ResumeText.Compose(null, null, tail, 3);

        Assert.Equal(
            Lines(
                ClaudeHeader,
                string.Empty,
                "## The last 3 turns, word for word (tool calls and results on one line each)",
                tail,
                string.Empty,
                Closing),
            text);
    }

    [Fact]
    public void TheSamePlatformZeroWithASessionNamesItFirst()
    {
        const string tail = "you: hi";

        var text = ResumeText.Compose(null, null, tail, 1, SourceId);

        Assert.Equal($"Continues session: {SourceId}", text.Split(Environment.NewLine)[0]);
    }

    [Fact]
    public void TheProviderZeroWithoutTurnsHasOnlyTheHeaderAndTheClosingLine()
    {
        var text = ProviderResumeText.Compose("Codex CLI", null, null, string.Empty, 3);

        Assert.Equal(Lines(ProviderHeader, string.Empty, Closing), text);
    }

    [Fact]
    public void AFitThatLeavesOutWholeTurnsSaysHowManyAtTheTopOfTheTail()
    {
        var lines = Conversation("first question", "second question", new string('w', 600));
        var limit = CommandLineOf(ResumeText.Compose(null, null, string.Empty, 3, SourceId)) + 250;

        var fitted = ResumeText.Fit(null, null, lines, 3, SourceId, CommandLineOf, limit);

        Assert.True(fitted.Fits);
        Assert.True(CommandLineOf(fitted.Text) <= limit, $"{CommandLineOf(fitted.Text)} characters for a limit of {limit}");
        var textLines = fitted.Text.Replace(Environment.NewLine, "\n").Split('\n');
        var heading = Array.FindIndex(textLines, l => l.StartsWith("## The last 3 turns", StringComparison.Ordinal));
        Assert.True(heading >= 0, "the tail block has its heading");
        Assert.Equal("(2 earlier turns are left out of this text to fit the command line.)", textLines[heading + 1]);
        Assert.Contains("you: www", fitted.Text);
    }

    [Fact]
    public void AFitThatLeavesOutNoTurnHasNoSuchLine()
    {
        var lines = Conversation("first question", "second question");

        var fitted = ResumeText.Fit(null, null, lines, 2, SourceId, CommandLineOf, 100_000);

        Assert.DoesNotContain("earlier turns are left out", fitted.Text);
        Assert.Equal(ResumeText.Compose(null, null, ResumeText.Tail(lines, 2), 2, SourceId), fitted.Text);
    }

    [Fact]
    public void ACutOfTheLatestTurnAloneLeavesNoTurnOutAndSaysNothingOfTurns()
    {
        var lines = Conversation(new string('w', 1000));
        var limit = CommandLineOf(ResumeText.Compose(null, null, string.Empty, 1, SourceId)) + 300;

        var fitted = ResumeText.Fit(null, null, lines, 1, SourceId, CommandLineOf, limit);

        Assert.True(fitted.CutCharacters > 0, "the test needs the latest turn to be cut");
        Assert.Contains("[cut ", fitted.Text);
        Assert.DoesNotContain("earlier turns are left out", fitted.Text);
    }

    [Fact]
    public void ALatestTurnCutToNothingCountsAsNoTurnKept()
    {
        // Five characters are too few for the cut marker, so the latest turn keeps no text, and no turn is kept.
        var lines = Conversation(new string('w', 600));

        Assert.Equal((string.Empty, 0), ResumeText.TailWithTurns(lines, 1, maxChars: 5));
        Assert.Equal((string.Empty, 0), ProviderResumeText.TailWithTurns(lines, "claude", 1, maxChars: 5));
    }

    [Fact]
    public void AZeroStartWhoseWholeTailIsLeftOutStillHasItsTailLine()
    {
        var lines = Conversation(new string('w', 600));

        // The limit is too small for the header, so the whole tail is left out. The start still has its tail line, so a Zero start does not throw.
        var fitted = ResumeText.FitWith(
            null, null, lines, 1, SourceId, CommandLineOf, 50,
            (turnLines, keep, maxChars) => ProviderResumeText.TailWithTurns(turnLines, "claude", keep, maxChars: maxChars),
            (summary, level, tail, turns, _) => ProviderResumeText.ComposeFrom("Claude Code", "Codex CLI", summary, level, tail, turns));

        Assert.False(fitted.Fits);
        Assert.Contains("(1 earlier turn is left out of this text to fit the command line.)", fitted.Text);
    }
}

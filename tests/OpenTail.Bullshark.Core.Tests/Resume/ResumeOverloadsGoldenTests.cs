using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>
/// Golden output of the start text functions that the S3 overloads sit beside: <c>ResumeText.Fit</c>, <c>ResumeText.Compose</c> and
/// <c>ProviderResumeText.Compose</c>, for fixed inputs. The expected strings were captured from the code before the S3 overloads were added,
/// and the same tests must pass after them without edits. Pure: the lines are synthetic.
/// </summary>
public sealed class ResumeOverloadsGoldenTests
{
    private const string SourceId = "11111111-2222-3333-4444-555555555555";

    private const string ClaudeHeader =
        "This session continues an earlier Claude Code session that had gone cold. What follows is not the full conversation.";

    private const string CodexHeader =
        "This session continues an earlier Codex CLI session that had gone cold. What follows is not the full conversation.";

    private const string CopilotHeader =
        "This session continues an earlier GitHub Copilot CLI session that had gone cold. What follows is not the full conversation.";

    private const string Closing = "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    private const string Summary = "Goal: finish the stage.";

    private const string TwoTurns = "you: first question\nclaude: reply 0\n\nyou: second question\nclaude: reply 1";

    /// <summary>
    /// The lines as AppendLine writes them: each one ends with the platform newline. A piece that holds "\n" (the tail) keeps it, as the tail does.
    /// </summary>
    private static string Block(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;

    /// <summary>
    /// The command line of these tests counts each platform newline as one character, so a cut comes out the same on every platform.
    /// </summary>
    private static int CommandLineOf(string text) => text.Replace(Environment.NewLine, "\n").Length;

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

    [Fact]
    public void FitWithAMinimalSummaryIsCaptured()
    {
        var fitted = ResumeText.Fit(Summary, SummaryLevel.Minimal, Conversation("first question", "second question"), 2, SourceId, CommandLineOf, 10_000);

        Assert.Equal(new ResumeText.FittedStart(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## Summary (minimal)",
                Summary,
                "",
                "## The last 2 turns, word for word (tool calls and results on one line each)",
                TwoTurns,
                "") + Closing,
            0,
            true), fitted);
    }

    [Fact]
    public void FitWithANormalSummaryIsTrimmedAndKeepsItsLines()
    {
        var fitted = ResumeText.Fit("  Goal: finish the stage.\nOpen: the tests.  \n", SummaryLevel.Normal,
            Conversation("first question", "second question"), 2, SourceId, CommandLineOf, 10_000);

        Assert.Equal(new ResumeText.FittedStart(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## Summary (normal)",
                "Goal: finish the stage.\nOpen: the tests.",
                "",
                "## The last 2 turns, word for word (tool calls and results on one line each)",
                TwoTurns,
                "") + Closing,
            0,
            true), fitted);
    }

    [Fact]
    public void FitWithACompleteSummaryIsCaptured()
    {
        var fitted = ResumeText.Fit(Summary, SummaryLevel.Complete, Conversation("first question", "second question"), 2, SourceId, CommandLineOf, 10_000);

        Assert.Equal(new ResumeText.FittedStart(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## Summary (complete)",
                Summary,
                "",
                "## The last 2 turns, word for word (tool calls and results on one line each)",
                TwoTurns,
                "") + Closing,
            0,
            true), fitted);
    }

    [Fact]
    public void FitWithoutASummaryIsCaptured()
    {
        var fitted = ResumeText.Fit(null, null, Conversation("first question", "second question"), 2, SourceId, CommandLineOf, 10_000);

        Assert.Equal(new ResumeText.FittedStart(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## The last 2 turns, word for word (tool calls and results on one line each)",
                TwoTurns,
                "") + Closing,
            0,
            true), fitted);
    }

    [Fact]
    public void FitCutsALongLatestTurnAndCaptionsTheCut()
    {
        var longMessage = string.Concat(Enumerable.Repeat("0123456789", 100));

        var fitted = ResumeText.Fit(Summary, SummaryLevel.Normal, Conversation("first question", longMessage), 2, SourceId, CommandLineOf, 700);

        Assert.Equal(new ResumeText.FittedStart(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## Summary (normal)",
                Summary,
                "",
                "## The last 2 turns, word for word (tool calls and results on one line each)",
                "(1 earlier turn is left out of this text to fit the command line.)\nyou: 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789 … [cut 812 characters] … 2345678901234567890123456789012345678901234567890123456789012345678901234567890123456789\nclaude: reply 1",
                "") + Closing,
            823,
            true), fitted);
    }

    [Fact]
    public void ComposeWithEachLevelAndASource()
    {
        Assert.Equal(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## Summary (minimal)",
                Summary,
                "",
                "## The last 1 turns, word for word (tool calls and results on one line each)",
                "you: hi\nclaude: reply 0",
                "") + Closing,
            ResumeText.Compose(Summary, SummaryLevel.Minimal, "you: hi\nclaude: reply 0", 1, SourceId));

        Assert.Equal(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## Summary (complete)",
                Summary,
                "",
                "## The last 1 turns, word for word (tool calls and results on one line each)",
                "you: hi",
                "") + Closing,
            ResumeText.Compose(Summary, SummaryLevel.Complete, "you: hi", 1, SourceId));
    }

    [Fact]
    public void ComposeWithANullSessionIdLeavesTheSourceLineOut()
    {
        Assert.Equal(
            Block(
                ClaudeHeader,
                "",
                "## Summary (normal)",
                Summary,
                "",
                "## The last 1 turns, word for word (tool calls and results on one line each)",
                "you: hi",
                "") + Closing,
            ResumeText.Compose(Summary, SummaryLevel.Normal, "you: hi", 1, null));
    }

    [Fact]
    public void ComposeWithATailOnlyIsCaptured()
    {
        Assert.Equal(
            Block(
                "Continues session: " + SourceId,
                ClaudeHeader,
                "",
                "## The last 1 turns, word for word (tool calls and results on one line each)",
                "you: hi",
                "") + Closing,
            ResumeText.Compose(null, null, "you: hi", 1, SourceId));
    }

    [Fact]
    public void ComposeWithNothingIsCaptured()
    {
        Assert.Equal(Block(ClaudeHeader, "") + Closing, ResumeText.Compose(null, null, string.Empty, 0));
    }

    [Fact]
    public void ProviderComposeWithTheCodexPhraseAndASource()
    {
        Assert.Equal(
            Block(
                "Continues session: " + SourceId,
                CodexHeader,
                "",
                "## Summary (normal)",
                Summary,
                "",
                "## The last 1 turns, word for word (messages only)",
                "you: hi\ncodex: reply 0",
                "") + Closing,
            ProviderResumeText.Compose("Codex CLI", Summary, SummaryLevel.Normal, "you: hi\ncodex: reply 0", 1, SourceId));
    }

    [Fact]
    public void ProviderComposeWithTheCopilotPhraseAndNoSummary()
    {
        Assert.Equal(
            Block(
                CopilotHeader,
                "",
                "## The last 1 turns, word for word (messages only)",
                "you: hi\ncopilot: reply 0",
                "") + Closing,
            ProviderResumeText.Compose("GitHub Copilot CLI", null, null, "you: hi\ncopilot: reply 0", 1));
    }

    [Fact]
    public void ProviderComposeWithASummaryAndNoTail()
    {
        Assert.Equal(
            Block(
                "Continues session: " + SourceId,
                CodexHeader,
                "",
                "## Summary (complete)",
                Summary,
                "") + Closing,
            ProviderResumeText.Compose("Codex CLI", Summary, SummaryLevel.Complete, string.Empty, 0, SourceId));

        Assert.Equal(
            Block(
                CopilotHeader,
                "",
                "## Summary (minimal)",
                Summary,
                "") + Closing,
            ProviderResumeText.Compose("GitHub Copilot CLI", Summary, SummaryLevel.Minimal, string.Empty, 0));
    }
}

using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Resume;

/// <summary>
/// The S3 overloads: <c>ResumeText.FitWith</c>, which fits a start text with its tail and compose steps as parameters, and
/// <c>ProviderResumeText.ComposeFrom</c>, the start text of a session in another platform than the one it summarises (RS-D6).
/// Pure: the lines are synthetic.
/// </summary>
public sealed class ResumeOverloadTests
{
    private const string SourceId = "11111111-2222-3333-4444-555555555555";

    private const string NewSession =
        "This new Codex CLI session starts from that summary. The Claude Code session itself is not continued.";

    private const string Closing = "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    /// <summary>The command line of these tests counts each platform newline as one character.</summary>
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

    private static string[] Lines(string text) => text.Split(Environment.NewLine);

    /// <summary>The text from the tail heading on, which is the part that <c>Compose</c> and <c>ComposeFrom</c> share.</summary>
    private static string FromTheTail(string text) => text[text.IndexOf("## The last", StringComparison.Ordinal)..];

    private static ResumeText.FittedStart FitWithTheExistingDelegates(string? summary, SummaryLevel? level, IReadOnlyList<TranscriptLine> lines, int limit) =>
        ResumeText.FitWith(summary, level, lines, 2, SourceId, CommandLineOf, limit,
            (turnLines, keep, maxChars) => ResumeText.TailWithTurns(turnLines, keep, maxChars: maxChars), ResumeText.Compose);

    [Fact]
    public void FitWithTheExistingDelegatesFitsWhatFitFits()
    {
        var lines = Conversation("first question", "second question");

        var fit = ResumeText.Fit("Goal: finish the stage.", SummaryLevel.Normal, lines, 2, SourceId, CommandLineOf, 10_000);

        Assert.True(fit.Fits);
        Assert.Equal(fit, FitWithTheExistingDelegates("Goal: finish the stage.", SummaryLevel.Normal, lines, 10_000));
    }

    [Fact]
    public void FitWithTheExistingDelegatesCutsWhatFitCuts()
    {
        var lines = Conversation("first question", new string('w', 1000));

        var fit = ResumeText.Fit(null, null, lines, 2, SourceId, CommandLineOf, 700);

        Assert.True(fit.CutCharacters > 0, "the test needs a tail that is cut");
        Assert.Equal(fit, FitWithTheExistingDelegates(null, null, lines, 700));
    }

    [Fact]
    public void FitWithTheExistingDelegatesReportsWhatFitReports()
    {
        var lines = Conversation("first question", "second question");

        var fit = ResumeText.Fit(null, null, lines, 2, SourceId, CommandLineOf, 10);

        Assert.False(fit.Fits, "the test needs a text that stays over the limit");
        Assert.Equal(fit, FitWithTheExistingDelegates(null, null, lines, 10));
    }

    [Fact]
    public void FitWithUsesTheTailAndTheComposeItIsGiven()
    {
        var fitted = ResumeText.FitWith(null, null, Conversation("first"), 1, SourceId, CommandLineOf, 1000,
            (_, _, _) => ("fake tail", 1),
            (_, _, tail, _, _) => "composed: " + tail);

        Assert.Equal(new ResumeText.FittedStart("composed: fake tail", 0, true), fitted);
    }

    [Fact]
    public void AComposeThatMakesTheTextLongerTriggersTheCut()
    {
        var pad = new string('#', 500);
        string Padded(string? summary, SummaryLevel? level, string tail, int turns, string? sessionId) =>
            ResumeText.Compose(summary, level, tail, turns, sessionId) + pad;
        var lines = Conversation("first question", new string('w', 300));
        var limit = CommandLineOf(Padded(null, null, string.Empty, 2, SourceId)) + 250;
        var uncut = Padded(null, null, ResumeText.Tail(lines, 2), 2, SourceId);
        Assert.True(CommandLineOf(uncut) > limit, "the test needs a text over the limit before the cut");

        var fitted = ResumeText.FitWith(null, null, lines, 2, SourceId, CommandLineOf, limit,
            (turnLines, keep, maxChars) => ResumeText.TailWithTurns(turnLines, keep, maxChars: maxChars), Padded);

        Assert.True(fitted.Fits);
        Assert.True(fitted.CutCharacters > 0);
        Assert.True(CommandLineOf(fitted.Text) <= limit, $"{CommandLineOf(fitted.Text)} characters for a limit of {limit}");
        Assert.Contains("[cut ", fitted.Text);
        Assert.EndsWith(pad, fitted.Text);
    }

    [Fact]
    public void TheFirstLineNamesTheSourcePlatform()
    {
        var text = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", "A summary.", SummaryLevel.Normal, "you: hi", 1);

        Assert.Equal("Summary of a Claude Code session", Lines(text)[0]);
    }

    [Fact]
    public void TheNewSessionLineFollowsTheFirstLineThenTheLevelHeadingAndTheSummary()
    {
        var text = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", "A summary.", SummaryLevel.Normal, "you: hi", 1);

        var lines = Lines(text);
        Assert.Equal(NewSession, lines[1]);
        Assert.Equal("## Summary (normal)", lines[2]);
        Assert.Equal("A summary.", lines[3]);
    }

    [Fact]
    public void TheNewSessionLineNamesTheTargetAndSaysTheSourceIsNotContinued()
    {
        var text = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", "A summary.", SummaryLevel.Complete, "you: hi", 1);

        Assert.Contains("This new Codex CLI session starts from that summary.", text);
        Assert.Contains("The Claude Code session itself is not continued.", text);
    }

    [Theory]
    [InlineData("Claude Code", "GitHub Copilot CLI")]
    [InlineData("Codex CLI", "Claude Code")]
    public void TheFirstLineAndTheNewSessionLineTakeEachPhrase(string targetPhrase, string sourcePhrase)
    {
        var text = ProviderResumeText.ComposeFrom(targetPhrase, sourcePhrase, "A summary.", SummaryLevel.Normal, "you: hi", 1);

        Assert.Equal($"Summary of a {sourcePhrase} session", Lines(text)[0]);
        Assert.Contains($"This new {targetPhrase} session starts from that summary. The {sourcePhrase} session itself is not continued.", text);
    }

    [Fact]
    public void TheTailAndTheClosingLineAreWrittenAsComposeWritesThem()
    {
        const string tail = "you: hi\nclaude: reply 0";
        var composed = ProviderResumeText.Compose("Codex CLI", "A summary.", SummaryLevel.Normal, tail, 1, SourceId);

        var text = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", "A summary.", SummaryLevel.Normal, tail, 1);

        Assert.Equal("Summary of a Claude Code session", Lines(text)[0]);
        Assert.Equal(FromTheTail(composed), FromTheTail(text));
        Assert.Contains($"## The last 1 turns, word for word (messages only){Environment.NewLine}you: hi\nclaude: reply 0{Environment.NewLine}", text);
        Assert.EndsWith(Closing, text);
    }

    [Fact]
    public void NoSessionLineIsWritten()
    {
        var text = ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", "A summary.", SummaryLevel.Normal, "you: hi", 1);

        Assert.DoesNotContain("Continues session", text);
    }

    [Theory]
    [InlineData(null, SummaryLevel.Normal)]
    [InlineData("", SummaryLevel.Normal)]
    [InlineData("A summary.", null)]
    public void WithoutASummaryAndItsLevelItThrows(string? summary, SummaryLevel? level)
    {
        Assert.Throws<ArgumentException>(() =>
            ProviderResumeText.ComposeFrom("Codex CLI", "Claude Code", summary, level, string.Empty, 0));
    }
}

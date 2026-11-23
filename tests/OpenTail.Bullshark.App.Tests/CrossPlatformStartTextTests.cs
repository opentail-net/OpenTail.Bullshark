using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The cross-platform start text (S3b-A): the header that names the source platform, the new session's own words (RS-D5, RS-D6), the target's own tail
/// and limit, the target's checks before the summary call (RS-D13, RS-D17) and the Claude target's default model and effort (RS-D12). Pure: no model
/// call, no CLI process and no state folder. The screens' builders start the CLIs' own checks, so they are not run here; the check before the summary
/// is tested through <see cref="CrossPlatformStartText.RefusalBeforeSummaryAsync"/> with a fake check.
/// </summary>
public sealed class CrossPlatformStartTextTests
{
    private const string ClosingLine =
        "Carry on from this. Where something is unclear, make the sensible decision and say what you decided.";

    private static TranscriptLine Line(string kind, string text) => new(0, kind, null, text, Collapsed: false);

    /// <summary>A Claude session: two turns, with a tool call and its result in the first, which a cross-platform tail leaves out.</summary>
    private static List<TranscriptLine> ClaudeConversation() =>
    [
        Line("you", "first question"),
        Line("claude", "first answer"),
        Line("tool", "▸ Read"),
        Line("result", "result: ok"),
        Line("you", "second question"),
        Line("claude", "second answer"),
    ];

    /// <summary>The lines of a text as AppendLine writes them, joined with the platform newline, with the newline after the last one.</summary>
    private static string Block(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;

    [Fact]
    public void FromClaudeToCodexNamesTheSourceInItsFirstLineAndHasNoSessionLine()
    {
        var fitted = CrossPlatformStartText.Build(
            ProviderId.Claude, ProviderId.Codex, "Built the parser.", SummaryLevel.Normal, ClaudeConversation(), 10);

        // The tail keeps the owner's messages and the source's own messages with the Claude label; the tool call and its result are left out. There is no "Continues session" line.
        var expected = Block(
            "Summary of a Claude Code session",
            "This new Codex CLI session starts from that summary. The Claude Code session itself is not continued.",
            "## Summary (normal)",
            "Built the parser.",
            string.Empty,
            "## The last 10 turns, word for word (messages only)",
            "you: first question\nclaude: first answer\n\nyou: second question\nclaude: second answer")
            + Environment.NewLine + ClosingLine;
        Assert.Equal(new ResumeText.FittedStart(expected, 0, Fits: true), fitted);
    }

    [Fact]
    public void FromCodexToClaudeFitsTheClaudeLimitByCuttingTheLatestTurn()
    {
        var longAnswer = new string('a', ClaudeCli.MaxLaunchCharacters + 5_000);
        var lines = new List<TranscriptLine> { Line("you", "question"), Line("codex", longAnswer) };

        // The command line is measured as the length of the text, so the cut is the one the limit asks for.
        var fitted = CrossPlatformStartText.Build(
            ProviderId.Codex, ProviderId.Claude, "Summary text.", SummaryLevel.Minimal, lines, 10, text => text.Length);

        Assert.True(fitted.Fits);
        Assert.True(fitted.CutCharacters > 0);
        Assert.True(fitted.Text.Length <= ClaudeCli.MaxLaunchCharacters);
        Assert.StartsWith(
            Block(
                "Summary of a Codex CLI session",
                "This new Claude Code session starts from that summary. The Codex CLI session itself is not continued.",
                "## Summary (minimal)",
                "Summary text.",
                string.Empty,
                "## The last 10 turns, word for word (messages only)")
            + "you: question\ncodex: aaa",
            fitted.Text);
        Assert.EndsWith(ClosingLine, fitted.Text);
    }

    [Fact]
    public void FromCopilotToCodexKeepsTheCopilotLabelOnItsMessages()
    {
        var lines = new List<TranscriptLine> { Line("you", "question"), Line("copilot", "copilot's answer") };

        var fitted = CrossPlatformStartText.Build(ProviderId.Copilot, ProviderId.Codex, "Summary.", SummaryLevel.Normal, lines, 10);

        Assert.Contains("you: question\ncopilot: copilot's answer", fitted.Text);
        Assert.DoesNotContain("codex: ", fitted.Text);
        Assert.Contains("This new Codex CLI session starts from that summary. The GitHub Copilot CLI session itself is not continued.", fitted.Text);
    }

    [Fact]
    public void FromCodexToCopilotIsBuiltWithCopilotsTailAndPhrase()
    {
        var lines = new List<TranscriptLine> { Line("you", "question"), Line("codex", "codex's answer") };

        var fitted = CrossPlatformStartText.Build(ProviderId.Codex, ProviderId.Copilot, "Summary.", SummaryLevel.Complete, lines, 10);

        Assert.Equal(0, fitted.CutCharacters);
        Assert.True(fitted.Fits);
        Assert.Contains("Summary of a Codex CLI session", fitted.Text);
        Assert.Contains("This new GitHub Copilot CLI session starts from that summary.", fitted.Text);
        Assert.Contains("## Summary (complete)", fitted.Text);
        Assert.Contains("you: question\ncodex: codex's answer", fitted.Text);
    }

    [Fact]
    public void FromClaudeToCodexKeepsTheClaudeLabelOnAClaudeReply()
    {
        var lines = new List<TranscriptLine> { Line("you", "owner question alpha"), Line("claude", "claude reply alpha") };

        var fitted = CrossPlatformStartText.Build(ProviderId.Claude, ProviderId.Codex, "Summary.", SummaryLevel.Normal, lines, 10);

        Assert.Contains("claude: claude reply alpha", fitted.Text);
        Assert.DoesNotContain("codex: claude reply alpha", fitted.Text);
    }

    [Fact]
    public void FromCodexToClaudeKeepsTheCodexLabelOnACodexReply()
    {
        var lines = new List<TranscriptLine> { Line("you", "owner question beta"), Line("codex", "codex reply beta") };

        var fitted = CrossPlatformStartText.Build(
            ProviderId.Codex, ProviderId.Claude, "Summary.", SummaryLevel.Normal, lines, 10, text => text.Length);

        Assert.Contains("codex: codex reply beta", fitted.Text);
        Assert.DoesNotContain("claude: codex reply beta", fitted.Text);
    }

    [Fact]
    public void ToolAndResultLinesAreLeftOutOfEveryCrossTail()
    {
        foreach (var (source, kind) in new[] { (ProviderId.Claude, "claude"), (ProviderId.Codex, "codex"), (ProviderId.Copilot, "copilot") })
        foreach (var target in new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot })
        {
            if (source == target) continue;
            var lines = new List<TranscriptLine>
            {
                Line("you", "owner question gamma"),
                Line("tool", "tool marker gamma"),
                Line("result", "result marker gamma"),
                Line(kind, $"{kind} reply gamma"),
            };

            var fitted = CrossPlatformStartText.Build(source, target, "Summary.", SummaryLevel.Normal, lines, 10, text => text.Length);

            Assert.DoesNotContain("tool marker gamma", fitted.Text);
            Assert.DoesNotContain("result marker gamma", fitted.Text);
            Assert.Contains($"{kind}: {kind} reply gamma", fitted.Text);
        }
    }

    [Fact]
    public void TheOwnersLinesAreKeptInEveryCrossTail()
    {
        foreach (var (source, kind) in new[] { (ProviderId.Claude, "claude"), (ProviderId.Codex, "codex"), (ProviderId.Copilot, "copilot") })
        foreach (var target in new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot })
        {
            if (source == target) continue;
            var lines = new List<TranscriptLine> { Line("you", "owner question delta"), Line(kind, $"{kind} reply delta") };

            var fitted = CrossPlatformStartText.Build(source, target, "Summary.", SummaryLevel.Normal, lines, 10, text => text.Length);

            Assert.Contains("you: owner question delta", fitted.Text);
        }
    }

    [Theory]
    [InlineData(null, SummaryLevel.Normal)]
    [InlineData("   ", SummaryLevel.Normal)]
    [InlineData("A summary.", null)]
    public void ACrossStartWithoutASummaryOrLevelThrows(string? summary, SummaryLevel? level)
    {
        Assert.Throws<ArgumentException>(() =>
            CrossPlatformStartText.Build(ProviderId.Claude, ProviderId.Codex, summary, level, ClaudeConversation(), 10));
        Assert.Throws<ArgumentException>(() =>
            CrossPlatformStartText.Build(ProviderId.Copilot, ProviderId.Codex, summary, level, ClaudeConversation(), 10));
    }

    [Fact]
    public void ACrossStartToClaudeWithoutASummaryThrowsBeforeTheLimitIsMeasured()
    {
        var measured = false;

        Assert.Throws<ArgumentException>(() => CrossPlatformStartText.Build(
            ProviderId.Codex, ProviderId.Claude, null, SummaryLevel.Normal, ClaudeConversation(), 10, text => { measured = true; return text.Length; }));
        Assert.False(measured);
    }

    [Fact]
    public void ASamePlatformStartIsNotBuiltHere()
    {
        Assert.Throws<ArgumentException>(() =>
            CrossPlatformStartText.Build(ProviderId.Codex, ProviderId.Codex, "Summary.", SummaryLevel.Normal, ClaudeConversation(), 10));
    }

    [Fact]
    public void AClaudeTargetNeedsItsCommandLineMeasure()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CrossPlatformStartText.Build(ProviderId.Codex, ProviderId.Claude, "Summary.", SummaryLevel.Normal, ClaudeConversation(), 10));
    }

    [Fact]
    public void ThePhrasesAreTheOnesTheSameScreenTextsWrite()
    {
        Assert.Equal("Claude Code", CrossPlatformStartText.PhraseOf(ProviderId.Claude));
        Assert.Equal("Codex CLI", CrossPlatformStartText.PhraseOf(ProviderId.Codex));
        Assert.Equal("GitHub Copilot CLI", CrossPlatformStartText.PhraseOf(ProviderId.Copilot));

        Assert.Contains($"an earlier {CrossPlatformStartText.PhraseOf(ProviderId.Claude)} session", ResumeText.Compose(null, null, string.Empty, 0));
        Assert.Contains($"an earlier {CrossPlatformStartText.PhraseOf(ProviderId.Codex)} session", CodexResumeText.Compose(null, null, string.Empty, 0));
        Assert.Contains($"an earlier {CrossPlatformStartText.PhraseOf(ProviderId.Copilot)} session", CopilotResumeText.Compose(null, null, string.Empty, 0));
    }

    [Fact]
    public async Task ARefusedModeGivesItsReasonAndRunsNoCheck()
    {
        var checks = 0;

        var refusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
            ProviderId.Claude, ProviderId.Codex, ContinueMode.LastTurns, () => { checks++; return Task.FromResult<string?>(null); });

        Assert.Equal(ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.LastTurns, ProviderId.Codex), refusal);
        Assert.NotNull(refusal);
        Assert.Equal(0, checks);
    }

    [Fact]
    public async Task ACopilotTargetFromASummaryIsAllowedAndAFullStartIsRefusedWithoutACheck()
    {
        // PP-D2 (2026-11-23): Copilot is an enabled target, so a summary to Copilot is not refused here.
        var checks = 0;

        var refusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
            ProviderId.Claude, ProviderId.Copilot, ContinueMode.Summary, () => { checks++; return Task.FromResult<string?>(null); });
        Assert.Null(refusal);

        var full = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
            ProviderId.Claude, ProviderId.Copilot, ContinueMode.Full, () => { checks++; return Task.FromResult<string?>(null); });
        Assert.Equal(ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.Full, ProviderId.Copilot), full);
        Assert.NotNull(full);
        // The allowed summary ran its check once; the refused full start ran none.
        Assert.Equal(1, checks);
    }

    [Fact]
    public async Task WhenBothRulesFailTheModeReasonComesFirst()
    {
        var checks = 0;

        var refusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
            ProviderId.Codex, ProviderId.Copilot, ContinueMode.Full, () => { checks++; return Task.FromResult<string?>(null); });

        Assert.StartsWith("Only a summary can start in another platform.", refusal);
        Assert.Equal(0, checks);
    }

    [Fact]
    public async Task ATargetCheckRunsOnceAndItsRefusalIsReturned()
    {
        var checks = 0;
        const string launchRefusal = "Not started: Claude Code 1.0 is older than 2.0, the version this build needs.";

        var refusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
            ProviderId.Codex, ProviderId.Claude, ContinueMode.Summary, () => { checks++; return Task.FromResult<string?>(launchRefusal); });

        Assert.Equal(launchRefusal, refusal);
        Assert.Equal(1, checks);
    }

    [Fact]
    public async Task ATargetCheckThatPassesLetsTheStartGoOn()
    {
        var checks = 0;

        var refusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
            ProviderId.Codex, ProviderId.Claude, ContinueMode.Summary, () => { checks++; return Task.FromResult<string?>(null); });

        Assert.Null(refusal);
        Assert.Equal(1, checks);
    }

    [Fact]
    public void TheClaudeTargetStartsOnTheModelAndEffortOfTheClaudeScreen()
    {
        // The Claude screen takes its planner model and effort from the saved solo pattern, or from the defaults when none is saved.
        Assert.Equal((RunPlan.PlannerModel, RunPlan.PlannerEffort), CrossPlatformStartText.ClaudeDefaults(PatternStore.Defaults("solo")));

        var saved = PatternStore.Defaults("solo") with { PlannerModel = "saved-model", PlannerEffort = "saved-effort" };
        Assert.Equal(("saved-model", "saved-effort"), CrossPlatformStartText.ClaudeDefaults(saved));
    }
}

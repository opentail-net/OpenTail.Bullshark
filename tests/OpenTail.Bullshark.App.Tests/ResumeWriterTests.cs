using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Who writes a resume's summary when the source is out of quota or its summary failed (resume source and target plan, RS-D9, RS-D28). The pure rules are
/// tested as tables. The screens are tested headless with fake preview callbacks: the Claude dialog at its limit calls the target and not the source, and the
/// "Make summary with" button is offered only after a source failure, and only for a target other than the source. No CLI process starts and no state folder
/// is read or written; the target's own checks and calls are in the window's builders, which no test makes.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ResumeWriterTests
{
    private const string SummaryText = "An example summary written by the target.";

    private const string TailText = "you: an example message";

    private const string Boom = "The summary was not made: an example failure.";

    // ---- The pure rules (SummaryWriterRules) ----

    /// <summary>The platform a test names: the same ids as <see cref="ProviderId"/>'s constants.</summary>
    private static ProviderId P(string name) => name switch
    {
        "Claude" => ProviderId.Claude,
        "Codex" => ProviderId.Codex,
        "Copilot" => ProviderId.Copilot,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("Claude", "Claude", false, "Claude")]
    [InlineData("Claude", "Codex", false, "Claude")]
    [InlineData("Claude", "Codex", true, "Codex")]
    [InlineData("Codex", "Codex", true, "Codex")]
    [InlineData("Codex", "Claude", true, "Claude")]
    [InlineData("Copilot", "Claude", false, "Copilot")]
    public void TheWriterIsTheTargetOnlyWhenAskedAndAnotherPlatform(string source, string target, bool writeByTarget, string writer)
    {
        Assert.Equal(P(writer), SummaryWriterRules.WriterOf(P(source), P(target), writeByTarget));
    }

    [Theory]
    [InlineData(true, "Codex", true)]
    [InlineData(true, "Claude", false)]
    [InlineData(false, "Codex", false)]
    public void ClaudeAtItsLimitTheTargetWritesOnlyForAnotherPlatform(bool atLimit, string target, bool expected)
    {
        Assert.Equal(expected, SummaryWriterRules.ClaudeAtLimitWritesByTarget(ProviderId.Claude, P(target), atLimit));
    }

    [Fact]
    public void ACodexSourceIsNeverWrittenByTheTargetAtItsLimit()
    {
        // The limit rule is Claude's only: a Codex source is never at a limit here.
        Assert.False(SummaryWriterRules.ClaudeAtLimitWritesByTarget(ProviderId.Codex, ProviderId.Claude, claudeAtLimit: true));
    }

    [Fact]
    public void TheWordsNameTheTargetAndTheOneCall()
    {
        Assert.Equal("Claude is at its limit, so Codex writes the summary (one call, made by Codex).", SummaryWriterRules.AtLimitCostLine(ProviderId.Codex));
        Assert.Equal("One call, made by Copilot.", SummaryWriterRules.OneCallLine(ProviderId.Copilot));
        Assert.Equal("Make summary with Claude", SummaryWriterRules.ButtonOf(ProviderId.Claude));
        Assert.Equal("Summary written by Codex.", SummaryWriterRules.WrittenByLine(ProviderId.Codex));
    }

    [Fact]
    public void ASummaryTheSourceWroteHasNoWriter()
    {
        Assert.Null(SummaryWriterRules.WrittenByOf(ProviderId.Claude, ProviderId.Claude));
        Assert.Equal(ProviderId.Codex, SummaryWriterRules.WrittenByOf(ProviderId.Claude, ProviderId.Codex));
    }

    // ---- The shared screen: Codex and Copilot ----

    private static CodexResumePrompt NewCodexPrompt(Func<CodexResumeChoice, KeptSummary?, bool, System.Threading.CancellationToken, Task<CodexPreview>> preview)
    {
        AppResources.Ensure();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            previewAsync: preview);
    }

    private static T Named<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pick(Window window, string itemName) =>
        Named<MenuItem>(window, itemName).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static void SummaryAt(Window window, string levelTag)
    {
        Named<RadioButton>(window, "SummaryChoice").IsChecked = true;
        Named<ComboBox>(window, "LevelBox").SelectedIndex = levelTag switch
        {
            "Zero" => 0,
            "Minimal" => 1,
            "Normal" => 2,
            "Complete" => 3,
            _ => throw new ArgumentException($"no level {levelTag}", nameof(levelTag)),
        };
    }

    [Fact]
    public void TheCodexScreenOffersTheTargetsSummaryOnlyAfterASourceFailureWithAnotherPlatform()
    {
        UiThread.Run(() =>
        {
            // The source's own Make summary fails (a summary error, not a refusal). The target writes it when the owner asks.
            var calls = new List<bool>();
            var prompt = NewCodexPrompt((choice, kept, writeByTarget, _) =>
            {
                if (choice.Level is null) return Task.FromResult(new CodexPreview(TailText, null));
                calls.Add(writeByTarget);
                if (writeByTarget)
                {
                    var made = new KeptSummary(SummaryLevel.Normal, SummaryText, Saved: true, Turns: 10, WrittenBy: ProviderId.Claude);
                    return Task.FromResult(new CodexPreview(SummaryText, made));
                }
                return Task.FromResult(new CodexPreview(string.Empty, null, Boom, SummaryFailed: true));
            });
            SummaryAt(prompt, "Normal");

            // With the source as the target, a failed summary has no target to ask: the button stays hidden (the S6c fallback applies).
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.Equal(Visibility.Collapsed, Named<Button>(prompt, "WriteByTargetButton").Visibility);

            // Another platform chosen: the button offers it, and its cost line says one call, made by that platform.
            Pick(prompt, "TargetClaudeItem");
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            var button = Named<Button>(prompt, "WriteByTargetButton");
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.Equal("Make summary with Claude", (string?)button.Content);
            Assert.Contains("One call, made by Claude.", Named<TextBlock>(prompt, "CostText").Text);
            Assert.Equal(new[] { false, false }, calls);

            // The button asks the target to write it. The preview names the writer.
            Click(button);

            Assert.Equal(new[] { false, false, true }, calls);
            Assert.Equal(SummaryText, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.Contains("Summary written by Claude.", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.Equal(Visibility.Collapsed, button.Visibility);
        });
    }

    [Fact]
    public void ATargetsRefusalMakesNoSummaryAndKeepsNone()
    {
        UiThread.Run(() =>
        {
            // The target's check refuses before its call (RS-D13): the preview is a refusal with no summary, and it is not a summary failure.
            var calls = new List<bool>();
            var prompt = NewCodexPrompt((choice, kept, writeByTarget, _) =>
            {
                if (choice.Level is null) return Task.FromResult(new CodexPreview(TailText, null));
                calls.Add(writeByTarget);
                if (writeByTarget) return Task.FromResult(new CodexPreview(string.Empty, null, "Not started: an example refusal."));
                return Task.FromResult(new CodexPreview(string.Empty, null, Boom, SummaryFailed: true));
            });
            Pick(prompt, "TargetClaudeItem");
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Click(Named<Button>(prompt, "WriteByTargetButton"));

            Assert.Equal(new[] { false, true }, calls);
            Assert.Equal(Visibility.Collapsed, Named<Button>(prompt, "WriteByTargetButton").Visibility);
            Assert.Null(prompt.KeptSummary);
        });
    }

    // ---- The Claude dialog ----

    private static ResumePrompt NewClaudePrompt(
        Func<ResumeChoice, KeptSummary?, bool, System.Threading.CancellationToken, Task<ResumePromptPreview>> preview, Func<bool>? sourceAtLimit)
    {
        AppResources.Ensure();
        return new ResumePrompt(
            "Resume: a test session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            contextTokens: 500_000,
            RailPalette.DarkPalette,
            "opus",
            "high",
            previewAsync: preview,
            lines: 240,
            sourceAtLimit: sourceAtLimit);
    }

    [Fact]
    public void AClaudeSummaryAtItsLimitIsMadeByTheTargetAndNotByClaude()
    {
        UiThread.Run(() =>
        {
            // RS-D28: Claude is at its limit and Codex is chosen. The cost line says Codex writes it, and Make summary calls Codex's side only.
            var writers = new List<bool>();
            var prompt = NewClaudePrompt((choice, kept, writeByTarget, _) =>
            {
                writers.Add(writeByTarget);
                if (!writeByTarget) return Task.FromResult(new ResumePromptPreview(null, "Claude's own call would have been made here.", SummaryFailed: true));
                var summary = new KeptSummary(SummaryLevel.Normal, SummaryText, Saved: true, Turns: 10, Lines: 240, WrittenBy: ProviderId.Codex);
                return Task.FromResult(new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, summary)));
            }, sourceAtLimit: () => true);
            Pick(prompt, "TargetCodexItem");
            SummaryAt(prompt, "Normal");

            Assert.Contains(SummaryWriterRules.AtLimitCostLine(ProviderId.Codex), Named<TextBlock>(prompt, "CostText").Text);
            Assert.Empty(writers);

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Assert.Equal(new[] { true }, writers);
            Assert.Equal(SummaryText, Named<TextBox>(prompt, "PreviewText").Text);
            Assert.Contains("Summary written by Codex.", Named<TextBlock>(prompt, "PreviewHeader").Text);
        });
    }

    [Fact]
    public void AClaudeSummaryWithClaudeChosenStaysWithClaudeEvenAtItsLimit()
    {
        UiThread.Run(() =>
        {
            // With Claude as the target there is no other platform to write it: the call is Claude's, as it always is.
            var writers = new List<bool>();
            var prompt = NewClaudePrompt((choice, kept, writeByTarget, _) =>
            {
                writers.Add(writeByTarget);
                return Task.FromResult(new ResumePromptPreview(null, Boom, SummaryFailed: true));
            }, sourceAtLimit: () => true);
            SummaryAt(prompt, "Normal");

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Assert.Equal(new[] { false }, writers);
            Assert.Equal(Visibility.Collapsed, Named<Button>(prompt, "WriteByTargetButton").Visibility);
        });
    }

    [Fact]
    public void TheClaudeDialogOffersTheTargetsSummaryOnlyAfterAFailureAndOnlyForAnotherPlatform()
    {
        UiThread.Run(() =>
        {
            var prompt = NewClaudePrompt((choice, kept, writeByTarget, _) =>
                Task.FromResult(writeByTarget
                    ? new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, new KeptSummary(SummaryLevel.Normal, SummaryText, Saved: true, Turns: 10, Lines: 240, WrittenBy: ProviderId.Codex)))
                    : new ResumePromptPreview(null, Boom, SummaryFailed: true)), sourceAtLimit: () => false);
            SummaryAt(prompt, "Normal");

            // Claude chosen: the failure is the S6c path, and no button is offered.
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.Equal(Visibility.Collapsed, Named<Button>(prompt, "WriteByTargetButton").Visibility);

            // Codex chosen: the failure offers Codex's summary, and the button writes it.
            Pick(prompt, "TargetCodexItem");
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            var button = Named<Button>(prompt, "WriteByTargetButton");
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.Equal("Make summary with Codex", (string?)button.Content);

            Click(button);
            Assert.Equal(SummaryText, Named<TextBox>(prompt, "PreviewText").Text);
            Assert.Contains("Summary written by Codex.", Named<TextBlock>(prompt, "PreviewHeader").Text);
        });
    }
}

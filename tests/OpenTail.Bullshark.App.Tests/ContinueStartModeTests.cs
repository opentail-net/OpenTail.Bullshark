using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The starting mode of the resume screens (stage C4, CN-D2, CN-D3, CN-D5, CN-D9, RS-D3, RS-D27), headless. The fake preview callbacks record the summary
/// calls they are asked for (a Zero preview makes no call, so it is not recorded), so the rule that opening a screen never makes a summary is tested. A
/// stored last-turns mode opens at Zero with the tail (RS-D27). The sample texts are made up; no transcript or rollout is read.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ContinueStartModeTests
{
    private const string SampleText = "you: an example message\nclaude: an example reply";

    /// <summary>Claude's cost line for a summary at the normal level, on the model and effort the helper passes (opus high).</summary>
    private static readonly string ClaudeSummaryCost =
        $"One Haiku call (low effort) reads your messages and Claude's replies, not the tool output, up to about " +
        $"{ResumeText.SummaryInputChars / 4:N0} tokens, and writes the normal summary. Then a new session starts from it and from the last turns. " +
        "The new session runs at opus high.";

    private static T Named<T>(FrameworkElement window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    /// <summary>The level row's segment with the given tag: Zero, Minimal, Normal or Complete.</summary>
    private static RadioButton Level(FrameworkElement window, string tag) =>
        Named<Panel>(window, "LevelRow").Children.OfType<RadioButton>().Single(radio => (string?)radio.Tag == tag);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static bool Checked(RadioButton radio) => radio.IsChecked == true;

    // ---- The mapping of a Continue mode to a starting mode (pure: no window). ----

    [Fact]
    public void EachContinueModeOpensTheSharedModeAndNoModeGivesNone()
    {
        Assert.Equal(ProviderResumeMode.Full, ProviderResumeRules.StartMode(ContinueMode.Full));
        Assert.Equal(ProviderResumeMode.Summary, ProviderResumeRules.StartMode(ContinueMode.LastTurns));
        Assert.Equal(ProviderResumeMode.Summary, ProviderResumeRules.StartMode(ContinueMode.Summary));
        Assert.Null(ProviderResumeRules.StartMode(null));
    }

    // ---- Claude's screen (CN-D9). With no starting mode the screen must look and behave as it did before C4, for Full and the summary. ----

    /// <summary>Claude's screen, with the given context size. Each summary call of the preview is recorded by its mode; a Zero preview makes none.</summary>
    private static ResumePrompt NewClaudePrompt(long contextTokens, List<ResumeMode> calls, ContinueMode? initialMode = null)
    {
        AppResources.Ensure();
        return new ResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            contextTokens,
            RailPalette.DarkPalette,
            "opus",
            "high",
            (choice, kept, token) =>
            {
                if (choice.Level is not null) calls.Add(choice.Mode);
                return Task.FromResult(new ResumePromptPreview(new ResumePreview(SampleText, 0, true, null)));
            },
            initialMode);
    }

    [Fact]
    public void ClaudeWithNoStartModeOpensOnInFullWhenTheContextIsSmallAndTheTextsAreUnchanged()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeMode>();
            var prompt = NewClaudePrompt(10_000, calls);

            Assert.True(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.Equal(
                "Recommended: in full. The context is about 10,000 tokens, small enough to re-read before the first reply.",
                Named<TextBlock>(prompt, "Recommendation").Text);
            Assert.Equal("Full 👍 (recommended)", Named<RadioButton>(prompt, "FullChoice").Content);
            Assert.Equal("Summary", Named<RadioButton>(prompt, "SummaryChoice").Content);
            Assert.Equal(Visibility.Collapsed, Named<UIElement>(prompt, "LevelBlock").Visibility);
            Assert.Equal(Visibility.Collapsed, Named<UIElement>(prompt, "TailBlock").Visibility);
            Assert.Equal(
                "Resuming in full re-reads all of the context, about 10,000 tokens, before the first reply. A warm cache makes that cheap; " +
                "a cold one charges it at full price. Nothing runs until you press Resume. The new session runs at opus high.",
                Named<TextBlock>(prompt, "CostText").Text);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeWithNoStartModeOpensOnTheSummaryWhenTheContextIsLargeAndTheTextsAreUnchanged()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeMode>();
            var prompt = NewClaudePrompt(500_000, calls);

            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.Equal(
                "Recommended: from a summary. The context is about 500,000 tokens; a full resume would re-read all of it before the first reply.",
                Named<TextBlock>(prompt, "Recommendation").Text);
            Assert.Equal("Full", Named<RadioButton>(prompt, "FullChoice").Content);
            Assert.Equal("Summary 👍 (recommended)", Named<RadioButton>(prompt, "SummaryChoice").Content);
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "LevelBlock").Visibility);
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "TailBlock").Visibility);
            Assert.True(Checked(Level(prompt, "Normal")));
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            Assert.Equal(ClaudeSummaryCost, Named<TextBlock>(prompt, "CostText").Text);
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeOpenedOnInFullChecksInFullAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeMode>();
            var prompt = NewClaudePrompt(500_000, calls, ContinueMode.Full);

            Assert.True(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeOpenedOnTheLastTurnsOpensOnZeroWithTheTailAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeMode>();
            var prompt = NewClaudePrompt(500_000, calls, ContinueMode.LastTurns);

            // A stored last-turns mode reads as Summary at Zero with the tail on (RS-D27): the Zero text is built at once, and no summary is called.
            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Level(prompt, "Zero")));
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            Assert.Equal("10", Named<TextBox>(prompt, "TurnsBox").Text);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeOpenedOnASummaryChecksTheSummaryRadioAndMakesNoCallEvenWhenFullIsRecommended()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeMode>();
            var prompt = NewClaudePrompt(10_000, calls, ContinueMode.Summary);

            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Level(prompt, "Normal")));
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "LevelBlock").Visibility);
            Assert.Equal(ClaudeSummaryCost, Named<TextBlock>(prompt, "CostText").Text);
            // The summary is not made at open, so Start waits for Make summary (RS-D21).
            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeRememberTickIsOffWhenTheScreenOpens()
    {
        // The owner's decision of 2026-11-23: Claude's screen gets the "Remember for this session" tick too. It supersedes the C4 AFK default of no tick.
        UiThread.Run(() =>
        {
            var remember = Named<CheckBox>(NewClaudePrompt(10_000, new List<ResumeMode>()), "RememberBox");
            Assert.False(remember.IsChecked == true);
            Assert.Equal("Remember for this session", (string?)remember.Content);
        });
    }

    // ---- Codex's screen. ----

    private static CodexResumePrompt NewCodexPrompt(List<CodexResumeMode> calls, ContinueMode? initialMode = null)
    {
        AppResources.Ensure();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) =>
            {
                if (choice.Level is not null) calls.Add(choice.Mode);
                return Task.FromResult(new CodexPreview(SampleText, null));
            },
            models: null,
            initialMode: initialMode);
    }

    [Fact]
    public void CodexWithNoStartModeOpensOnZeroWithTheTailAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeMode>();
            var prompt = NewCodexPrompt(calls);

            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.True(Checked(Level(prompt, "Zero")));
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void CodexOpenedOnInFullChecksInFullAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeMode>();
            var prompt = NewCodexPrompt(calls, ContinueMode.Full);

            Assert.True(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.False(Named<TextBox>(prompt, "TurnsBox").IsEnabled);
        });
    }

    [Fact]
    public void CodexOpenedOnTheLastTurnsOpensOnZeroWithTheTailAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeMode>();
            var prompt = NewCodexPrompt(calls, ContinueMode.LastTurns);

            // A stored last-turns mode reads as Summary at Zero with the tail (RS-D27): nothing is called at open.
            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Level(prompt, "Zero")));
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void CodexOpenedOnASummaryChecksTheSummaryRadioAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeMode>();
            var prompt = NewCodexPrompt(calls, ContinueMode.Summary);

            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Level(prompt, "Normal")));
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Panel>(prompt, "LevelRow").IsEnabled);
            Assert.Equal(CodexResumeRules.CostLine(CodexResumeMode.Summary), Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void CodexRememberTickIsOffWhenTheScreenOpensAndIsNamedAsSpecified()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCodexPrompt(new List<CodexResumeMode>());
            var remember = Named<CheckBox>(prompt, "RememberBox");
            Assert.False(remember.IsChecked == true);
            Assert.Equal("Remember for this session", (string?)remember.Content);
        });
    }

    [Fact]
    public void CodexStartReturnsTheTickAsItIsWhenStartIsPressed()
    {
        UiThread.Run(() =>
        {
            var untouched = NewCodexPrompt(new List<CodexResumeMode>());
            Click(Named<Button>(untouched, "StartButton"));
            Assert.NotNull(untouched.Choice);
            Assert.False(untouched.Choice!.Remember);

            var ticked = NewCodexPrompt(new List<CodexResumeMode>());
            Named<CheckBox>(ticked, "RememberBox").IsChecked = true;
            Click(Named<Button>(ticked, "StartButton"));
            Assert.NotNull(ticked.Choice);
            Assert.True(ticked.Choice!.Remember);
            Assert.Equal(CodexResumeMode.Summary, ticked.Choice.Mode);
            Assert.Null(ticked.Choice.Level);
        });
    }

    [Fact]
    public void CodexChoiceRecordDefaultsTheTickToOff()
    {
        Assert.False(new CodexResumeChoice(CodexResumeMode.Summary, null, 4).Remember);
    }

    // ---- Copilot's screen. ----

    private static CopilotResumePrompt NewCopilotPrompt(List<CopilotResumeMode> calls, ContinueMode? initialMode = null)
    {
        AppResources.Ensure();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) =>
            {
                if (choice.Level is not null) calls.Add(choice.Mode);
                return Task.FromResult(new CopilotPreview(SampleText, null));
            },
            initialMode);
    }

    [Fact]
    public void CopilotWithNoStartModeOpensOnZeroWithTheTailAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeMode>();
            var prompt = NewCopilotPrompt(calls);

            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.True(Checked(Level(prompt, "Zero")));
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void CopilotOpenedOnInFullChecksInFullAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeMode>();
            var prompt = NewCopilotPrompt(calls, ContinueMode.Full);

            Assert.True(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.False(Named<TextBox>(prompt, "TurnsBox").IsEnabled);
        });
    }

    [Fact]
    public void CopilotOpenedOnTheLastTurnsOpensOnZeroWithTheTailAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeMode>();
            var prompt = NewCopilotPrompt(calls, ContinueMode.LastTurns);

            // A stored last-turns mode reads as Summary at Zero with the tail (RS-D27): nothing is called at open.
            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Level(prompt, "Zero")));
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void CopilotOpenedOnASummaryChecksTheSummaryRadioAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeMode>();
            var prompt = NewCopilotPrompt(calls, ContinueMode.Summary);

            Assert.True(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Level(prompt, "Normal")));
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Panel>(prompt, "LevelRow").IsEnabled);
            Assert.Equal(CopilotResumeRules.CostLine(CopilotResumeMode.Summary), Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void CopilotRememberTickIsOffWhenTheScreenOpensAndIsNamedAsSpecified()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCopilotPrompt(new List<CopilotResumeMode>());
            var remember = Named<CheckBox>(prompt, "RememberBox");

            Assert.False(remember.IsChecked == true);
            Assert.Equal("Remember for this session", (string?)remember.Content);
        });
    }

    [Fact]
    public void CopilotStartReturnsTheTickAsItIsWhenStartIsPressed()
    {
        UiThread.Run(() =>
        {
            var untouched = NewCopilotPrompt(new List<CopilotResumeMode>());
            Click(Named<Button>(untouched, "StartButton"));
            Assert.NotNull(untouched.Choice);
            Assert.False(untouched.Choice!.Remember);

            var ticked = NewCopilotPrompt(new List<CopilotResumeMode>());
            Named<CheckBox>(ticked, "RememberBox").IsChecked = true;
            Click(Named<Button>(ticked, "StartButton"));
            Assert.NotNull(ticked.Choice);
            Assert.True(ticked.Choice!.Remember);
            Assert.Equal(CopilotResumeMode.Summary, ticked.Choice.Mode);
        });
    }

    [Fact]
    public void CopilotChoiceRecordDefaultsTheTickToOff()
    {
        Assert.False(new CopilotResumeChoice(CopilotResumeMode.Summary, null, 4).Remember);
    }
}

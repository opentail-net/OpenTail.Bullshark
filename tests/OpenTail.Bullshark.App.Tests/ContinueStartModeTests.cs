using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The starting mode of the resume screens (stage C4, CN-D2, CN-D3, CN-D5, CN-D9), headless. The fake preview callbacks record the modes they are
/// asked for, so the rule that opening a screen never makes a summary is tested: a screen opened on a summary makes no call. The sample texts are made
/// up; no transcript or rollout is read.
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

    /// <summary>The app's shared styles and the window's resources, as App.xaml gives them. The window's XAML needs them when it is made.</summary>
    private static void EnsureAppResources()
    {
        if (Application.Current is not null) return;
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/OpenTail.Bullshark.App;component/Styles/Controls.xaml", UriKind.Absolute),
        });
    }

    private static T Named<T>(FrameworkElement window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static bool Checked(RadioButton radio) => radio.IsChecked == true;

    // ---- The mapping of a Continue mode to a starting mode (pure: no window). ----

    [Fact]
    public void EachContinueModeOpensTheSharedModeAndNoModeGivesNone()
    {
        Assert.Equal(ProviderResumeMode.Full, ProviderResumeRules.StartMode(ContinueMode.Full));
        Assert.Equal(ProviderResumeMode.LastTurns, ProviderResumeRules.StartMode(ContinueMode.LastTurns));
        Assert.Equal(ProviderResumeMode.Summary, ProviderResumeRules.StartMode(ContinueMode.Summary));
        Assert.Null(ProviderResumeRules.StartMode(null));
    }

    // ---- Claude's screen (CN-D9). With no starting mode the screen must look and behave as it did before C4. ----

    /// <summary>Claude's screen, with the given context size. Each call of the preview is recorded by its mode.</summary>
    private static ResumePrompt NewClaudePrompt(long contextTokens, List<ResumeMode> calls, ContinueMode? initialMode = null)
    {
        EnsureAppResources();
        return new ResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            contextTokens,
            RailPalette.DarkPalette,
            "opus",
            "high",
            (choice, kept, token) =>
            {
                calls.Add(choice.Mode);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Equal(
                "Recommended: in full. The context is about 10,000 tokens, small enough to re-read before the first reply.",
                Named<TextBlock>(prompt, "Recommendation").Text);
            Assert.Equal("In full: the whole conversation as context 👍 (recommended)", Named<RadioButton>(prompt, "FullChoice").Content);
            Assert.Equal("From a summary, then a new session", Named<RadioButton>(prompt, "SummaryChoice").Content);
            Assert.Equal(Visibility.Collapsed, Named<UIElement>(prompt, "LevelBlock").Visibility);
            Assert.Equal(Visibility.Collapsed, Named<UIElement>(prompt, "TurnsBlock").Visibility);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Equal(
                "Recommended: from a summary. The context is about 500,000 tokens; a full resume would re-read all of it before the first reply.",
                Named<TextBlock>(prompt, "Recommendation").Text);
            Assert.Equal("In full: the whole conversation as context", Named<RadioButton>(prompt, "FullChoice").Content);
            Assert.Equal("From a summary, then a new session 👍 (recommended)", Named<RadioButton>(prompt, "SummaryChoice").Content);
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "LevelBlock").Visibility);
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "TurnsBlock").Visibility);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeOpenedOnTheLastTurnsChecksTheTailRadioAndMakesNoCall()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeMode>();
            var prompt = NewClaudePrompt(500_000, calls, ContinueMode.LastTurns);

            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.True(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "TurnsBlock").Visibility);
            Assert.Equal(
                "No model call. The last turns are copied word for word into a new session. The new session runs at opus high.",
                Named<TextBlock>(prompt, "CostText").Text);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Equal(Visibility.Visible, Named<UIElement>(prompt, "LevelBlock").Visibility);
            Assert.Equal(ClaudeSummaryCost, Named<TextBlock>(prompt, "CostText").Text);
            Assert.Empty(calls);
        });
    }

    [Fact]
    public void ClaudeHasNoRememberTick()
    {
        // CN-D9 and the AFK decision of 2026-11-23: a new visible control would change Claude's screen, so there is none.
        UiThread.Run(() => Assert.Null(NewClaudePrompt(10_000, new List<ResumeMode>()).FindName("RememberBox")));
    }

    // ---- Codex's screen. ----

    private static CodexResumePrompt NewCodexPrompt(List<CodexResumeMode> calls, ContinueMode? initialMode = null)
    {
        EnsureAppResources();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) =>
            {
                calls.Add(choice.Mode);
                return Task.FromResult(new CodexPreview(SampleText, null));
            },
            models: null,
            initialMode: initialMode);
    }

    [Fact]
    public void CodexWithNoStartModeOpensOnTheLastTurnsAsBefore()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeMode>();
            var prompt = NewCodexPrompt(calls);

            Assert.True(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.Equal(new[] { CodexResumeMode.LastTurns }, calls);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.False(Named<TextBox>(prompt, "TurnsBox").IsEnabled);
        });
    }

    [Fact]
    public void CodexOpenedOnTheLastTurnsChecksTheLastTurnsRadioAndBuildsItsTextOnce()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeMode>();
            var prompt = NewCodexPrompt(calls, ContinueMode.LastTurns);

            Assert.True(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Equal(new[] { CodexResumeMode.LastTurns }, calls);
            Assert.Equal(SampleText, Named<TextBox>(prompt, "PreviewBox").Text);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
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
            Assert.Equal(CodexResumeMode.LastTurns, ticked.Choice.Mode);
        });
    }

    [Fact]
    public void CodexChoiceRecordDefaultsTheTickToOff()
    {
        Assert.False(new CodexResumeChoice(CodexResumeMode.LastTurns, null, 4).Remember);
    }

    // ---- Copilot's screen. ----

    private static CopilotResumePrompt NewCopilotPrompt(List<CopilotResumeMode> calls, ContinueMode? initialMode = null)
    {
        EnsureAppResources();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) =>
            {
                calls.Add(choice.Mode);
                return Task.FromResult(new CopilotPreview(SampleText, null));
            },
            initialMode);
    }

    [Fact]
    public void CopilotWithNoStartModeOpensOnTheLastTurnsAsBefore()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeMode>();
            var prompt = NewCopilotPrompt(calls);

            Assert.True(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "FullChoice")));
            Assert.False(Checked(Named<RadioButton>(prompt, "SummaryChoice")));
            Assert.Equal(new[] { CopilotResumeMode.LastTurns }, calls);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Empty(calls);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.False(Named<TextBox>(prompt, "TurnsBox").IsEnabled);
        });
    }

    [Fact]
    public void CopilotOpenedOnTheLastTurnsChecksTheLastTurnsRadioAndBuildsItsTextOnce()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeMode>();
            var prompt = NewCopilotPrompt(calls, ContinueMode.LastTurns);

            Assert.True(Checked(Named<RadioButton>(prompt, "TailChoice")));
            Assert.Equal(new[] { CopilotResumeMode.LastTurns }, calls);
            Assert.Equal(SampleText, Named<TextBox>(prompt, "PreviewBox").Text);
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
            Assert.False(Checked(Named<RadioButton>(prompt, "TailChoice")));
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
            Assert.Equal(CopilotResumeMode.LastTurns, ticked.Choice.Mode);
        });
    }

    [Fact]
    public void CopilotChoiceRecordDefaultsTheTickToOff()
    {
        Assert.False(new CopilotResumeChoice(CopilotResumeMode.LastTurns, null, 4).Remember);
    }
}

using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The Copilot resume screen (G3.1), headless: a fake preview callback stands in for the window's method, so the screen's own rules are what is tested.
/// The sample texts are made up; no transcript is read and no Copilot process starts. The callbacks return finished tasks, so each step completes at once.
/// A copy of the Codex resume screen's tests, without the model row (Copilot has no model choice in v1).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CopilotResumePromptTests
{
    private const string TailText = "you: an example message\ncopilot: an example reply";
    private const string SummaryText = "## Summary (normal)\nAn example summary of an example session.";

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

    private static CopilotResumePrompt NewPrompt(Func<CopilotResumeChoice, KeptSummary?, CancellationToken, Task<CopilotPreview>> preview)
    {
        EnsureAppResources();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            preview);
    }

    private static T Named<T>(CopilotResumePrompt prompt, string name) where T : class =>
        prompt.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static RadioButton LevelSegment(CopilotResumePrompt prompt, string tag) =>
        Named<Panel>(prompt, "LevelRow").Children.OfType<RadioButton>().First(radio => (string?)radio.Tag == tag);

    private static Task<CopilotPreview> TailPreview(CopilotResumeChoice choice, KeptSummary? kept, CancellationToken token) =>
        Task.FromResult(new CopilotPreview(TailText, null));

    [Fact]
    public void TheLastTurnsIsTheDefaultAndItsTextIsReadyWhenTheScreenOpens()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeChoice>();
            var prompt = NewPrompt((choice, kept, token) =>
            {
                calls.Add(choice);
                return TailPreview(choice, kept, token);
            });

            Assert.True(Named<RadioButton>(prompt, "TailChoice").IsChecked == true);
            var call = Assert.Single(calls);
            Assert.Equal(CopilotResumeMode.LastTurns, call.Mode);
            Assert.Equal(CopilotResumeRules.DefaultTurns, call.Turns);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.Equal(CopilotResumeRules.NoModelCallLine, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void TheModelRowIsNotThereSoTheScreenHasNoModelBox()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);

            Assert.Null(prompt.FindName("ModelBox"));
            Assert.Null(prompt.FindName("ModelBlock"));
        });
    }

    [Fact]
    public void InFullStartIsOnWithNoTextAndNothingIsRequested()
    {
        UiThread.Run(() =>
        {
            var calls = 0;
            var prompt = NewPrompt((choice, kept, token) =>
            {
                calls++;
                return TailPreview(choice, kept, token);
            });

            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;

            Assert.Equal(1, calls);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.False(Named<TextBox>(prompt, "TurnsBox").IsEnabled);
            Assert.Equal(CopilotResumeRules.FullCostLine, Named<TextBlock>(prompt, "CostText").Text);

            Click(Named<Button>(prompt, "StartButton"));
            Assert.NotNull(prompt.Choice);
            Assert.Equal(CopilotResumeMode.Full, prompt.Choice!.Mode);
            Assert.Null(prompt.PreviewText);
            Assert.Null(prompt.KeptSummary);
        });
    }

    [Fact]
    public void ASummaryIsMadeOnlyByTheMakeSummaryButton()
    {
        UiThread.Run(() =>
        {
            // A summary is made (the fake counts it) only when none is kept for the level. A request with a kept summary is a re-render, no model call.
            var made = new List<SummaryLevel>();
            var prompt = NewPrompt((choice, kept, token) =>
            {
                if (choice.Mode != CopilotResumeMode.Summary) return Task.FromResult(new CopilotPreview(TailText, null));
                if (kept is not null && kept.Level == choice.Level) return Task.FromResult(new CopilotPreview(SummaryText, null));
                made.Add(choice.Level ?? SummaryLevel.Normal);
                return Task.FromResult(new CopilotPreview(SummaryText, new KeptSummary(choice.Level ?? SummaryLevel.Normal, "An example summary.", Saved: true)));
            });

            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Assert.Empty(made);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.True(Named<Panel>(prompt, "LevelRow").IsEnabled);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(CopilotResumeRules.CostLine(CopilotResumeMode.Summary), Named<TextBlock>(prompt, "CostText").Text);

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Assert.Equal(new[] { SummaryLevel.Normal }, made);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(SummaryText, Named<TextBox>(prompt, "PreviewBox").Text);
        });
    }

    [Fact]
    public void StartSendsTheShownTextAndKeepsTheSummaryMade()
    {
        UiThread.Run(() =>
        {
            var summary = new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true);
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new CopilotPreview(SummaryText, summary)));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Click(Named<Button>(prompt, "StartButton"));

            Assert.NotNull(prompt.Choice);
            Assert.Equal(CopilotResumeMode.Summary, prompt.Choice!.Mode);
            Assert.Equal(SummaryLevel.Normal, prompt.Choice.Level);
            Assert.Equal(CopilotResumeRules.DefaultTurns, prompt.Choice.Turns);
            Assert.Equal(SummaryText, prompt.PreviewText);
            Assert.Equal(summary, prompt.KeptSummary);
        });
    }

    [Fact]
    public void ChangingTheLevelDropsTheSummaryAndTheText()
    {
        UiThread.Run(() =>
        {
            var calls = 0;
            var summary = new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true);
            var prompt = NewPrompt((choice, kept, token) =>
            {
                calls++;
                return Task.FromResult(new CopilotPreview(SummaryText, summary));
            });
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            var made = calls;

            LevelSegment(prompt, "Minimal").IsChecked = true;

            Assert.Equal(made, calls);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Null(prompt.KeptSummary);
        });
    }

    [Fact]
    public void ChangingTheNumberOfTurnsKeepsTheSummaryAndRebuildsTheText()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, token) =>
            {
                if (choice.Mode != CopilotResumeMode.Summary) return Task.FromResult(new CopilotPreview(TailText, null));
                if (kept is not null && kept.Level == choice.Level) return Task.FromResult(new CopilotPreview(SummaryText + "\n" + choice.Turns, null));
                made++;
                return Task.FromResult(new CopilotPreview(SummaryText, new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true)));
            });
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.Equal(1, made);

            Named<TextBox>(prompt, "TurnsBox").Text = "4";

            // The summary is kept (no new one is made); the text is built again with the new turns, and Start stays on.
            Assert.Equal(1, made);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.EndsWith("\n4", Named<TextBox>(prompt, "PreviewBox").Text);
        });
    }

    [Fact]
    public void ChangingTheModeKeepsASummaryMadeForTheSameLevel()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, token) =>
            {
                if (choice.Mode != CopilotResumeMode.Summary) return Task.FromResult(new CopilotPreview(TailText, null));
                if (kept is not null && kept.Level == choice.Level) return Task.FromResult(new CopilotPreview(SummaryText, null));
                made++;
                return Task.FromResult(new CopilotPreview(SummaryText, new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true)));
            });
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);

            Named<RadioButton>(prompt, "TailChoice").IsChecked = true;
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            // Back in summary mode the kept summary is used again: nothing is made, and Start is on with the summary's text.
            Assert.Equal(1, made);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(SummaryText, Named<TextBox>(prompt, "PreviewBox").Text);
        });
    }

    [Fact]
    public void ChangingTheLevelDropsTheSummaryMadeForTheOldLevel()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, token) => choice.Mode == CopilotResumeMode.Summary
                ? Task.FromResult(new CopilotPreview(SummaryText, new KeptSummary(choice.Level ?? SummaryLevel.Normal, "An example summary.", Saved: true)))
                : Task.FromResult(new CopilotPreview(TailText, null)));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);

            Named<Panel>(prompt, "LevelRow").Children.OfType<RadioButton>().Single(radio => Equals(radio.Tag, "Complete")).IsChecked = true;

            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
        });
    }

    [Fact]
    public void ASessionWithNoMessagesHasNoStartText()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new CopilotPreview(string.Empty, null)));

            Assert.Equal("This session has no messages to continue from, so Start is off.", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.Equal(string.Empty, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "CopyButton").IsEnabled);
        });
    }

    [Fact]
    public void AProblemTheWindowReportsIsShownAndStartStaysOff()
    {
        UiThread.Run(() =>
        {
            var problem = "The summary was not made: the summary failed (exit 1).";
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new CopilotPreview(string.Empty, null, problem)));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Assert.Equal(problem, Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void AFailureOfTheCallbackIsShownByItsTypeAndStartStaysOff()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, token) => Task.FromException<CopilotPreview>(new InvalidOperationException("example failure")));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            // The message of an unexpected failure can name a file or a folder, so the screen shows the type only.
            Assert.Equal("The start text could not be made (InvalidOperationException).", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.DoesNotContain("example failure", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void ATurnsBoxThatIsNotANumberSwitchesTheTextOff()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);

            Named<TextBox>(prompt, "TurnsBox").Text = "lots";

            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Contains("Enter a whole number from 0 to 200 for the turns.", Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void CancelChoosesNothing()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);

            Click(Named<Button>(prompt, "CancelButton"));

            Assert.Null(prompt.Choice);
            Assert.Null(prompt.PreviewText);
        });
    }
}

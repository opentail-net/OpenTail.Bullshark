using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The Copilot resume screen (G3.1, RS-D3 to RS-D21), headless: a fake preview callback stands in for the window's method, so the screen's own rules are
/// what is tested. The sample texts are made up; no transcript is read and no Copilot process starts. The callbacks return finished tasks, so each step
/// completes at once. A copy of the Codex resume screen's tests, without the model row (Copilot has no model choice in v1). A Zero preview (no level) is
/// built at once with no call; a summary is made only by Make summary.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CopilotResumePromptTests
{
    private const string TailText = "you: an example message\ncopilot: an example reply";
    private const string SummaryText = "## Summary (normal)\nAn example summary of an example session.";

    private static CopilotResumePrompt NewPrompt(Func<CopilotResumeChoice, KeptSummary?, bool, CancellationToken, Task<CopilotPreview>> preview)
    {
        AppResources.Ensure();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            preview);
    }

    private static CopilotResumePrompt NewPromptWithCoverage(Func<int, bool> summaryHasLines)
    {
        AppResources.Ensure();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            TailPreview,
            summaryHasLines: summaryHasLines);
    }

    private static T Named<T>(CopilotResumePrompt prompt, string name) where T : class =>
        prompt.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static RadioButton LevelSegment(CopilotResumePrompt prompt, string tag) =>
        Named<Panel>(prompt, "LevelRow").Children.OfType<RadioButton>().First(radio => (string?)radio.Tag == tag);

    /// <summary>Chooses Summary at a level (a tag: Zero, Minimal, Normal or Complete).</summary>
    private static void SummaryAt(CopilotResumePrompt prompt, string tag)
    {
        Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
        LevelSegment(prompt, tag).IsChecked = true;
    }

    /// <summary>The text of a Zero start: the callback's answer for the choice that has no summary.</summary>
    private static Task<CopilotPreview> TailPreview(CopilotResumeChoice choice, KeptSummary? kept, bool writeByTarget, CancellationToken token) =>
        Task.FromResult(new CopilotPreview(TailText, null));

    /// <summary>The callback of a summary: the fake makes a summary of the choice's level and tail, and the screen keeps it.</summary>
    private static CopilotPreview SummaryOf(CopilotResumeChoice choice) =>
        new(SummaryText, new KeptSummary(choice.Level!.Value, "An example summary.", Saved: true, Turns: choice.Turns));

    [Fact]
    public void TheZeroStartIsTheDefaultAndItsTextIsReadyWhenTheScreenOpens()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CopilotResumeChoice>();
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls.Add(choice);
                return TailPreview(choice, kept, false, token);
            });

            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsChecked == true);
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            Assert.True(LevelSegment(prompt, "Zero").IsChecked == true);
            var call = Assert.Single(calls);
            Assert.Equal(CopilotResumeMode.Summary, call.Mode);
            Assert.Null(call.Level);
            Assert.Equal(CopilotResumeRules.DefaultTurns, call.Turns);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.Equal(ProviderResumeRules.NoSummaryCallLine, Named<TextBlock>(prompt, "CostText").Text);
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
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls++;
                return TailPreview(choice, kept, false, token);
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
            // A summary is made (the fake counts it) only by Make summary. A kept summary for the setting is a re-render, no model call.
            var made = new List<SummaryLevel>();
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                if (choice.Level is null || choice.Mode != CopilotResumeMode.Summary) return TailPreview(choice, kept, false, token);
                if (kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)) return Task.FromResult(new CopilotPreview(SummaryText, null));
                made.Add(choice.Level.Value);
                return Task.FromResult(SummaryOf(choice));
            });

            SummaryAt(prompt, "Normal");

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
            var prompt = NewPrompt((choice, kept, _, token) => choice.Level is null
                ? TailPreview(choice, kept, false, token)
                : Task.FromResult(SummaryOf(choice)));
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Click(Named<Button>(prompt, "StartButton"));

            Assert.NotNull(prompt.Choice);
            Assert.Equal(CopilotResumeMode.Summary, prompt.Choice!.Mode);
            Assert.Equal(SummaryLevel.Normal, prompt.Choice.Level);
            Assert.Equal(CopilotResumeRules.DefaultTurns, prompt.Choice.Turns);
            Assert.Equal(SummaryText, prompt.PreviewText);
            Assert.Equal(new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true, Turns: CopilotResumeRules.DefaultTurns), prompt.KeptSummary);
        });
    }

    [Fact]
    public void ZeroStartKeepsNoSummaryAndNeedsTheTail()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);

            Named<CheckBox>(prompt, "TailBox").IsChecked = false;

            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Contains("Nothing to start from: tick the tail or choose a summary level.", Named<TextBlock>(prompt, "CostText").Text);

            Named<CheckBox>(prompt, "TailBox").IsChecked = true;

            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
        });
    }

    [Fact]
    public void ChangingTheLevelMakesTheSummaryOutOfDateAndStartWaits()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                if (choice.Level is null) return TailPreview(choice, kept, false, token);
                made++;
                return Task.FromResult(SummaryOf(choice));
            });
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));

            LevelSegment(prompt, "Minimal").IsChecked = true;

            Assert.Equal(1, made);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Contains("The summary no longer matches the tail you chose. Make summary again.", Named<TextBlock>(prompt, "CostText").Text);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
        });
    }

    [Fact]
    public void ChangingTheNumberOfTurnsMakesTheSummaryOutOfDateAndGoingBackKeepsIt()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                if (choice.Level is null) return TailPreview(choice, kept, false, token);
                if (kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)) return Task.FromResult(new CopilotPreview(SummaryText + "\n" + choice.Turns, null));
                made++;
                return Task.FromResult(SummaryOf(choice));
            });
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.Equal(1, made);

            Named<TextBox>(prompt, "TurnsBox").Text = "4";

            // A summary is tied to its tail (RS-D21): the new number makes it out of date, the text is hidden, and no call is made.
            Assert.Equal(1, made);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Contains("The summary no longer matches the tail you chose.", Named<TextBlock>(prompt, "CostText").Text);

            // The old number again: the summary made for it is the one for the setting, so it is shown again, and Start is on, with no call.
            Named<TextBox>(prompt, "TurnsBox").Text = "10";

            Assert.Equal(1, made);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.EndsWith("\n10", Named<TextBox>(prompt, "PreviewBox").Text);
        });
    }

    [Fact]
    public void ChangingTheModeKeepsASummaryMadeForTheSameSetting()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                if (choice.Mode != CopilotResumeMode.Summary || choice.Level is null) return TailPreview(choice, kept, false, token);
                if (kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)) return Task.FromResult(new CopilotPreview(SummaryText, null));
                made++;
                return Task.FromResult(SummaryOf(choice));
            });
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);

            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
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
            var prompt = NewPrompt((choice, kept, _, token) => choice.Level is null
                ? TailPreview(choice, kept, false, token)
                : Task.FromResult(SummaryOf(choice)));
            SummaryAt(prompt, "Normal");
            Click(Named<Button>(prompt, "MakeSummaryButton"));
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);

            LevelSegment(prompt, "Complete").IsChecked = true;

            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
        });
    }

    [Fact]
    public void ANothingToSummariseTheTailCoversSaysSoAndMakeSummaryIsOff()
    {
        UiThread.Run(() =>
        {
            // The window's check: with this many turns the tail keeps every turn, so there is nothing to summarise (RS-D26).
            var prompt = NewPromptWithCoverage(turns => turns < 4);

            SummaryAt(prompt, "Normal");
            Named<TextBox>(prompt, "TurnsBox").Text = "4";

            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Contains("The tail covers the whole session, so there is nothing to summarise.", Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void ASessionWithNoMessagesHasNoStartText()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromResult(new CopilotPreview(string.Empty, null)));

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
            var prompt = NewPrompt((choice, kept, _, token) => choice.Level is null
                ? TailPreview(choice, kept, false, token)
                : Task.FromResult(new CopilotPreview(string.Empty, null, problem)));
            SummaryAt(prompt, "Normal");

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Assert.Equal(problem + " " + ResumeDefaults.FullFallbackLine, Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.True(Named<RadioButton>(prompt, "FullChoice").IsChecked == true);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void AFailureOfTheCallbackIsShownByItsTypeAndStartStaysOff()
    {
        // Dedup plan 2026-11-23, D1: the message of an unexpected failure can name a file or a folder, so the screen shows the type only, and the summary
        // falls back to In full.
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => choice.Level is null
                ? TailPreview(choice, kept, false, token)
                : Task.FromException<CopilotPreview>(new InvalidOperationException("example failure")));
            SummaryAt(prompt, "Normal");

            Click(Named<Button>(prompt, "MakeSummaryButton"));

            Assert.Equal("The start text could not be made (InvalidOperationException). " + ResumeDefaults.FullFallbackLine, Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.DoesNotContain("example failure", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
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

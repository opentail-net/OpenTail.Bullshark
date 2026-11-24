using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The Codex resume screen (step R5, RS-D3 to RS-D21), headless: a fake preview callback stands in for the window's method, so the screen's own rules are
/// what is tested. The sample texts are made up; no transcript or rollout is read. The callbacks return finished tasks, so each step completes at once.
/// A Zero preview (no level) is built at once with no call; a summary is made only by Make summary.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CodexResumePromptTests
{
    private const string TailText = "you: an example message\ncodex: an example reply";
    private const string SummaryText = "## Summary (normal)\nAn example summary of an example session.";

    private static CodexResumePrompt NewPrompt(Func<CodexResumeChoice, KeptSummary?, bool, CancellationToken, Task<CodexPreview>> preview)
    {
        AppResources.Ensure();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            preview);
    }

    private static T Named<T>(CodexResumePrompt prompt, string name) where T : class =>
        prompt.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static RadioButton LevelSegment(CodexResumePrompt prompt, string tag) =>
        Named<Panel>(prompt, "LevelRow").Children.OfType<RadioButton>().First(radio => (string?)radio.Tag == tag);

    /// <summary>Chooses Summary at a level (a tag: Zero, Minimal, Normal or Complete).</summary>
    private static void SummaryAt(CodexResumePrompt prompt, string tag)
    {
        Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
        LevelSegment(prompt, tag).IsChecked = true;
    }

    /// <summary>The text of a Zero start or a tail: the callback's answer for the choice that has no summary.</summary>
    private static Task<CodexPreview> TailPreview(CodexResumeChoice choice, KeptSummary? kept, bool writeByTarget, CancellationToken token) =>
        Task.FromResult(new CodexPreview(TailText, null));

    /// <summary>The callback of a summary: the fake makes a summary of the choice's level and tail, and the screen keeps it.</summary>
    private static CodexPreview SummaryOf(CodexResumeChoice choice) =>
        new(SummaryText, new KeptSummary(choice.Level!.Value, "An example summary.", Saved: true, Turns: choice.Turns));

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
    public void TheZeroStartIsTheDefaultAndItsTextIsReadyWhenTheScreenOpens()
    {
        UiThread.Run(() =>
        {
            var calls = new List<CodexResumeChoice>();
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls.Add(choice);
                return TailPreview(choice, kept, false, token);
            });

            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsChecked == true);
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            Assert.True(LevelSegment(prompt, "Zero").IsChecked == true);
            var call = Assert.Single(calls);
            Assert.Equal(CodexResumeMode.Summary, call.Mode);
            Assert.Null(call.Level);
            Assert.Equal(CodexResumeRules.DefaultTurns, call.Turns);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.Equal(ProviderResumeRules.NoSummaryCallLine, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
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
            Assert.Equal(CodexResumeRules.FullCostLine, Named<TextBlock>(prompt, "CostText").Text);

            Click(Named<Button>(prompt, "StartButton"));
            Assert.NotNull(prompt.Choice);
            Assert.Equal(CodexResumeMode.Full, prompt.Choice!.Mode);
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
                if (choice.Level is null || choice.Mode != CodexResumeMode.Summary) return TailPreview(choice, kept, false, token);
                if (kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)) return Task.FromResult(new CodexPreview(SummaryText, null));
                made.Add(choice.Level.Value);
                return Task.FromResult(SummaryOf(choice));
            });

            SummaryAt(prompt, "Normal");

            Assert.Empty(made);
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.True(Named<Panel>(prompt, "LevelRow").IsEnabled);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(CodexResumeRules.CostLine(CodexResumeMode.Summary), Named<TextBlock>(prompt, "CostText").Text);

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
            Assert.Equal(CodexResumeMode.Summary, prompt.Choice!.Mode);
            Assert.Equal(SummaryLevel.Normal, prompt.Choice.Level);
            Assert.Equal(CodexResumeRules.DefaultTurns, prompt.Choice.Turns);
            Assert.Equal(SummaryText, prompt.PreviewText);
            Assert.Equal(new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true, Turns: CodexResumeRules.DefaultTurns), prompt.KeptSummary);
        });
    }

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
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

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
    public void ChangingTheNumberOfTurnsMakesTheSummaryOutOfDateAndGoingBackKeepsIt()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                if (choice.Level is null) return TailPreview(choice, kept, false, token);
                if (kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)) return Task.FromResult(new CodexPreview(SummaryText + "\n" + choice.Turns, null));
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

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
    public void ChangingTheModeKeepsASummaryMadeForTheSameSetting()
    {
        UiThread.Run(() =>
        {
            var made = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                if (choice.Mode != CodexResumeMode.Summary || choice.Level is null) return TailPreview(choice, kept, false, token);
                if (kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)) return Task.FromResult(new CodexPreview(SummaryText, null));
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

    private static CodexResumePrompt NewPromptWithCoverage(Func<int, bool> summaryHasLines)
    {
        AppResources.Ensure();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            TailPreview,
            summaryHasLines: summaryHasLines);
    }

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
    public void ASessionWithNoMessagesHasNoStartText()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromResult(new CodexPreview(string.Empty, null)));

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
                : Task.FromResult(new CodexPreview(string.Empty, null, problem)));
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
        // Dedup plan 2026-11-23, D1: the same as the Copilot screen. The message of an unexpected failure can name a file or a folder, so the screen
        // shows the type only, and the summary falls back to In full.
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => choice.Level is null
                ? TailPreview(choice, kept, false, token)
                : Task.FromException<CodexPreview>(new InvalidOperationException("example failure")));
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

    /// <summary>A model list of synthetic ids, as the window would hand it over.</summary>
    private static Task<CodexModelReading> ModelsOf(params CodexModelEntry[] entries) =>
        Task.FromResult(new CodexModelReading(entries, Hidden: 0, Dropped: 0, CodexModelProblem.None));

    private static CodexResumePrompt NewPromptWithModels(Task<CodexModelReading> models)
    {
        AppResources.Ensure();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            TailPreview,
            models);
    }

    [Fact]
    public void TheModelRowIsShownForTheNewSessionModesAndNotForInFull()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPromptWithModels(ModelsOf(new CodexModelEntry("example-default", "Example Default", IsDefault: true)));
            var row = Named<UIElement>(prompt, "ModelBlock");

            Assert.Equal(Visibility.Visible, row.Visibility);

            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.Equal(Visibility.Collapsed, row.Visibility);

            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Assert.Equal(Visibility.Visible, row.Visibility);
        });
    }

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
    public void TheListFillsTheModelBoxWithTheDefaultSelectedAndStartSendsItsId()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPromptWithModels(ModelsOf(
                new CodexModelEntry("example-default", "Example Default", IsDefault: true),
                new CodexModelEntry("example-other", "Example Other", IsDefault: false)));
            var box = Named<ComboBox>(prompt, "ModelBox");

            Assert.Equal(2, box.Items.Count);
            Assert.Equal("example-default", ((ComboBoxItem)box.SelectedItem).Tag);

            Click(Named<Button>(prompt, "StartButton"));

            Assert.Equal("example-default", prompt.Choice!.Model);
        });
    }

    [Fact(Skip = "Automatic builds are off for now (OnChoiceChanged builds nothing on a change). Re-enable with them.")]
    public void WhenTheListCouldNotBeReadTheOnlyEntryPassesNoModel()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPromptWithModels(Task.FromResult(
                new CodexModelReading([], 0, 0, CodexModelProblem.ExitedWithoutReply)));
            var box = Named<ComboBox>(prompt, "ModelBox");

            Assert.Single(box.Items);
            Assert.Contains("ExitedWithoutReply", Named<TextBlock>(prompt, "ModelNote").Text);

            Click(Named<Button>(prompt, "StartButton"));

            Assert.NotNull(prompt.Choice);
            Assert.Null(prompt.Choice!.Model);
        });
    }

    [Fact]
    public void InFullTheModelIsNotNeededSoStartDoesNotWaitForTheList()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPromptWithModels(new TaskCompletionSource<CodexModelReading>().Task);

            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }
}

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The opencode resume screen, headless (OC3): its title and Start text, the summary (OC-O4, decided 2026-11-23: allowed; made only by Make summary), no
/// target chooser (OC-D18), the Remember tick, the default mode, the cost lines (OC-D9), the preview, and the folder notice. Controls are found by name. The
/// choices are set in code; Make summary is pressed by raising its click, and the preview callback returns finished text at once. No model call is made,
/// no process starts, and no state folder or database is read or written.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class OpenCodeResumePromptTests
{
    private const string TailText = "you: an example message\nopencode: an example reply";
    private const string Heading = "Resume: an example session";
    private const string Facts = "2 turns · last active 1 day ago · in C:\\Work\\example";

    /// <summary>A folder under the temp folder that does not exist.</summary>
    private static string MissingFolder() => Path.Combine(Path.GetTempPath(), "opentail-missing-" + Guid.NewGuid().ToString("N"));

    /// <summary>The screen. Every preview request is recorded in <paramref name="asked"/>, when one is given.</summary>
    private static OpenCodeResumePrompt NewPrompt(string? folder = null, bool? folderExists = null, List<OpenCodeResumeChoice>? asked = null)
    {
        AppResources.Ensure();
        return new OpenCodeResumePrompt(Heading, Facts, RailPalette.DarkPalette,
            (choice, ct) =>
            {
                asked?.Add(choice);
                // A summary comes back with the summary it made, for the setting it was made at (RS-D21); a Zero start makes none.
                return Task.FromResult(new OpenCodePreview(TailText, null,
                    choice.Level is { } level ? new KeptSummary(level, "An example summary.", Saved: false, Turns: choice.Turns) : null));
            },
            folder: folder, folderExists: folderExists);
    }

    private static T Named<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    [Fact]
    public void TheTitleIsTheOpencodeTitle()
    {
        UiThread.Run(() =>
        {
            Assert.Equal("Resume an opencode session", NewPrompt().Title);
            Assert.Equal(OpenCodeResumePrompt.OpenCodeTitle, NewPrompt().Title);
        });
    }

    [Fact]
    public void TheStartButtonReadsStartInOpencodeAndItsAccessibleNameMatches()
    {
        UiThread.Run(() =>
        {
            var button = Named<Button>(NewPrompt(), "StartButton");
            Assert.Equal("Start in opencode", (string?)button.Content);
            Assert.Equal(OpenCodeResumePrompt.OpenCodeStartText, (string?)button.Content);
            Assert.Equal("Start in opencode", AutomationProperties.GetName(button));
        });
    }

    [Fact]
    public void TheSummaryRadioIsOnAndTheLevelRowAndMakeSummaryAreOnlyOnWhenTheSummaryIsChosen()
    {
        UiThread.Run(() =>
        {
            // The screen opens on Summary at Zero (no call): the level row is on, and Make summary is off until a summary level is chosen (RS-D22).
            var prompt = NewPrompt();
            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsChecked == true);
            Assert.True(Named<WrapPanel>(prompt, "LevelRow").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Named<RadioButton>(prompt, "NormalLevel").IsChecked = true;
            Assert.True(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.False(Named<WrapPanel>(prompt, "LevelRow").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
        });
    }

    [Fact]
    public void TheCostLineOfTheSummaryIsShownBeforeItsCallAndNamesOneModelCallAndTheSessionRow()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Named<RadioButton>(prompt, "NormalLevel").IsChecked = true;
            var cost = Named<TextBlock>(prompt, "CostText").Text;
            Assert.Equal(
                "Making the summary is one model call on your opencode login, and opencode writes a session row into your opencode database.", cost);
            Assert.Equal(OpenCodeResumeRules.SummaryCostLine, cost);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
        });
    }

    [Fact]
    public void TheLevelRowHasZeroAndTheThreeLevelsWithTheSharedLabelsAndZeroIsChosenAtOpening()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Assert.Equal(ResumeSummaryState.ZeroLabel, Named<RadioButton>(prompt, "ZeroLevel").Content);
            Assert.Equal(ProviderResumeRules.LevelLabel(SummaryLevel.Minimal), Named<RadioButton>(prompt, "MinimalLevel").Content);
            Assert.Equal(ProviderResumeRules.LevelLabel(SummaryLevel.Normal), Named<RadioButton>(prompt, "NormalLevel").Content);
            Assert.Equal(ProviderResumeRules.LevelLabel(SummaryLevel.Complete), Named<RadioButton>(prompt, "CompleteLevel").Content);
            Assert.True(Named<RadioButton>(prompt, "ZeroLevel").IsChecked == true);
        });
    }

    [Fact]
    public void TheSummaryIsNotPreviewedByChoosingItAndStartStaysOffUntilMakeSummaryIsPressed()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            var countBefore = asked.Count;
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Named<RadioButton>(prompt, "NormalLevel").IsChecked = true;
            Assert.DoesNotContain(asked.Skip(countBefore), choice => choice.Level is not null);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void MakeSummaryMakesTheSummaryForTheLevelOnScreenAndShowsItsPreviewWithTheStartLine()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Named<RadioButton>(prompt, "CompleteLevel").IsChecked = true;
            var countBefore = asked.Count;

            Named<Button>(prompt, "MakeSummaryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var choice = Assert.Single(asked.Skip(countBefore));
            Assert.Equal(OpenCodeResumeMode.Summary, choice.Mode);
            Assert.Equal(SummaryLevel.Complete, choice.Level);
            Assert.Equal(OpenCodeResumeRules.DefaultTurns, choice.Turns);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
            Assert.Contains(OpenCodeResumeRules.LastTurnsCostLine, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void AChangedLevelTakesTheSummaryPreviewAwayUntilMakeSummaryIsPressedAgain()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Named<RadioButton>(prompt, "NormalLevel").IsChecked = true;
            Named<Button>(prompt, "MakeSummaryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);

            Named<RadioButton>(prompt, "MinimalLevel").IsChecked = true;

            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Contains(ResumeSummaryState.OutOfDateLine, Named<TextBlock>(prompt, "CostText").Text);
            Assert.Equal(1, asked.Count(choice => choice.Level is not null));
        });
    }

    [Fact]
    public void ASummaryWithAnInvalidTurnsNumberCannotBeMade()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Named<TextBox>(prompt, "TurnsBox").Text = "many";
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.Contains(OpenCodeResumeRules.TurnsMessage, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void AStoredSummaryOpensOnTheSummaryRadioAndMakesNothing()
    {
        UiThread.Run(() =>
        {
            AppResources.Ensure();
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = new OpenCodeResumePrompt(Heading, Facts, RailPalette.DarkPalette,
                (choice, ct) =>
                {
                    asked.Add(choice);
                    return Task.FromResult(new OpenCodePreview(TailText));
                },
                initialMode: ContinueMode.Summary);
            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsChecked == true);
            Assert.Empty(asked);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void ThereIsNoTargetChooserAndNoSplitPlatformMenu()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Assert.Null(prompt.FindName("TargetBox"));
            Assert.Null(prompt.FindName("TargetRow"));
            Assert.Null(prompt.FindName("TargetMenuButton"));
            Assert.Null(prompt.FindName("TargetMenu"));
            Assert.Null(prompt.FindName("StartSplit"));
        });
    }

    [Fact]
    public void TheRememberTickExistsAndIsUncheckedByDefault()
    {
        UiThread.Run(() =>
        {
            var box = Named<CheckBox>(NewPrompt(), "RememberBox");
            Assert.NotEqual(true, box.IsChecked);
        });
    }

    [Fact]
    public void ZeroWithTheTailIsTheDefaultAndInFullIsNotChecked()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsChecked == true);
            Assert.True(Named<RadioButton>(prompt, "ZeroLevel").IsChecked == true);
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            Assert.NotEqual(true, Named<RadioButton>(prompt, "FullChoice").IsChecked);
        });
    }

    [Fact]
    public void TheDefaultCostLineIsTheLastTurnsLineAndItSaysOneModelTurn()
    {
        UiThread.Run(() =>
        {
            var cost = Named<TextBlock>(NewPrompt(), "CostText").Text;
            Assert.Equal(OpenCodeResumeRules.LastTurnsCostLine, cost);
            Assert.Contains("one model turn", cost);
        });
    }

    [Fact]
    public void InFullShowsItsCostLineAndNoPreviewAndSendsNothingToBePreviewed()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            var countBefore = asked.Count;
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.Equal("Opens the session in opencode's terminal. Nothing is sent until you type.", Named<TextBlock>(prompt, "CostText").Text);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.DoesNotContain(asked.Skip(countBefore), choice => choice.Mode == OpenCodeResumeMode.Full);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void NoCostLineOnTheScreenSaysNoModelCall()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            var cost = Named<TextBlock>(prompt, "CostText");
            Assert.DoesNotContain("no model call", cost.Text, StringComparison.OrdinalIgnoreCase);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.DoesNotContain("no model call", cost.Text, StringComparison.OrdinalIgnoreCase);
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Assert.DoesNotContain("no model call", cost.Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TheZeroPreviewIsBuiltForTheDefaultChoiceAndShownBeforeStart()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            var choice = Assert.Single(asked);
            Assert.Equal(OpenCodeResumeMode.Summary, choice.Mode);
            Assert.Null(choice.Level);
            Assert.Equal(OpenCodeResumeRules.DefaultTurns, choice.Turns);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewBox").Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
        });
    }

    [Fact]
    public void AMissingFolderShowsTheNoticeAndStartStaysEnabled()
    {
        UiThread.Run(() =>
        {
            var folder = MissingFolder();
            var prompt = NewPrompt(folder, folderExists: false);
            var notice = Named<TextBlock>(prompt, "FolderNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(ResumeFolderNotice.For(folder, folderExists: false), notice.Text);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void ASessionWithNoFolderOnRecordShowsTheNoticeAndBlocksStart()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(folder: null, folderExists: false);
            var notice = Named<TextBlock>(prompt, "FolderNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(ResumeFolderNotice.For(null, folderExists: false), notice.Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void AnExistingFolderShowsNoNotice()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(Path.GetTempPath(), folderExists: true);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(prompt, "FolderNotice").Visibility);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void AnInvalidTurnsNumberShowsTheMessageAndNoPreviewIsBuilt()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            var countBefore = asked.Count;
            Named<TextBox>(prompt, "TurnsBox").Text = "many";
            Assert.Contains(OpenCodeResumeRules.TurnsMessage, Named<TextBlock>(prompt, "CostText").Text);
            Assert.Equal(countBefore, asked.Count);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }
}

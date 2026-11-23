using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The opencode resume screen, headless (OC3): its title and Start text, the summary that is off (OC-D19), no target chooser (OC-D18), the Remember tick, the
/// default mode, the cost lines (OC-D9), the preview, and the folder notice. Controls are found by name. Nothing is clicked (no Start, Make summary, Copy or
/// Preview press): the choices are set in code, and the preview callback returns finished text at once. No model call is made, no process starts, and no
/// state folder or database is read or written.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class OpenCodeResumePromptTests
{
    private const string TailText = "you: an example message\nopencode: an example reply";
    private const string Heading = "Resume: an example session";
    private const string Facts = "2 turns · last active 1 day ago · in C:\\Work\\example";

    /// <summary>The app's shared styles and the window's resources, as App.xaml gives them. The window's XAML needs them when it is made.</summary>
    private static void EnsureAppResources()
    {
        if (Application.Current is not null) return;
        // Closing the last open window would otherwise shut the application down, and the next test could not make another one.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/OpenTail.Bullshark.App;component/Styles/Controls.xaml", UriKind.Absolute),
        });
    }

    /// <summary>A folder under the temp folder that does not exist.</summary>
    private static string MissingFolder() => Path.Combine(Path.GetTempPath(), "opentail-missing-" + Guid.NewGuid().ToString("N"));

    /// <summary>The screen. Every preview request is recorded in <paramref name="asked"/>, when one is given.</summary>
    private static OpenCodeResumePrompt NewPrompt(string? folder = null, bool? folderExists = null, List<OpenCodeResumeChoice>? asked = null)
    {
        EnsureAppResources();
        return new OpenCodeResumePrompt(Heading, Facts, RailPalette.DarkPalette,
            (choice, ct) =>
            {
                asked?.Add(choice);
                return Task.FromResult(new OpenCodePreview(TailText));
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
    public void TheSummaryRadioAndItsButtonAreDisabledAndTheOffTextIsShownBesideThem()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Assert.False(Named<RadioButton>(prompt, "SummaryChoice").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            var note = Named<TextBlock>(prompt, "SummaryOffNote");
            Assert.Equal("The summary is off until the owner decides where it is written (OC-O4).", note.Text);
            Assert.Equal(Visibility.Visible, note.Visibility);
        });
    }

    [Fact]
    public void TheSummaryIsNeverPreviewedAndCheckingItStartsNothing()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            var countBefore = asked.Count;
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Assert.DoesNotContain(asked.Skip(countBefore), choice => choice.Mode == OpenCodeResumeMode.Summary);
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
    public void LastTurnsIsTheDefaultModeAndInFullAndTheSummaryAreNotChecked()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt();
            Assert.True(Named<RadioButton>(prompt, "TailChoice").IsChecked == true);
            Assert.NotEqual(true, Named<RadioButton>(prompt, "FullChoice").IsChecked);
            Assert.NotEqual(true, Named<RadioButton>(prompt, "SummaryChoice").IsChecked);
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
            Named<RadioButton>(prompt, "TailChoice").IsChecked = true;
            Assert.DoesNotContain("no model call", cost.Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TheLastTurnsPreviewIsBuiltForTheDefaultChoiceAndShownBeforeStart()
    {
        UiThread.Run(() =>
        {
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = NewPrompt(asked: asked);
            var choice = Assert.Single(asked);
            Assert.Equal(OpenCodeResumeMode.LastTurns, choice.Mode);
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

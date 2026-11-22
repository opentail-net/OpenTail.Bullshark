using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The layout of the three resume windows (owner, 2026-11-22): each can be resized, is capped to the work area of the screen, and keeps its Start button
/// outside the scrolling body, so the button is always in view. Also the Codex screen with a missing folder: the folder is made again at Start, so the
/// summary and Start stay on. Headless. No Start or Make summary is clicked, no preview is pressed, and no CLI or model runs.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ResumeScreenLayoutTests
{
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

    private static T Named<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    /// <summary>True when the element sits inside a ScrollViewer, by the logical tree (the windows' controls are logical children).</summary>
    private static bool InsideScrollViewer(DependencyObject element)
    {
        for (var node = LogicalTreeHelper.GetParent(element); node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer) return true;
        }
        return false;
    }

    [Fact]
    public void TheClaudeWindowCanBeResizedIsCappedToTheWorkAreaAndKeepsItsButtonOutOfTheScroll()
    {
        UiThread.Run(() =>
        {
            EnsureAppResources();
            var prompt = new ResumePrompt(Heading, Facts, 500_000, RailPalette.DarkPalette, "opus", "high");

            Assert.Equal(ResizeMode.CanResize, prompt.ResizeMode);
            Assert.True(prompt.MaxHeight > 0);
            Assert.False(InsideScrollViewer(Named<Button>(prompt, "ResumeButton")));
            Assert.True(InsideScrollViewer(Named<TextBlock>(prompt, "Heading")));
        });
    }

    [Fact]
    public void TheCodexWindowCanBeResizedIsCappedToTheWorkAreaAndKeepsItsButtonOutOfTheScroll()
    {
        UiThread.Run(() =>
        {
            EnsureAppResources();
            var prompt = new CodexResumePrompt(Heading, Facts, RailPalette.DarkPalette);

            Assert.Equal(ResizeMode.CanResize, prompt.ResizeMode);
            Assert.True(prompt.MaxHeight > 0);
            Assert.False(InsideScrollViewer(Named<Button>(prompt, "StartButton")));
            Assert.True(InsideScrollViewer(Named<TextBlock>(prompt, "Heading")));
        });
    }

    [Fact]
    public void TheCopilotWindowCanBeResizedIsCappedToTheWorkAreaAndKeepsItsButtonOutOfTheScroll()
    {
        UiThread.Run(() =>
        {
            EnsureAppResources();
            var prompt = new CopilotResumePrompt(Heading, Facts, RailPalette.DarkPalette);

            Assert.Equal(ResizeMode.CanResize, prompt.ResizeMode);
            Assert.True(prompt.MaxHeight > 0);
            Assert.False(InsideScrollViewer(Named<Button>(prompt, "StartButton")));
            Assert.True(InsideScrollViewer(Named<TextBlock>(prompt, "Heading")));
        });
    }

    [Fact]
    public void ACodexMissingFolderShowsItsNoticeAndTheSummaryAndStartStayOn()
    {
        UiThread.Run(() =>
        {
            EnsureAppResources();
            // In full is opened on, so no preview is built at all: the callback is null and nothing runs.
            var folder = Path.Combine(Path.GetTempPath(), "opentail-missing-" + Guid.NewGuid().ToString("N"));
            var prompt = new CodexResumePrompt(Heading, Facts, RailPalette.DarkPalette, previewAsync: null,
                initialMode: ContinueMode.Full, folder: folder, folderExists: false);

            Assert.Contains("Start will make it again before it starts.", Named<TextBlock>(prompt, "FolderNotice").Text);
            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsEnabled);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void ACodexSessionWithNoFolderOnRecordStillBlocksStart()
    {
        UiThread.Run(() =>
        {
            EnsureAppResources();
            var prompt = new CodexResumePrompt(Heading, Facts, RailPalette.DarkPalette, previewAsync: null,
                initialMode: ContinueMode.Full, folder: null, folderExists: false);

            Assert.Contains("Start cannot make one", Named<TextBlock>(prompt, "FolderNotice").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }
}

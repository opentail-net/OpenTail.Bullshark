using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            AppResources.Ensure();
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
            AppResources.Ensure();
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
            AppResources.Ensure();
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
            AppResources.Ensure();
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
            AppResources.Ensure();
            var prompt = new CodexResumePrompt(Heading, Facts, RailPalette.DarkPalette, previewAsync: null,
                initialMode: ContinueMode.Full, folder: null, folderExists: false);

            Assert.Contains("Start cannot make one", Named<TextBlock>(prompt, "FolderNotice").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void TheStartButtonsAndTheirSplitHaveNoFixedWidthAndTheCodexAndCopilotMenusOpenUpward()
    {
        UiThread.Run(() =>
        {
            AppResources.Ensure();
            var claude = new ResumePrompt(Heading, Facts, 500_000, RailPalette.DarkPalette, "opus", "high");
            var codex = new CodexResumePrompt(Heading, Facts, RailPalette.DarkPalette);
            var copilot = new CopilotResumePrompt(Heading, Facts, RailPalette.DarkPalette);

            // No fixed width: the buttons are as wide as their labels, so the Start label is not clipped by the part beside it.
            Assert.True(double.IsNaN(Named<Button>(claude, "ResumeButton").Width));
            Assert.True(double.IsNaN(Named<Button>(claude, "ResumeMenuButton").Width));
            Assert.True(double.IsNaN(Named<Button>(codex, "StartButton").Width));
            Assert.True(double.IsNaN(Named<StackPanel>(codex, "StartSplit").Width));
            Assert.True(double.IsNaN(Named<Button>(copilot, "StartButton").Width));
            Assert.True(double.IsNaN(Named<StackPanel>(copilot, "StartSplit").Width));

            // The platform menu of the split button opens upward, above the Start row that is pinned at the bottom.
            Assert.Equal(PlacementMode.Top, Named<ContextMenu>(codex, "TargetMenu").Placement);
            Assert.Equal(PlacementMode.Top, Named<ContextMenu>(copilot, "TargetMenu").Placement);
        });
    }
}

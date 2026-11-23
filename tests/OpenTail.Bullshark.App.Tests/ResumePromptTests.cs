using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTail.Bullshark.Core.Resume;
using Xunit.Abstractions;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The resume prompt's preview and its Summary mode (RS-D3 to RS-D26), headless: a fake preview stands in for the window's method, so the dialog's own rules
/// are what is tested. The sample texts are made up for the tests; no transcript is read. Zero (no level) is built at once with no call; a summary is made
/// only by Make summary, and a summary made for another tail or level is out of date (RS-D21).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ResumePromptTests
{
    private const string TailText =
        "Continues session: 00000000-0000-0000-0000-000000000000\n" +
        "This session continues an earlier Claude Code session that had gone cold. What follows is not the full conversation.\n\n" +
        "## The last 2 turns, word for word (tool calls and results on one line each)\n" +
        "you: an example message\nclaude: an example reply\n\nCarry on from this.";

    private const string SummaryText =
        "Continues session: 00000000-0000-0000-0000-000000000000\n\n" +
        "## Summary (normal)\nAn example summary of an example session.\n\nCarry on from this.";

    private readonly ITestOutputHelper _output;

    public ResumePromptTests(ITestOutputHelper output) => _output = output;

    /// <summary>A prompt for a large context (a summary is the recommendation, at the Normal level), with the given preview. Made on the WPF thread.</summary>
    private static ResumePrompt NewPrompt(
        Func<ResumeChoice, KeptSummary?, bool, CancellationToken, Task<ResumePromptPreview>> preview, Func<int, bool>? summaryHasLines = null)
    {
        AppResources.Ensure();
        return new ResumePrompt(
            "Resume: a test session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            contextTokens: 500_000,
            RailPalette.DarkPalette,
            "opus",
            "high",
            preview,
            summaryHasLines: summaryHasLines);
    }

    private static T Named<T>(ResumePrompt prompt, string name) where T : class =>
        prompt.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static RadioButton LevelSegment(ResumePrompt prompt, string tag) =>
        Named<Panel>(prompt, "LevelRow").Children.OfType<RadioButton>().First(radio => (string?)radio.Tag == tag);

    /// <summary>Chooses Summary at a level (a tag: Zero, Minimal, Normal or Complete).</summary>
    private static void SummaryAt(ResumePrompt prompt, string tag)
    {
        Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
        LevelSegment(prompt, tag).IsChecked = true;
    }

    /// <summary>Presses the Make summary button, the way a click does.</summary>
    private static void MakeSummary(ResumePrompt prompt) =>
        Named<Button>(prompt, "MakeSummaryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task<ResumePromptPreview> TailPreview(ResumeChoice choice, KeptSummary? kept, bool writeByTarget, CancellationToken token) =>
        Task.FromResult(new ResumePromptPreview(new ResumePreview(TailText, 0, true, null)));

    /// <summary>The summary the fake makes for a choice: the level and the tail the choice holds.</summary>
    private static ResumePromptPreview SummaryOf(ResumeChoice choice) =>
        new(new ResumePreview(SummaryText, 0, true, new KeptSummary(choice.Level!.Value, "An example summary.", Saved: true, Turns: choice.Turns)));

    [Fact]
    public void AFullResumeHasNoPreviewAndNoCopy()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;

            Assert.False(Named<MenuItem>(prompt, "CopyItem").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
        });
    }

    [Fact]
    public void TheZeroStartIsBuiltAtOnceAndAMinimalSummaryOnlyByMakeSummary()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeChoice>();
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls.Add(choice);
                return choice.Level is null ? TailPreview(choice, kept, false, token) : Task.FromResult(SummaryOf(choice));
            });

            SummaryAt(prompt, "Zero");

            // Zero is built at once: its text is shown, Start is on, and no summary is called.
            var zero = Assert.Single(calls);
            Assert.Null(zero.Level);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewText").Text);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.True(Named<MenuItem>(prompt, "CopyItem").IsEnabled);
            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);

            LevelSegment(prompt, "Minimal").IsChecked = true;
            Assert.Single(calls);
            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);

            MakeSummary(prompt);
            Assert.Equal(2, calls.Count);
            Assert.Equal(SummaryLevel.Minimal, calls[1].Level);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
        });
    }

    [Fact]
    public void AZeroStartWithTheTailOffIsRefusedWithItsStatusLine()
    {
        UiThread.Run(() =>
        {
            var calls = new List<ResumeChoice>();
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls.Add(choice);
                return TailPreview(choice, kept, false, token);
            });
            SummaryAt(prompt, "Zero");

            Named<CheckBox>(prompt, "TailBox").IsChecked = false;

            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Contains("Nothing to start from: tick the tail or choose a summary level.", Named<TextBlock>(prompt, "CostText").Text);
            Assert.Single(calls);
        });
    }

    [Fact]
    public void AnInvalidNumberOfTurnsSwitchesThePreviewOff()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            Named<TextBox>(prompt, "TurnsBox").Text = "lots";

            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.False(Named<MenuItem>(prompt, "CopyItem").IsEnabled);
            Assert.Contains("Enter a whole number from 0 to 200 for the turns.", Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void AZeroPreviewShowsTheTextAndItsSizeLine()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            SummaryAt(prompt, "Zero");

            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Equal(TailText, Named<TextBox>(prompt, "PreviewText").Text);
            Assert.Equal(ResumePreviewText.Header(TailText.Length, 0, true), Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.True(Named<TextBox>(prompt, "PreviewText").IsReadOnly);
            Assert.Null(prompt.KeptSummary);
        });
    }

    // No test presses "Copy the prompt": it writes the real Windows clipboard, and a test run must not replace what the owner copied.
    // Copy is a live-run item (docs/plans/live-run-checklist.md, Resume preview).

    [Fact]
    public void ASummaryIsKeptAndStartReusesItWithoutASecondCall()
    {
        UiThread.Run(() =>
        {
            var seen = new List<KeptSummary?>();
            var calls = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls++;
                seen.Add(kept);
                // The window's method reuses a kept summary of the same level and tail: the fake does the same, and makes one only otherwise.
                return kept is not null && kept.MatchesChoice(choice.Level, choice.Turns, kept.Lines)
                    ? Task.FromResult(new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, kept)))
                    : Task.FromResult(SummaryOf(choice));
            });
            SummaryAt(prompt, "Normal");
            Assert.Equal(0, calls);

            MakeSummary(prompt);

            Assert.Equal(1, calls);
            Assert.NotNull(prompt.KeptSummary);
            Assert.Equal(SummaryLevel.Normal, prompt.KeptSummary!.Level);
            Assert.Equal(10, prompt.KeptSummary.Turns);

            Named<Button>(prompt, "ResumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(prompt.Choice);
            Assert.Equal(ResumeMode.Summary, prompt.Choice!.Mode);
            Assert.Equal(SummaryLevel.Normal, prompt.Choice.Level);
            Assert.Equal(10, prompt.Choice.Turns);
            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public void AChangedLevelHidesThePreviewAndMakesTheSummaryOutOfDate()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromResult(SummaryOf(choice)));
            SummaryAt(prompt, "Normal");
            MakeSummary(prompt);
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);

            LevelSegment(prompt, "Minimal").IsChecked = true;

            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Contains(ResumeSummaryState.OutOfDateLine, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void AChangedNumberOfTurnsHidesThePreviewAndMakesTheSummaryOutOfDate()
    {
        UiThread.Run(() =>
        {
            var calls = 0;
            var prompt = NewPrompt((choice, kept, _, token) =>
            {
                calls++;
                return Task.FromResult(SummaryOf(choice));
            });
            SummaryAt(prompt, "Normal");
            MakeSummary(prompt);

            Named<TextBox>(prompt, "TurnsBox").Text = "4";

            Assert.Equal(1, calls);
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Contains(ResumeSummaryState.OutOfDateLine, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void ANothingToSummariseTheTailCoversIsSaidAndMakeSummaryIsOff()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview, summaryHasLines: turns => turns < 4);
            SummaryAt(prompt, "Normal");
            Named<TextBox>(prompt, "TurnsBox").Text = "4";

            Assert.False(Named<Button>(prompt, "MakeSummaryButton").IsEnabled);
            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Contains(ResumeSummaryState.NothingToSummariseLine, Named<TextBlock>(prompt, "CostText").Text);
        });
    }

    [Fact]
    public void AFailedPreviewShowsItsMessageAndTheDialogStaysOpen()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromException<ResumePromptPreview>(new InvalidOperationException("boom")));
            SummaryAt(prompt, "Normal");

            MakeSummary(prompt);

            Assert.Contains("boom", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.Equal(Visibility.Collapsed, Named<TextBox>(prompt, "PreviewText").Visibility);
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Null(prompt.Choice);
        });
    }

    [Fact]
    public void ARefusedSummaryShowsTheRefusalAndFallsBackToFull()
    {
        UiThread.Run(() =>
        {
            const string refusal = "Not started: the sample tool is older than the minimum, the version this build needs.";
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromResult(new ResumePromptPreview(null, refusal)));
            SummaryAt(prompt, "Normal");

            MakeSummary(prompt);

            Assert.Equal(refusal + " " + ResumeDefaults.FullFallbackLine, Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.Equal(Visibility.Collapsed, Named<TextBox>(prompt, "PreviewText").Visibility);
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<RadioButton>(prompt, "FullChoice").IsChecked == true);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.False(Named<MenuItem>(prompt, "CopyItem").IsEnabled);
            Assert.Null(prompt.KeptSummary);
            Assert.Null(prompt.Choice);
        });
    }

    [Fact]
    public void ABuildCancelledWithTheDialogShowsNothing()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromCanceled<ResumePromptPreview>(new CancellationToken(canceled: true)));
            SummaryAt(prompt, "Normal");

            MakeSummary(prompt);

            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Null(prompt.KeptSummary);
        });
    }

    [Fact]
    public void TheDialogWithAPreviewRendersToAPng()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bullshark-resume-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "resume-prompt-preview.png");

        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, _, token) => Task.FromResult(SummaryOf(choice)));
            SummaryAt(prompt, "Normal");
            MakeSummary(prompt);

            // Shown off screen, so the window is laid out and its template applied, then drawn into a bitmap. Nothing is shown to the owner.
            prompt.Left = -30_000;
            prompt.Top = -30_000;
            prompt.ShowActivated = false;
            prompt.Show();
            try
            {
                prompt.UpdateLayout();
                var width = (int)Math.Ceiling(prompt.ActualWidth);
                var height = (int)Math.Ceiling(prompt.ActualHeight);
                var window = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                window.Render(prompt);
                // The window's render can leave a strip unpainted at its bottom (the window is taller than the drawn content). It is laid over the
                // window's own background, so the picture shows the dialog only.
                var frame = new DrawingVisual();
                using (var context = frame.RenderOpen())
                {
                    context.DrawRectangle(prompt.Background, null, new Rect(0, 0, width, height));
                    context.DrawImage(window, new Rect(0, 0, width, height));
                }
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            finally
            {
                prompt.Close();
            }
        });

        _output.WriteLine($"resume prompt preview png: {path}");
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
    }
}

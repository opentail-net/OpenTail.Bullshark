using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTail.Bullshark.Core.Resume;
using Xunit.Abstractions;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The resume prompt's preview (resume preview, 2026-11-20), headless: a fake preview stands in for the window's method, so the dialog's own rules
/// are what is tested. The sample texts are made up for the tests; no transcript is read.
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

    /// <summary>A prompt for a large context (a summary is the recommendation), with the given preview. Made on the WPF thread.</summary>
    private static ResumePrompt NewPrompt(Func<ResumeChoice, KeptSummary?, CancellationToken, Task<ResumePromptPreview>> preview)
    {
        EnsureAppResources();
        return new ResumePrompt(
            "Resume: a test session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            contextTokens: 500_000,
            RailPalette.DarkPalette,
            "opus",
            "high",
            preview);
    }

    private static T Named<T>(ResumePrompt prompt, string name) where T : class =>
        prompt.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static RadioButton LevelSegment(ResumePrompt prompt, string tag) =>
        Named<Panel>(prompt, "LevelRow").Children.OfType<RadioButton>().First(radio => (string?)radio.Tag == tag);

    /// <summary>Presses a menu item, the way a click does.</summary>
    private static void Press(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static Task<ResumePromptPreview> TailPreview(ResumeChoice choice, KeptSummary? kept, CancellationToken token) =>
        Task.FromResult(new ResumePromptPreview(new ResumePreview(TailText, 0, true, null)));

    [Fact]
    public void AFullResumeHasNoPreviewAndSaysWhy()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;

            var preview = Named<MenuItem>(prompt, "PreviewItem");
            var copy = Named<MenuItem>(prompt, "CopyItem");
            Assert.False(preview.IsEnabled);
            Assert.False(copy.IsEnabled);
            Assert.Equal("Resuming in full sends no prompt.", (string?)preview.ToolTip);
            Assert.Equal("Resuming in full sends no prompt.", (string?)copy.ToolTip);
        });
    }

    [Fact]
    public void TheTailAndTheSummaryCanBePreviewed()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            Named<RadioButton>(prompt, "TailChoice").IsChecked = true;
            Assert.True(Named<MenuItem>(prompt, "PreviewItem").IsEnabled);
            Assert.True(Named<MenuItem>(prompt, "CopyItem").IsEnabled);

            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Assert.True(Named<MenuItem>(prompt, "PreviewItem").IsEnabled);
        });
    }

    [Fact]
    public void AnInvalidNumberOfTurnsSwitchesThePreviewOff()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            Named<RadioButton>(prompt, "TailChoice").IsChecked = true;
            Named<TextBox>(prompt, "TurnsBox").Text = "lots";

            Assert.False(Named<MenuItem>(prompt, "PreviewItem").IsEnabled);
            Assert.Equal("Enter a whole number of turns first.", (string?)Named<MenuItem>(prompt, "PreviewItem").ToolTip);
        });
    }

    [Fact]
    public void ATailPreviewShowsTheTextAndItsSizeLine()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(TailPreview);
            Named<RadioButton>(prompt, "TailChoice").IsChecked = true;

            Press(Named<MenuItem>(prompt, "PreviewItem"));

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
    public void ASummaryPreviewIsKeptAndResumeReusesIt()
    {
        UiThread.Run(() =>
        {
            var seen = new List<KeptSummary?>();
            var summary = new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true);
            var calls = 0;
            var prompt = NewPrompt((choice, kept, token) =>
            {
                calls++;
                seen.Add(kept);
                // The window's method reuses a kept summary of the same level: the fake does the same, and makes one only otherwise.
                var made = kept is not null && kept.MatchesLevel(choice.Level) ? kept : summary;
                return Task.FromResult(new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, made)));
            });
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Press(Named<MenuItem>(prompt, "PreviewItem"));
            Assert.Equal(summary, prompt.KeptSummary);

            // A second preview of the same level sends the kept summary back, and the fake makes no new one.
            Press(Named<MenuItem>(prompt, "PreviewItem"));
            Assert.Equal(2, calls);
            Assert.Null(seen[0]);
            Assert.Equal(summary, seen[1]);

            Named<Button>(prompt, "ResumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(prompt.Choice);
            Assert.Equal(ResumeMode.Summary, prompt.Choice!.Mode);
            Assert.Equal(SummaryLevel.Normal, prompt.Choice.Level);
            Assert.Equal(summary, prompt.KeptSummary);
        });
    }

    [Fact]
    public void AChangedLevelHidesThePreviewAndDropsTheKeptSummary()
    {
        UiThread.Run(() =>
        {
            var summary = new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true);
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, summary))));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Press(Named<MenuItem>(prompt, "PreviewItem"));
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);

            LevelSegment(prompt, "Minimal").IsChecked = true;

            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Null(prompt.KeptSummary);
        });
    }

    [Fact]
    public void AChangedNumberOfTurnsHidesThePreviewButKeepsASummaryOfTheSameLevel()
    {
        UiThread.Run(() =>
        {
            var summary = new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true);
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, summary))));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Press(Named<MenuItem>(prompt, "PreviewItem"));

            Named<TextBox>(prompt, "TurnsBox").Text = "4";

            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.Equal(summary, prompt.KeptSummary);
        });
    }

    [Fact]
    public void AFailedPreviewShowsItsMessageAndTheDialogStaysOpen()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, token) => Task.FromException<ResumePromptPreview>(new InvalidOperationException("boom")));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Press(Named<MenuItem>(prompt, "PreviewItem"));

            Assert.Contains("boom", Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.Equal(Visibility.Collapsed, Named<TextBox>(prompt, "PreviewText").Visibility);
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Null(prompt.Choice);
        });
    }

    [Fact]
    public void ARefusedLaunchShowsTheRefusalAndResumeStaysOffUntilTheChoiceChanges()
    {
        UiThread.Run(() =>
        {
            const string refusal = "Not started: the sample tool is older than the minimum, the version this build needs.";
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new ResumePromptPreview(null, refusal)));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Press(Named<MenuItem>(prompt, "PreviewItem"));

            Assert.Equal(refusal, Named<TextBlock>(prompt, "PreviewHeader").Text);
            Assert.Equal(Visibility.Collapsed, Named<TextBox>(prompt, "PreviewText").Visibility);
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.False(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.False(Named<MenuItem>(prompt, "CopyItem").IsEnabled);
            Assert.True(Named<MenuItem>(prompt, "PreviewItem").IsEnabled);
            Assert.Null(prompt.KeptSummary);

            // A changed level takes the refusal away, and Resume is on again.
            LevelSegment(prompt, "Minimal").IsChecked = true;
            Assert.Equal(Visibility.Collapsed, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.Null(prompt.Choice);
        });
    }

    [Fact]
    public void ABuildCancelledWithTheDialogShowsNothing()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt((choice, kept, token) => Task.FromCanceled<ResumePromptPreview>(new CancellationToken(canceled: true)));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;

            Press(Named<MenuItem>(prompt, "PreviewItem"));

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
            var summary = new KeptSummary(SummaryLevel.Normal, "An example summary.", Saved: true);
            var prompt = NewPrompt((choice, kept, token) => Task.FromResult(new ResumePromptPreview(new ResumePreview(SummaryText, 0, true, summary))));
            Named<RadioButton>(prompt, "SummaryChoice").IsChecked = true;
            Press(Named<MenuItem>(prompt, "PreviewItem"));

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

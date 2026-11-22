using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The target chooser of the three resume screens (resume source and target plan, S3b-B2, RS-D3, RS-D4, RS-D8, RS-D18), headless. The chooser shows in a
/// summary only; its items, its default, the button and the summary line follow the pick; a pick hides an existing preview. The pure rule
/// <see cref="ResumeTarget.ChosenTarget"/> is tested as a table. No Start or Preview button is clicked, no preview callback is called, no CLI process
/// starts, and no state folder is read or written. A preview is put on screen through the screen's own private <c>ShowPreview</c>, with fixed text.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TargetChooserTests
{
    private const string TailText = "you: an example message\nreply: an example reply";

    /// <summary>The text the Copilot item shows as its tooltip while Copilot is not enabled as a target (RS-D8, ResumeTarget.CopilotTargetEnabled).</summary>
    private const string CopilotRefusal = "Copilot cannot be the target yet: the Copilot install that runs is not confirmed (G-O5).";

    /// <summary>The app's shared styles and the window's resources, as App.xaml gives them. The windows' XAML needs them when they are made.</summary>
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

    private static ResumePrompt NewClaudePrompt(ContinueMode? initialMode = null)
    {
        EnsureAppResources();
        return new ResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            contextTokens: 500_000,
            RailPalette.DarkPalette,
            "opus",
            "high",
            initialMode: initialMode);
    }

    private static CodexResumePrompt NewCodexPrompt(ContinueMode? initialMode = null)
    {
        EnsureAppResources();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) => Task.FromResult(new CodexPreview(TailText, null)),
            initialMode: initialMode);
    }

    private static CopilotResumePrompt NewCopilotPrompt(ContinueMode? initialMode = null)
    {
        EnsureAppResources();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) => Task.FromResult(new CopilotPreview(TailText, null)),
            initialMode: initialMode);
    }

    private static T Named<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    /// <summary>The chooser's item of a platform.</summary>
    private static ComboBoxItem ItemOf(ComboBox box, ProviderId platform) =>
        box.Items.OfType<ComboBoxItem>().Single(item => item.Tag is ProviderId tag && tag == platform);

    /// <summary>Picks a platform in the chooser, as the owner does: the selection changes, and the screen reacts.</summary>
    private static void Pick(ComboBox box, ProviderId platform) => box.SelectedItem = ItemOf(box, platform);

    /// <summary>Puts a built preview on screen through the screen's own private method. Nothing is built or sent.</summary>
    private static void ShowPreviewOn(Window window, object preview)
    {
        var method = window.GetType().GetMethod("ShowPreview", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no ShowPreview method");
        method.Invoke(window, new[] { preview });
    }

    /// <summary>The chooser is collapsed when the mode is In full or the last turns, and shown in a summary (RS-D3).</summary>
    private static void AssertChooserShownOnlyInASummary(Window window, string fullName, string tailName, string summaryName)
    {
        var box = Named<ComboBox>(window, "TargetBox");
        Named<RadioButton>(window, fullName).IsChecked = true;
        Assert.Equal(Visibility.Collapsed, box.Visibility);
        Named<RadioButton>(window, tailName).IsChecked = true;
        Assert.Equal(Visibility.Collapsed, box.Visibility);
        Named<RadioButton>(window, summaryName).IsChecked = true;
        Assert.Equal(Visibility.Visible, box.Visibility);
    }

    /// <summary>The three platforms, in order. The Copilot item is refused by the constant when it is another platform than the screen's own.</summary>
    private static void AssertThreePlatforms(ComboBox box, bool copilotIsOwnPlatform)
    {
        Assert.Equal(new[] { "Claude", "Codex", "Copilot" }, box.Items.OfType<ComboBoxItem>().Select(item => (string)item.Content).ToArray());
        var copilot = ItemOf(box, ProviderId.Copilot);
        if (copilotIsOwnPlatform)
        {
            // The screen's own platform is always allowed (RS-D1, RS-D2), so its item is enabled.
            Assert.True(copilot.IsEnabled);
            return;
        }
        Assert.False(copilot.IsEnabled);
        Assert.Equal(CopilotRefusal, (string?)copilot.ToolTip);
    }

    /// <summary>The default pick is the screen's own platform (RS-D3).</summary>
    private static void AssertDefaultIsTheSource(ComboBox box, string sourceName)
    {
        Assert.NotNull(box.SelectedItem);
        Assert.Equal(sourceName, (string?)((ComboBoxItem)box.SelectedItem!).Content);
    }

    /// <summary>
    /// Picking another platform changes the button and shows the line; picking the source again hides the line and restores the button (RS-D4, RS-O2).
    /// </summary>
    private static void AssertPickShowsTheLine(
        Window window, string buttonName, string sourceButton, ProviderId pick, string pickedButton, string pickedLine, ProviderId source)
    {
        var box = Named<ComboBox>(window, "TargetBox");
        var button = Named<Button>(window, buttonName);
        var line = Named<TextBlock>(window, "TargetLine");
        Assert.Equal(sourceButton, (string?)button.Content);
        Assert.Equal(Visibility.Collapsed, line.Visibility);

        Pick(box, pick);
        Assert.Equal(pickedButton, (string?)button.Content);
        Assert.Equal(pickedButton, AutomationProperties.GetName(button));
        Assert.Equal(pickedLine, line.Text);
        Assert.Equal(Visibility.Visible, line.Visibility);

        Pick(box, source);
        Assert.Equal(sourceButton, (string?)button.Content);
        Assert.Equal(Visibility.Collapsed, line.Visibility);
    }

    /// <summary>A preview on screen is taken away when the owner picks another platform (RS-D18).</summary>
    private static void AssertPickHidesAPreview(Window window, object preview, ProviderId pick)
    {
        ShowPreviewOn(window, preview);
        var block = Named<StackPanel>(window, "PreviewBlock");
        Assert.Equal(Visibility.Visible, block.Visibility);
        Pick(Named<ComboBox>(window, "TargetBox"), pick);
        Assert.Equal(Visibility.Collapsed, block.Visibility);
    }

    // ----- The Claude screen -----

    [Fact]
    public void TheClaudeChooserIsShownOnlyInASummary()
    {
        UiThread.Run(() => AssertChooserShownOnlyInASummary(NewClaudePrompt(ContinueMode.Summary), "FullChoice", "TailChoice", "SummaryChoice"));
    }

    [Fact]
    public void TheClaudeChooserOffersTheThreePlatformsAndCopilotIsRefused()
    {
        UiThread.Run(() => AssertThreePlatforms(Named<ComboBox>(NewClaudePrompt(ContinueMode.Summary), "TargetBox"), copilotIsOwnPlatform: false));
    }

    [Fact]
    public void TheClaudeChooserDefaultIsClaude()
    {
        UiThread.Run(() => AssertDefaultIsTheSource(Named<ComboBox>(NewClaudePrompt(ContinueMode.Summary), "TargetBox"), "Claude"));
    }

    [Fact]
    public void PickingCodexOnTheClaudeScreenChangesTheButtonAndShowsTheLine()
    {
        UiThread.Run(() => AssertPickShowsTheLine(
            NewClaudePrompt(ContinueMode.Summary), "ResumeButton", "Start in Claude", ProviderId.Codex,
            "Start in Codex", "Summary of Claude session → new Codex session", ProviderId.Claude));
    }

    [Fact]
    public void PickingAnotherPlatformOnTheClaudeScreenHidesAnExistingPreview()
    {
        UiThread.Run(() => AssertPickHidesAPreview(
            NewClaudePrompt(ContinueMode.Summary), new ResumePreview(TailText, 0, true, null), ProviderId.Codex));
    }

    // ----- The Codex screen -----

    [Fact]
    public void TheCodexChooserIsShownOnlyInASummary()
    {
        UiThread.Run(() => AssertChooserShownOnlyInASummary(NewCodexPrompt(ContinueMode.Summary), "FullChoice", "TailChoice", "SummaryChoice"));
    }

    [Fact]
    public void TheCodexChooserOffersTheThreePlatformsAndCopilotIsRefused()
    {
        UiThread.Run(() => AssertThreePlatforms(Named<ComboBox>(NewCodexPrompt(ContinueMode.Summary), "TargetBox"), copilotIsOwnPlatform: false));
    }

    [Fact]
    public void TheCodexChooserDefaultIsCodex()
    {
        UiThread.Run(() => AssertDefaultIsTheSource(Named<ComboBox>(NewCodexPrompt(ContinueMode.Summary), "TargetBox"), "Codex"));
    }

    [Fact]
    public void PickingClaudeOnTheCodexScreenChangesTheButtonAndShowsTheLine()
    {
        UiThread.Run(() => AssertPickShowsTheLine(
            NewCodexPrompt(ContinueMode.Summary), "StartButton", "Start in Codex", ProviderId.Claude,
            "Start in Claude", "Summary of Codex session → new Claude session", ProviderId.Codex));
    }

    [Fact]
    public void PickingAnotherPlatformOnTheCodexScreenHidesAnExistingPreview()
    {
        UiThread.Run(() => AssertPickHidesAPreview(
            NewCodexPrompt(ContinueMode.Summary), new CodexPreview(TailText, null), ProviderId.Claude));
    }

    // ----- The Copilot screen -----

    [Fact]
    public void TheCopilotChooserIsShownOnlyInASummary()
    {
        UiThread.Run(() => AssertChooserShownOnlyInASummary(NewCopilotPrompt(ContinueMode.Summary), "FullChoice", "TailChoice", "SummaryChoice"));
    }

    [Fact]
    public void TheCopilotChooserOffersTheThreePlatformsWithItsOwnEnabled()
    {
        UiThread.Run(() => AssertThreePlatforms(Named<ComboBox>(NewCopilotPrompt(ContinueMode.Summary), "TargetBox"), copilotIsOwnPlatform: true));
    }

    [Fact]
    public void TheCopilotChooserDefaultIsCopilot()
    {
        UiThread.Run(() => AssertDefaultIsTheSource(Named<ComboBox>(NewCopilotPrompt(ContinueMode.Summary), "TargetBox"), "Copilot"));
    }

    [Fact]
    public void PickingCodexOnTheCopilotScreenChangesTheButtonAndShowsTheLine()
    {
        UiThread.Run(() => AssertPickShowsTheLine(
            NewCopilotPrompt(ContinueMode.Summary), "StartButton", "Start in Copilot", ProviderId.Codex,
            "Start in Codex", "Summary of Copilot session → new Codex session", ProviderId.Copilot));
    }

    [Fact]
    public void PickingAnotherPlatformOnTheCopilotScreenHidesAnExistingPreview()
    {
        UiThread.Run(() => AssertPickHidesAPreview(
            NewCopilotPrompt(ContinueMode.Summary), new CopilotPreview(TailText, null), ProviderId.Codex));
    }

    // ----- The rule -----

    /// <summary>
    /// <see cref="ResumeTarget.ChosenTarget"/> for every source, mode and pick (3 x 3 x 3): the pick in a summary, else the source. The expected column is
    /// written out, not computed from the rule.
    /// </summary>
    [Fact]
    public void ChosenTargetIsThePickOnlyInASummaryForEveryCombination()
    {
        var rows = new (ProviderId Source, ContinueMode Mode, ProviderId Pick, ProviderId Expected)[]
        {
            (ProviderId.Claude, ContinueMode.Full, ProviderId.Claude, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.Full, ProviderId.Codex, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.Full, ProviderId.Copilot, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.LastTurns, ProviderId.Claude, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.LastTurns, ProviderId.Codex, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.LastTurns, ProviderId.Copilot, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.Summary, ProviderId.Claude, ProviderId.Claude),
            (ProviderId.Claude, ContinueMode.Summary, ProviderId.Codex, ProviderId.Codex),
            (ProviderId.Claude, ContinueMode.Summary, ProviderId.Copilot, ProviderId.Copilot),
            (ProviderId.Codex, ContinueMode.Full, ProviderId.Claude, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.Full, ProviderId.Codex, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.Full, ProviderId.Copilot, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.LastTurns, ProviderId.Claude, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.LastTurns, ProviderId.Codex, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.LastTurns, ProviderId.Copilot, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.Summary, ProviderId.Claude, ProviderId.Claude),
            (ProviderId.Codex, ContinueMode.Summary, ProviderId.Codex, ProviderId.Codex),
            (ProviderId.Codex, ContinueMode.Summary, ProviderId.Copilot, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.Full, ProviderId.Claude, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.Full, ProviderId.Codex, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.Full, ProviderId.Copilot, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.LastTurns, ProviderId.Claude, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.LastTurns, ProviderId.Codex, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.LastTurns, ProviderId.Copilot, ProviderId.Copilot),
            (ProviderId.Copilot, ContinueMode.Summary, ProviderId.Claude, ProviderId.Claude),
            (ProviderId.Copilot, ContinueMode.Summary, ProviderId.Codex, ProviderId.Codex),
            (ProviderId.Copilot, ContinueMode.Summary, ProviderId.Copilot, ProviderId.Copilot),
        };
        for (var row = 0; row < rows.Length; row++)
        {
            var (source, mode, pick, expected) = rows[row];
            var actual = ResumeTarget.ChosenTarget(source, mode, pick);
            Assert.True(expected == actual, $"row {row}: source {source.Value}, mode {mode}, pick {pick.Value}: got {actual.Value}");
        }
    }
}

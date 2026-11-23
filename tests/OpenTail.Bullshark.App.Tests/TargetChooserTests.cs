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
/// The Start menu of the three resume screens (resume source and target plan, RS-D2, RS-D3, RS-D4, RS-D8, RS-D18), headless. The menu is on in a summary
/// only; its items, their check, the Start button and the summary line follow the pick; a pick hides an existing preview. The pure rule
/// <see cref="ResumeTarget.ChosenTarget"/> is tested as a table. A pick raises the item's Click, which runs the screen's own handler. No Start, Make summary,
/// Copy or Preview button is pressed, no preview callback is called, no CLI process starts, and no state folder is read or written. A preview is put on screen
/// through the screen's own private <c>ShowPreview</c>, with fixed text.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TargetChooserTests
{
    private const string TailText = "you: an example message\nreply: an example reply";


    /// <summary>The tooltip of the narrow part of the Start split button in a summary (RS-D2).</summary>
    private const string MenuTipInSummary = "Start in another platform: only from a summary";

    /// <summary>The tooltip of the narrow part of the Start split button outside a summary (RS-D2).</summary>
    private const string MenuTipOutside = "Switching platform is only for a summary.";

    private static readonly ProviderId[] Platforms = { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot };

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

    /// <summary>
    /// The Start menu's item of a platform. The three items have the same names on every screen: the Resume menu's on the Claude screen, the split
    /// button's menu on the Codex and Copilot screens.
    /// </summary>
    private static MenuItem ItemOf(Window window, ProviderId platform) =>
        Named<MenuItem>(window, platform == ProviderId.Claude ? "TargetClaudeItem" : platform == ProviderId.Codex ? "TargetCodexItem" : "TargetCopilotItem");

    /// <summary>Picks a platform in the Start menu, as the owner does: the item's Click runs the screen's own handler. Nothing is started.</summary>
    private static void Pick(Window window, ProviderId platform) =>
        ItemOf(window, platform).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    /// <summary>Puts a built preview on screen through the screen's own private method. Nothing is built or sent.</summary>
    private static void ShowPreviewOn(Window window, object preview)
    {
        var method = window.GetType().GetMethod("ShowPreview", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no ShowPreview method");
        method.Invoke(window, new[] { preview });
    }

    /// <summary>
    /// The item is on in a summary where the rule allows it: the screen's own platform always, and another one unless it is Copilot, which is refused while
    /// Copilot is not enabled as a target (RS-D1, RS-D2, RS-D8).
    /// </summary>
    private static bool OnInASummary(ProviderId source, ProviderId platform) =>
        platform == source || platform != ProviderId.Copilot || ResumeTarget.CopilotTargetEnabled;

    /// <summary>
    /// The Start menu is on only in a summary (RS-D2). Outside one, every platform item is off. On the Codex and Copilot screens the narrow part of the
    /// split button is off too, with its tooltip. On the Claude screen the Resume menu stays on for its Preview and Copy items, so only its platform items
    /// follow the mode (<paramref name="splitName"/> is null there).
    /// </summary>
    private static void AssertStartMenuOnlyInASummary(
        Window window, string fullName, string tailName, string summaryName, ProviderId source, string? splitName)
    {
        foreach (var name in new[] { fullName, tailName })
        {
            Named<RadioButton>(window, name).IsChecked = true;
            foreach (var platform in Platforms) Assert.False(ItemOf(window, platform).IsEnabled);
            if (splitName is null) continue;
            var part = Named<Button>(window, splitName);
            Assert.False(part.IsEnabled);
            Assert.Equal(MenuTipOutside, (string?)part.ToolTip);
        }

        Named<RadioButton>(window, summaryName).IsChecked = true;
        foreach (var platform in Platforms) Assert.Equal(OnInASummary(source, platform), ItemOf(window, platform).IsEnabled);
        if (splitName is null) return;
        var summaryPart = Named<Button>(window, splitName);
        Assert.True(summaryPart.IsEnabled);
        Assert.Equal(MenuTipInSummary, (string?)summaryPart.ToolTip);
    }

    /// <summary>
    /// The three platform items, in the order Claude, Codex, Copilot: each is a checkable item with its header (RS-D4), on in a summary where the rule allows
    /// it. The refused Copilot item carries its reason as its tooltip (RS-D8); an item for the screen's own platform has none.
    /// </summary>
    private static void AssertThreePlatforms(Window window, ProviderId source)
    {
        Assert.Equal(new[] { "Start in Claude", "Start in Codex", "Start in Copilot" }, Platforms.Select(platform => (string?)ItemOf(window, platform).Header).ToArray());
        foreach (var platform in Platforms)
        {
            var item = ItemOf(window, platform);
            Assert.True(item.IsCheckable);
            Assert.Equal(OnInASummary(source, platform), item.IsEnabled);
        }
        // PP-D2 (2026-11-23): Copilot is an enabled target, so no item carries a refusal tooltip.
        Assert.Null((string?)ItemOf(window, ProviderId.Copilot).ToolTip);
    }

    /// <summary>The default pick is the screen's own platform, and it is the one checked item (RS-D3).</summary>
    private static void AssertDefaultIsTheSource(Window window, ProviderId source)
    {
        foreach (var platform in Platforms) Assert.Equal(platform == source, ItemOf(window, platform).IsChecked == true);
    }

    /// <summary>
    /// Picking another platform changes the button, checks its item and shows the line; picking the source again hides the line and restores the button
    /// (RS-D4, RS-O2).
    /// </summary>
    private static void AssertPickShowsTheLine(
        Window window, string buttonName, string sourceButton, ProviderId pick, string pickedButton, string pickedLine, ProviderId source)
    {
        var button = Named<Button>(window, buttonName);
        var line = Named<TextBlock>(window, "TargetLine");
        Assert.Equal(sourceButton, (string?)button.Content);
        Assert.Equal(Visibility.Collapsed, line.Visibility);

        Pick(window, pick);
        Assert.Equal(pickedButton, (string?)button.Content);
        Assert.Equal(pickedButton, AutomationProperties.GetName(button));
        Assert.Equal(pickedLine, line.Text);
        Assert.Equal(Visibility.Visible, line.Visibility);
        Assert.True(ItemOf(window, pick).IsChecked == true);
        Assert.False(ItemOf(window, source).IsChecked == true);

        Pick(window, source);
        Assert.Equal(sourceButton, (string?)button.Content);
        Assert.Equal(Visibility.Collapsed, line.Visibility);
        Assert.True(ItemOf(window, source).IsChecked == true);
    }

    /// <summary>A preview on screen is taken away when the owner picks another platform (RS-D18).</summary>
    private static void AssertPickHidesAPreview(Window window, object preview, ProviderId pick)
    {
        ShowPreviewOn(window, preview);
        var block = Named<StackPanel>(window, "PreviewBlock");
        Assert.Equal(Visibility.Visible, block.Visibility);
        Pick(window, pick);
        Assert.Equal(Visibility.Collapsed, block.Visibility);
    }

    /// <summary>
    /// Leaving a summary puts the pick back on the source (RS-D3): the Start button names the source, the line hides, and the source is checked again. The
    /// summary that comes back starts in the source too.
    /// </summary>
    private static void AssertLeavingASummaryPutsThePickBack(
        Window window, string fullName, string summaryName, string buttonName, string sourceButton, ProviderId pick, ProviderId source)
    {
        Pick(window, pick);
        Named<RadioButton>(window, fullName).IsChecked = true;
        Assert.Equal(sourceButton, (string?)Named<Button>(window, buttonName).Content);
        Assert.Equal(Visibility.Collapsed, Named<TextBlock>(window, "TargetLine").Visibility);
        Assert.True(ItemOf(window, source).IsChecked == true);

        Named<RadioButton>(window, summaryName).IsChecked = true;
        Assert.Equal(sourceButton, (string?)Named<Button>(window, buttonName).Content);
    }

    // ----- The Claude screen -----

    [Fact]
    public void TheClaudeStartMenuIsOnlyInASummary()
    {
        UiThread.Run(() =>
        {
            var prompt = NewClaudePrompt(ContinueMode.Summary);
            AssertStartMenuOnlyInASummary(prompt, "FullChoice", "TailChoice", "SummaryChoice", ProviderId.Claude, splitName: null);
            // The Resume menu itself stays on in every mode: its Preview and Copy items work there (ResumePrompt.xaml).
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.True(Named<Button>(prompt, "ResumeMenuButton").IsEnabled);
        });
    }

    [Fact]
    public void TheClaudeResumeMenuListsThePreviewAndCopyThenASeparatorAndTheThreePlatforms()
    {
        UiThread.Run(() =>
        {
            var prompt = NewClaudePrompt(ContinueMode.Summary);
            var menu = Named<ContextMenu>(prompt, "ResumeMenu");
            Assert.Equal(6, menu.Items.Count);
            Assert.Same(Named<MenuItem>(prompt, "PreviewItem"), menu.Items[0]);
            Assert.Same(Named<MenuItem>(prompt, "CopyItem"), menu.Items[1]);
            Assert.IsType<Separator>(menu.Items[2]);
            Assert.Same(ItemOf(prompt, ProviderId.Claude), menu.Items[3]);
            Assert.Same(ItemOf(prompt, ProviderId.Codex), menu.Items[4]);
            Assert.Same(ItemOf(prompt, ProviderId.Copilot), menu.Items[5]);
        });
    }

    [Fact]
    public void TheClaudeStartMenuOffersTheThreePlatformsAndCopilotIsEnabled()
    {
        UiThread.Run(() => AssertThreePlatforms(NewClaudePrompt(ContinueMode.Summary), ProviderId.Claude));
    }

    [Fact]
    public void TheClaudeStartMenuDefaultIsClaude()
    {
        UiThread.Run(() => AssertDefaultIsTheSource(NewClaudePrompt(ContinueMode.Summary), ProviderId.Claude));
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

    [Fact]
    public void LeavingASummaryPutsThePickBackOnTheClaudeSource()
    {
        UiThread.Run(() => AssertLeavingASummaryPutsThePickBack(
            NewClaudePrompt(ContinueMode.Summary), "FullChoice", "SummaryChoice", "ResumeButton", "Start in Claude", ProviderId.Codex, ProviderId.Claude));
    }

    // ----- The Codex screen -----

    [Fact]
    public void TheCodexStartMenuIsOnlyInASummary()
    {
        UiThread.Run(() => AssertStartMenuOnlyInASummary(
            NewCodexPrompt(ContinueMode.Summary), "FullChoice", "TailChoice", "SummaryChoice", ProviderId.Codex, splitName: "TargetMenuButton"));
    }

    [Fact]
    public void TheCodexStartMenuOffersTheThreePlatformsAndCopilotIsEnabled()
    {
        UiThread.Run(() => AssertThreePlatforms(NewCodexPrompt(ContinueMode.Summary), ProviderId.Codex));
    }

    [Fact]
    public void TheCodexStartMenuDefaultIsCodex()
    {
        UiThread.Run(() => AssertDefaultIsTheSource(NewCodexPrompt(ContinueMode.Summary), ProviderId.Codex));
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

    [Fact]
    public void PickingTheSamePlatformAgainKeepsThePreview()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCodexPrompt(ContinueMode.Summary);
            ShowPreviewOn(prompt, new CodexPreview(TailText, null));
            Pick(prompt, ProviderId.Codex);
            Assert.Equal(Visibility.Visible, Named<StackPanel>(prompt, "PreviewBlock").Visibility);
            Assert.True(ItemOf(prompt, ProviderId.Codex).IsChecked == true);
        });
    }

    [Fact]
    public void LeavingASummaryPutsThePickBackOnTheCodexSource()
    {
        UiThread.Run(() => AssertLeavingASummaryPutsThePickBack(
            NewCodexPrompt(ContinueMode.Summary), "FullChoice", "SummaryChoice", "StartButton", "Start in Codex", ProviderId.Claude, ProviderId.Codex));
    }

    // ----- The Copilot screen -----

    [Fact]
    public void TheCopilotStartMenuIsOnlyInASummary()
    {
        UiThread.Run(() => AssertStartMenuOnlyInASummary(
            NewCopilotPrompt(ContinueMode.Summary), "FullChoice", "TailChoice", "SummaryChoice", ProviderId.Copilot, splitName: "TargetMenuButton"));
    }

    [Fact]
    public void TheCopilotStartMenuOffersTheThreePlatformsWithItsOwnEnabled()
    {
        UiThread.Run(() => AssertThreePlatforms(NewCopilotPrompt(ContinueMode.Summary), ProviderId.Copilot));
    }

    [Fact]
    public void TheCopilotStartMenuDefaultIsCopilot()
    {
        UiThread.Run(() => AssertDefaultIsTheSource(NewCopilotPrompt(ContinueMode.Summary), ProviderId.Copilot));
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

    [Fact]
    public void LeavingASummaryPutsThePickBackOnTheCopilotSource()
    {
        UiThread.Run(() => AssertLeavingASummaryPutsThePickBack(
            NewCopilotPrompt(ContinueMode.Summary), "FullChoice", "SummaryChoice", "StartButton", "Start in Copilot", ProviderId.Codex, ProviderId.Copilot));
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

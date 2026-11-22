using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The words of the three resume screens (RS-D4, RS-O2, RS-O4), headless. Each screen's title and its start button come from
/// <see cref="ResumeTarget"/>, and each choice record's Target defaults to its own platform (RS-D14), which the Start handler also sets. The
/// preview callbacks return finished text at once; no model call is made, no CLI process starts, and no state folder is read or written.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ResumeScreenTextTests
{
    private const string TailText = "you: an example message\nreply: an example reply";

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

    private static CodexResumePrompt NewCodexPrompt()
    {
        EnsureAppResources();
        return new CodexResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) => Task.FromResult(new CodexPreview(TailText, null)));
    }

    private static CopilotResumePrompt NewCopilotPrompt()
    {
        EnsureAppResources();
        return new CopilotResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            RailPalette.DarkPalette,
            (choice, kept, token) => Task.FromResult(new CopilotPreview(TailText, null)));
    }

    private static T Named<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void TheClaudeScreenTitleIsTheTitleOfItsPlatform()
    {
        UiThread.Run(() => Assert.Equal(ResumeTarget.TitleOf(ProviderId.Claude), NewClaudePrompt().Title));
    }

    [Fact]
    public void TheCodexScreenTitleIsTheTitleOfItsPlatform()
    {
        UiThread.Run(() => Assert.Equal(ResumeTarget.TitleOf(ProviderId.Codex), NewCodexPrompt().Title));
    }

    [Fact]
    public void TheCopilotScreenTitleIsTheTitleOfItsPlatform()
    {
        UiThread.Run(() => Assert.Equal(ResumeTarget.TitleOf(ProviderId.Copilot), NewCopilotPrompt().Title));
    }

    [Fact]
    public void TheClaudeButtonShowsTheButtonOfItsPlatformAndItsAccessibleNameMatches()
    {
        UiThread.Run(() =>
        {
            var button = Named<Button>(NewClaudePrompt(), "ResumeButton");
            Assert.Equal(ResumeTarget.ButtonOf(ProviderId.Claude), (string?)button.Content);
            Assert.Equal(ResumeTarget.ButtonOf(ProviderId.Claude), AutomationProperties.GetName(button));
        });
    }

    [Fact]
    public void TheCodexStartButtonShowsTheButtonOfItsPlatformAndItsAccessibleNameMatches()
    {
        UiThread.Run(() =>
        {
            var button = Named<Button>(NewCodexPrompt(), "StartButton");
            Assert.Equal(ResumeTarget.ButtonOf(ProviderId.Codex), (string?)button.Content);
            Assert.Equal(ResumeTarget.ButtonOf(ProviderId.Codex), AutomationProperties.GetName(button));
        });
    }

    [Fact]
    public void TheCopilotStartButtonShowsTheButtonOfItsPlatformAndItsAccessibleNameMatches()
    {
        UiThread.Run(() =>
        {
            var button = Named<Button>(NewCopilotPrompt(), "StartButton");
            Assert.Equal(ResumeTarget.ButtonOf(ProviderId.Copilot), (string?)button.Content);
            Assert.Equal(ResumeTarget.ButtonOf(ProviderId.Copilot), AutomationProperties.GetName(button));
        });
    }

    [Fact]
    public void TheClaudeChoiceTargetDefaultsToClaude()
    {
        Assert.Equal(ProviderId.Claude, new ResumeChoice(ResumeMode.Full, null, 0, "opus", "high").Target);
        var remembered = new ResumeChoice(ResumeMode.Summary, SummaryLevel.Normal, 5, "opus", "high", Remember: true);
        Assert.Equal(ProviderId.Claude, remembered.Target);
        Assert.True(remembered.Remember);
    }

    [Fact]
    public void TheCodexChoiceTargetDefaultsToCodex()
    {
        Assert.Equal(ProviderId.Codex, new CodexResumeChoice(CodexResumeMode.LastTurns, null, 5).Target);
        var remembered = new CodexResumeChoice(CodexResumeMode.Summary, SummaryLevel.Normal, 5, null, Remember: true);
        Assert.Equal(ProviderId.Codex, remembered.Target);
        Assert.True(remembered.Remember);
    }

    [Fact]
    public void TheCopilotChoiceTargetDefaultsToCopilot()
    {
        Assert.Equal(ProviderId.Copilot, new CopilotResumeChoice(CopilotResumeMode.Full, null, 0).Target);
        var remembered = new CopilotResumeChoice(CopilotResumeMode.Summary, SummaryLevel.Normal, 5, Remember: true);
        Assert.Equal(ProviderId.Copilot, remembered.Target);
        Assert.True(remembered.Remember);
    }

    [Fact]
    public void TheClaudeStartGivesAChoiceWhoseTargetIsClaude()
    {
        UiThread.Run(() =>
        {
            var prompt = NewClaudePrompt(ContinueMode.Full);
            Click(Named<Button>(prompt, "ResumeButton"));
            var choice = prompt.Choice;
            Assert.NotNull(choice);
            Assert.Equal(ProviderId.Claude, choice!.Target);
        });
    }

    [Fact]
    public void TheCodexStartGivesAChoiceWhoseTargetIsCodex()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCodexPrompt();
            Click(Named<Button>(prompt, "StartButton"));
            var choice = prompt.Choice;
            Assert.NotNull(choice);
            Assert.Equal(ProviderId.Codex, choice!.Target);
        });
    }

    [Fact]
    public void TheCopilotStartGivesAChoiceWhoseTargetIsCopilot()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCopilotPrompt();
            Click(Named<Button>(prompt, "StartButton"));
            var choice = prompt.Choice;
            Assert.NotNull(choice);
            Assert.Equal(ProviderId.Copilot, choice!.Target);
        });
    }
}

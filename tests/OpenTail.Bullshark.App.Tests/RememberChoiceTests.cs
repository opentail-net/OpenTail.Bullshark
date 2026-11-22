using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The "Remember for this session" tick (Continue plan, stage C6, CN-D6). The mapping of each screen's mode, the key and the new entry are pure
/// (<see cref="RememberChoice"/>). The write goes through the window's static mark path (<see cref="SessionWindow.ApplyMark"/>) on a temporary library, so
/// the key in the file is checked. Claude's screen is built headless: its tick is off when it opens, and its start returns the tick. The sample ids and
/// folders are made up.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RememberChoiceTests : IDisposable
{
    private const string Id = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-remember-" + Guid.NewGuid().ToString("N"));

    public RememberChoiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string LibraryPath => Path.Combine(_dir, "library.json");

    // ---- The mapping of each screen's mode (pure). ----

    [Fact]
    public void ClaudesModesMapToTheContinueModes()
    {
        Assert.Equal(ContinueMode.Full, RememberChoice.ModeOf(ResumeMode.Full));
        Assert.Equal(ContinueMode.Summary, RememberChoice.ModeOf(ResumeMode.Summary));
        Assert.Equal(ContinueMode.LastTurns, RememberChoice.ModeOf(ResumeMode.TailOnly));
    }

    [Fact]
    public void CodexModesMapToTheContinueModes()
    {
        Assert.Equal(ContinueMode.Full, RememberChoice.ModeOf(CodexResumeMode.Full));
        Assert.Equal(ContinueMode.LastTurns, RememberChoice.ModeOf(CodexResumeMode.LastTurns));
        Assert.Equal(ContinueMode.Summary, RememberChoice.ModeOf(CodexResumeMode.Summary));
    }

    [Fact]
    public void CopilotModesMapToTheContinueModes()
    {
        Assert.Equal(ContinueMode.Full, RememberChoice.ModeOf(CopilotResumeMode.Full));
        Assert.Equal(ContinueMode.LastTurns, RememberChoice.ModeOf(CopilotResumeMode.LastTurns));
        Assert.Equal(ContinueMode.Summary, RememberChoice.ModeOf(CopilotResumeMode.Summary));
    }

    [Fact]
    public void TheKeyIsTheProvidersOwnKeyAndTheSessionId()
    {
        Assert.Equal("claude:" + Id, RememberChoice.KeyOf(ProviderId.Claude, Id));
        Assert.Equal("codex:" + Id, RememberChoice.KeyOf(ProviderId.Codex, Id));
        Assert.Equal("copilot:" + Id, RememberChoice.KeyOf(ProviderId.Copilot, Id));
    }

    // ---- The new entry (pure). ----

    [Fact]
    public void ANullEntryGivesANewEntryWithOnlyTheMode()
    {
        var entry = RememberChoice.Apply(null, ContinueMode.Summary);

        Assert.Equal(new LibraryEntry(ResumeMode: ContinueMode.Summary), entry);
        Assert.False(entry.IsEmpty);
    }

    [Fact]
    public void AnExistingEntryKeepsEveryOtherField()
    {
        var existing = new LibraryEntry(
            Name: "The owner's name",
            Pinned: true,
            Archived: true,
            Tags: ["review", "later"],
            Note: "a note",
            Colour: "red",
            MagicAt: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
            MagicNamedAt: new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
            PlanFile: "C:\\synthetic\\plan.md",
            AutoWake: true,
            AutoWokenHitAt: new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
            AutoWakeTimes: [new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)],
            ResumeMode: ContinueMode.Full);

        var updated = RememberChoice.Apply(existing, ContinueMode.LastTurns);

        Assert.Equal(ContinueMode.LastTurns, updated.ResumeMode);
        Assert.Equal("The owner's name", updated.Name);
        Assert.True(updated.Pinned);
        Assert.True(updated.Archived);
        Assert.Equal(new[] { "review", "later" }, updated.Tags);
        Assert.Equal("a note", updated.Note);
        Assert.Equal("red", updated.Colour);
        Assert.Equal(existing.MagicAt, updated.MagicAt);
        Assert.Equal(existing.MagicNamedAt, updated.MagicNamedAt);
        Assert.Equal("C:\\synthetic\\plan.md", updated.PlanFile);
        Assert.True(updated.AutoWake);
        Assert.Equal(existing.AutoWokenHitAt, updated.AutoWokenHitAt);
        Assert.Equal(existing.AutoWakeTimes, updated.AutoWakeTimes);
        Assert.Equal(existing with { ResumeMode = ContinueMode.LastTurns }, updated);
    }

    [Fact]
    public void ARememberedModeIsReplacedByTheNewOne()
    {
        Assert.Equal(ContinueMode.Summary, RememberChoice.Apply(new LibraryEntry(ResumeMode: ContinueMode.Full), ContinueMode.Summary).ResumeMode);
    }

    // ---- The write, on a temporary library: the key in the file and the other entries. ----

    [Fact]
    public void TheModeIsWrittenUnderTheClaudeKeyAndTheExistingEntryIsKept()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Name = "Claude name", Pinned = true });

        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, existing => RememberChoice.Apply(existing, ContinueMode.Summary));

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        var mark = all[new SessionKey(ProviderId.Claude, Id)];
        Assert.Equal(ContinueMode.Summary, mark.ResumeMode);
        Assert.Equal("Claude name", mark.Name);
        Assert.True(mark.Pinned);
        Assert.Contains("\"" + RememberChoice.KeyOf(ProviderId.Claude, Id) + "\"", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void EachProvidersModeIsWrittenUnderItsOwnKey()
    {
        var library = new SessionLibrary(LibraryPath);

        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, existing => RememberChoice.Apply(existing, ContinueMode.Full));
        SessionWindow.ApplyMark(library, ProviderId.Codex, Id, existing => RememberChoice.Apply(existing, ContinueMode.LastTurns));
        SessionWindow.ApplyMark(library, ProviderId.Copilot, Id, existing => RememberChoice.Apply(existing, ContinueMode.Summary));

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Equal(3, all.Count);
        Assert.Equal(ContinueMode.Full, all[new SessionKey(ProviderId.Claude, Id)].ResumeMode);
        Assert.Equal(ContinueMode.LastTurns, all[new SessionKey(ProviderId.Codex, Id)].ResumeMode);
        Assert.Equal(ContinueMode.Summary, all[new SessionKey(ProviderId.Copilot, Id)].ResumeMode);
        var text = File.ReadAllText(LibraryPath);
        Assert.Contains("\"" + RememberChoice.KeyOf(ProviderId.Codex, Id) + "\"", text);
        Assert.Contains("\"" + RememberChoice.KeyOf(ProviderId.Copilot, Id) + "\"", text);
    }

    // ---- The tick: its default, and what Claude's screen returns. ----

    [Fact]
    public void TheChoiceRecordDefaultsTheTickToOff()
    {
        Assert.False(new ResumeChoice(ResumeMode.Full, null, 0, "opus", "high").Remember);
        Assert.True(new ResumeChoice(ResumeMode.Full, null, 0, "opus", "high", Remember: true).Remember);
    }

    [Fact]
    public void ClaudesTickIsOffWhenTheScreenOpensAndTheStartReturnsIt()
    {
        UiThread.Run(() =>
        {
            var untouched = NewClaudePrompt();
            Assert.False(Named<CheckBox>(untouched, "RememberBox").IsChecked == true);
            Click(Named<Button>(untouched, "ResumeButton"));
            Assert.NotNull(untouched.Choice);
            Assert.False(untouched.Choice!.Remember);

            var ticked = NewClaudePrompt();
            Named<CheckBox>(ticked, "RememberBox").IsChecked = true;
            Click(Named<Button>(ticked, "ResumeButton"));
            Assert.NotNull(ticked.Choice);
            Assert.True(ticked.Choice!.Remember);
        });
    }

    /// <summary>Claude's screen on a small context, so the summary is the recommendation. Nothing is previewed or started.</summary>
    private static ResumePrompt NewClaudePrompt()
    {
        EnsureAppResources();
        return new ResumePrompt(
            "Resume: an example session",
            "2 turns · last active 1 day ago · in C:\\Work\\example",
            10_000,
            RailPalette.DarkPalette,
            "opus",
            "high");
    }

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

    private static T Named<T>(FrameworkElement window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}

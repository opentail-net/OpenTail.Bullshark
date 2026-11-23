using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>How a GitHub Copilot CLI session continues (step G3.1, the Codex resume rules R-D2 copied for Copilot).</summary>
public enum CopilotResumeMode
{
    /// <summary>The same session, with <c>copilot --resume</c> in a terminal tab. No text is sent by OpenTail.</summary>
    Full,

    /// <summary>A new session that starts from the last turns. No model call.</summary>
    LastTurns,

    /// <summary>A new session that starts from a summary made by one Copilot call (only on "Make summary"), then the last turns.</summary>
    Summary,
}

/// <summary>
/// The choice made in the Copilot resume screen. Level is set only for a summary; Turns is the number kept verbatim (0 for In full). Copilot has no model
/// choice in v1 (G-O3): the new session uses Copilot's own default. Remember is the "Remember for this session" tick: it is only returned to the caller,
/// which decides what to do with it (the screen writes nothing). Target is the platform the new session starts in (RS-D14). In a summary it is the chooser's pick; otherwise it is
/// Copilot, the screen's own platform.
/// </summary>
public sealed record CopilotResumeChoice(CopilotResumeMode Mode, SummaryLevel? Level, int Turns, bool Remember, ProviderId Target)
    : IProviderResumeChoice<CopilotResumeChoice>
{
    /// <summary>A choice whose target is Copilot, the screen's own platform. The target is a static field, not a constant, so it cannot be a default value.</summary>
    public CopilotResumeChoice(CopilotResumeMode Mode, SummaryLevel? Level, int Turns, bool Remember = false)
        : this(Mode, Level, Turns, Remember, ProviderId.Copilot) { }

    /// <summary>The mode as the shared rules see it.</summary>
    public ProviderResumeMode SharedMode => CopilotResumeRules.ToShared(Mode) ?? throw new ArgumentOutOfRangeException(nameof(Mode));

    public CopilotResumeChoice WithTarget(ProviderId target) => this with { Target = target };

    public CopilotResumeChoice WithRemember(bool remember) => this with { Remember = remember };
}

/// <summary>
/// What the screen's preview callback gives: the start text (empty when the session has no messages), the summary it made or reused (null for none),
/// and the reason no text was made (null when there is text, or when the session has no messages).
/// </summary>
public sealed record CopilotPreview(string Text, KeptSummary? KeptSummary, string? Problem = null) : IProviderPreview;

/// <summary>
/// The Copilot resume screen (step G3.1): a copy of the Codex resume screen for a Copilot session, with three ways to continue. Nothing runs until Start.
/// The logic is in <see cref="ProviderResumeScreen{TChoice, TPreview}"/> (dedup plan 2026-11-23); this window keeps its XAML and forwards its handlers.
/// Copilot has no model list, so Start never waits for one.
/// </summary>
public partial class CopilotResumePrompt : Window
{
    private readonly ProviderResumeScreen<CopilotResumeChoice, CopilotPreview> _screen;

    /// <param name="heading">The heading: the session's name.</param>
    /// <param name="facts">The line under the heading: turns, last activity and folder.</param>
    /// <param name="palette">The theme, as the rest of the window's dialogs use it.</param>
    /// <param name="previewAsync">Makes the start text for a choice. It is given the summary kept for the choice (or null), and returns the text and the summary it used.</param>
    /// <param name="initialMode">The mode the screen opens on (stage C4). Null keeps the screen's own start, the last turns. Opening on a mode never makes a summary.</param>
    /// <param name="folder">The session's folder, for the folder notice. Used only with <paramref name="folderExists"/>.</param>
    /// <param name="folderExists">Whether the folder exists. Null: no folder notice is shown. Start is off only with no folder on record.</param>
    public CopilotResumePrompt(string heading, string facts, RailPalette palette,
        Func<CopilotResumeChoice, KeptSummary?, CancellationToken, Task<CopilotPreview>>? previewAsync = null, ContinueMode? initialMode = null,
        string? folder = null, bool? folderExists = null)
    {
        InitializeComponent();
        // The window grows to the screen's work area at most: the body scrolls and the buttons stay in view (owner, 2026-11-22).
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        // The title names the source and the button names the target (RS-D4, RS-O2, RS-O4). The words come from ResumeTarget, the one place.
        Title = ResumeTarget.TitleOf(ProviderId.Copilot);
        var startLabel = ResumeTarget.ButtonOf(ProviderId.Copilot);
        StartButton.Content = startLabel;
        AutomationProperties.SetName(StartButton, startLabel);
        var folderNotice = folderExists is { } exists ? ResumeFolderNotice.For(folder, exists) : null;
        var blocksStart = ResumeFolderNotice.BlocksStart(folder, folderExists);
        UiTheme.Apply(palette);
        Resources["PromptBg"] = RailPalette.Brush(palette.Background);
        Resources["PromptPanel"] = RailPalette.Brush(palette.Panel);
        Resources["PromptText"] = RailPalette.Brush(palette.Text);
        Resources["PromptMuted"] = RailPalette.Brush(palette.Muted);
        Resources["PromptBorder"] = RailPalette.Brush(palette.Border);
        Resources["PromptSelection"] = RailPalette.Brush(palette.Selection);
        Resources["PromptGo"] = RailPalette.Brush(palette.Go);
        Resources["PromptGoHover"] = RailPalette.Brush(palette.GoHover);
        Resources["PromptGoPressed"] = RailPalette.Brush(palette.GoPressed);
        Resources["PromptGoText"] = RailPalette.Brush(palette.GoText);
        Resources["PromptAttention"] = RailPalette.Brush(palette.Attention);
        Resources["PromptAttentionHover"] = RailPalette.Brush(palette.AttentionHover);
        Resources["PromptAttentionPressed"] = RailPalette.Brush(palette.AttentionPressed);
        Resources["PromptAttentionText"] = RailPalette.Brush(palette.AttentionText);

        Heading.Text = heading;
        Facts.Text = facts;
        _screen = new ProviderResumeScreen<CopilotResumeChoice, CopilotPreview>(
            new ResumeScreenControls(
                FullChoice, TailChoice, SummaryChoice, LevelRow, TurnsBox, TargetLine, StartButton, TargetMenuButton, TargetMenu,
                TargetClaudeItem, TargetCodexItem, TargetCopilotItem, MakeSummaryButton, RememberBox, CostText, FolderNotice,
                PreviewHeader, PreviewBox, PreviewBlock, PreviewNote, CopyButton, (Style)Resources["Segment"]),
            new CopilotHooks(), previewAsync, initialMode, folderNotice, blocksStart, Close);
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public CopilotResumeChoice? Choice => _screen.Choice;

    /// <summary>
    /// The start text the window sends: exactly the text that was shown, so nothing is built again for the start. Null for In full, which sends no
    /// text.
    /// </summary>
    public string? PreviewText => _screen.PreviewText;

    /// <summary>The summary the dialog holds for the choice that was started, or null when none was made for it. The window does not save it again.</summary>
    public KeptSummary? KeptSummary => _screen.KeptSummary;

    private void TargetItem_Click(object sender, RoutedEventArgs e) => _screen.TargetItemClick(sender);

    private void TargetMenuButton_Click(object sender, RoutedEventArgs e) => _screen.OpenTargetMenu();

    private void MakeSummary_Click(object sender, RoutedEventArgs e) => _screen.MakeSummary();

    private void Copy_Click(object sender, RoutedEventArgs e) => _screen.Copy();

    private void Start_Click(object sender, RoutedEventArgs e) => _screen.Start();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _screen.Cancel();

    /// <summary>For the screenshots only (the App's <c>--copilot-resume summary</c>): chooses the summary mode, at the level on screen, and presses Make summary once. Nothing else calls it.</summary>
    public void MakeSummaryForScreenshot() => _screen.MakeSummaryForScreenshot();

    /// <summary>For the screenshots only (the App's <c>--copilot-resume full</c>): chooses In full, which sends no text. Nothing else calls it.</summary>
    public void ChooseFullForScreenshot() => _screen.ChooseFullForScreenshot();

    /// <summary>Puts a built preview on screen. The tests call it by reflection (TargetChooserTests); the screen itself never does.</summary>
    private void ShowPreview(CopilotPreview preview) => _screen.ShowBuiltPreview(preview);

    /// <summary>A request still being made is cancelled with the dialog: nothing waits for it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _screen.OnClosed();
        base.OnClosed(e);
    }

    /// <summary>What is specific to Copilot: its platform, its words, and its choice (no model).</summary>
    private sealed class CopilotHooks : IProviderResumeScreenHooks<CopilotResumeChoice>
    {
        public ProviderId Provider => ProviderId.Copilot;

        public ProviderResumeWording Wording => CopilotResumeRules.Wording;

        public CopilotResumeChoice MakeChoice(ProviderResumeMode mode, SummaryLevel? level, int turns) =>
            new(CopilotResumeRules.FromShared(mode), level, turns);

        public bool StartReady(CopilotResumeChoice? choice) => true;

        public void RefreshOwnControls(CopilotResumeChoice? choice, ProviderResumeMode mode, bool free)
        {
        }
    }
}

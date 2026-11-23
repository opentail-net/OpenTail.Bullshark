using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>How a Codex session continues (steps R5 and R7, decision R-D2).</summary>
public enum CodexResumeMode
{
    /// <summary>The same session, with <c>codex resume</c> in a terminal tab. No text is sent by OpenTail.</summary>
    Full,

    /// <summary>A new session that starts from the last turns. No model call.</summary>
    LastTurns,

    /// <summary>A new session that starts from a summary made by one Codex call (only on "Make summary"), then the last turns.</summary>
    Summary,
}

/// <summary>
/// The choice made in the Codex resume screen. Level is set only for a summary; Turns is the number kept verbatim (0 for In full). Model is the model
/// id the new session is started with (P9), or null for Codex's own default (also the case for In full, which sends no model). Remember is the
/// "Remember for this session" tick: it is only returned to the caller, which decides what to do with it (the screen writes nothing). Target is the platform the new session starts in (RS-D14). In a summary it is the chooser's pick; otherwise it is
/// Codex, the screen's own platform.
/// </summary>
public sealed record CodexResumeChoice(CodexResumeMode Mode, SummaryLevel? Level, int Turns, string? Model, bool Remember, ProviderId Target)
    : IProviderResumeChoice<CodexResumeChoice>
{
    /// <summary>A choice whose target is Codex, the screen's own platform. The target is a static field, not a constant, so it cannot be a default value.</summary>
    public CodexResumeChoice(CodexResumeMode Mode, SummaryLevel? Level, int Turns, string? Model = null, bool Remember = false)
        : this(Mode, Level, Turns, Model, Remember, ProviderId.Codex) { }

    /// <summary>The mode as the shared rules see it.</summary>
    public ProviderResumeMode SharedMode => CodexResumeRules.ToShared(Mode) ?? throw new ArgumentOutOfRangeException(nameof(Mode));

    public CodexResumeChoice WithTarget(ProviderId target) => this with { Target = target };

    public CodexResumeChoice WithRemember(bool remember) => this with { Remember = remember };
}

/// <summary>
/// What the screen's preview callback gives: the start text (empty when the session has no messages), the summary it made or reused (null for
/// none), and the reason no text was made (null when there is text, or when the session has no messages).
/// </summary>
public sealed record CodexPreview(string Text, KeptSummary? KeptSummary, string? Problem = null, int? Lines = null, bool Stale = false, bool SummaryFailed = false) : IProviderPreview;

/// <summary>
/// The Codex resume screen (step R5): a copy of Claude's resume prompt for a Codex session, with three ways to continue. Nothing runs until Start.
/// The logic is in <see cref="ProviderResumeScreen{TChoice, TPreview}"/> (dedup plan 2026-11-23). What is specific to Codex is here: its model list, which
/// Start waits for (except In full, and a cross start), and its model row. This window keeps its XAML and forwards its handlers.
/// </summary>
public partial class CodexResumePrompt : Window
{
    private readonly ProviderResumeScreen<CodexResumeChoice, CodexPreview> _screen;

    /// <summary>True once the model list is in (or none was asked for). Start of a new session waits for it; In full does not need it.</summary>
    private bool _modelsReady;

    /// <param name="heading">The heading: the session's name.</param>
    /// <param name="facts">The line under the heading: turns, last activity and folder.</param>
    /// <param name="palette">The theme, as the rest of the window's dialogs use it.</param>
    /// <param name="previewAsync">Makes the start text for a choice. It is given the summary kept for the choice (or null), and returns the text and the summary it used.</param>
    /// <param name="models">The model list, read off the UI thread. The model box shows that it is loading until the list is in, and Start waits for it (except In full). Null: no list, so Codex's default.</param>
    /// <param name="initialMode">The mode the screen opens on (stage C4). Null keeps the screen's own start, the last turns. Opening on a mode never makes a summary.</param>
    /// <param name="folder">The session's folder, for the folder notice. Used only with <paramref name="folderExists"/>.</param>
    /// <param name="folderExists">Whether the folder exists. Null: no folder notice is shown. Start is off only with no folder on record.</param>
    public CodexResumePrompt(string heading, string facts, RailPalette palette,
        Func<CodexResumeChoice, KeptSummary?, bool, CancellationToken, Task<CodexPreview>>? previewAsync = null,
        Task<CodexModelReading>? models = null, ContinueMode? initialMode = null, string? folder = null, bool? folderExists = null,
        Func<int, bool>? summaryHasLines = null, int? lines = null)
    {
        InitializeComponent();
        // The window grows to the screen's work area at most: the body scrolls and the buttons stay in view (owner, 2026-11-22).
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        // The title names the source and the button names the target (RS-D4, RS-O2, RS-O4). The words come from ResumeTarget, the one place.
        Title = ResumeTarget.TitleOf(ProviderId.Codex);
        var startLabel = ResumeTarget.ButtonOf(ProviderId.Codex);
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
        _screen = new ProviderResumeScreen<CodexResumeChoice, CodexPreview>(
            new ResumeScreenControls(
                FullChoice, SummaryChoice, LevelBlock, LevelRow, TailBox, TurnsBox, TailBlock, TargetLine, StartButton, TargetMenuButton, TargetMenu,
                TargetClaudeItem, TargetCodexItem, TargetCopilotItem, MakeSummaryButton, WriteByTargetButton, RememberBox, CostText, FolderNotice,
                PreviewHeader, PreviewBox, PreviewBlock, PreviewNote, CopyButton, (Style)Resources["Segment"]),
            new CodexHooks(this), previewAsync, summaryHasLines, initialMode, folderNotice, blocksStart, Close, lines);

        // The screen's first build runs before the model list is read: the list then turns Start on (or the model box) when it is in.
        if (models is null)
        {
            _modelsReady = true;
            ModelNote.Text = CodexModelChoices.Fill(ModelBox, null);
            _screen.Refresh();
        }
        else
        {
            ModelBox.Items.Add(new ComboBoxItem { Content = "Reading the model list...", IsEnabled = false });
            ModelBox.SelectedIndex = 0;
            ModelNote.Text = "Reading the model list. Start waits for it.";
            _ = LoadModelsAsync(models);
        }
    }

    /// <summary>
    /// Fills the model box when the list is read (off the UI thread, by the window). A list that cannot be read gives the one entry "Codex's default",
    /// and the note says why (M-D3). Never throws: the read's own failures are problems in the reading, and any other failure is the same entry.
    /// </summary>
    private async Task LoadModelsAsync(Task<CodexModelReading> models)
    {
        CodexModelReading reading;
        try
        {
            reading = await models;
        }
        catch (Exception ex)
        {
            AppLog.Write($"codex model list not read in the screen: {ex.GetType().Name}");
            reading = new CodexModelReading([], 0, 0, CodexModelProblem.Unexpected);
        }
        ModelNote.Text = CodexModelChoices.Fill(ModelBox, reading);
        _modelsReady = true;
        _screen.Refresh();
    }

    /// <summary>The model id on screen for a new session, or null for Codex's default (the one entry of a list that could not be read).</summary>
    private string? SelectedModel() => CodexModelChoices.SelectedModel(ModelBox);

    /// <summary>True when the start on screen goes to another platform than Codex: the model is not used, so its row is hidden (RS-D12).</summary>
    private static bool IsCross(CodexResumeChoice? choice) => choice is not null && !CrossModelRow.ShowsModel(ProviderId.Codex, choice.Target);

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public CodexResumeChoice? Choice => _screen.Choice;

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

    /// <summary>The "Make summary with &lt;Target&gt;" button (RS-D28): the chosen platform writes the summary after the source's failed.</summary>
    private void WriteByTarget_Click(object sender, RoutedEventArgs e) => _screen.WriteByTarget();

    private void Copy_Click(object sender, RoutedEventArgs e) => _screen.Copy();

    private void Start_Click(object sender, RoutedEventArgs e) => _screen.Start();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _screen.Cancel();

    /// <summary>
    /// For the screenshots only (the App's <c>--codex-resume summary</c>): chooses the summary mode, at the level on screen, and presses Make summary
    /// once. Nothing else calls it.
    /// </summary>
    public void MakeSummaryForScreenshot() => _screen.MakeSummaryForScreenshot();

    /// <summary>For the screenshots only: chooses In full, which sends no text. Nothing calls it for Codex yet; kept with the Copilot screen's.</summary>
    public void ChooseFullForScreenshot() => _screen.ChooseFullForScreenshot();

    /// <summary>Puts a built preview on screen. The tests call it by reflection (TargetChooserTests); the screen itself never does.</summary>
    private void ShowPreview(CodexPreview preview) => _screen.ShowBuiltPreview(preview);

    /// <summary>A request still being made is cancelled with the dialog: nothing waits for it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _screen.OnClosed();
        base.OnClosed(e);
    }

    /// <summary>
    /// What is specific to Codex: its platform, its words, its choice (with the model), its model list (Start waits for it, unless the choice needs
    /// none), and its model row. The model state is the window's; the hooks read it.
    /// </summary>
    private sealed class CodexHooks : IProviderResumeScreenHooks<CodexResumeChoice>
    {
        private readonly CodexResumePrompt _owner;

        public CodexHooks(CodexResumePrompt owner) => _owner = owner;

        public ProviderId Provider => ProviderId.Codex;

        public ProviderResumeWording Wording => CodexResumeRules.Wording;

        public CodexResumeChoice MakeChoice(ProviderResumeMode mode, SummaryLevel? level, int turns) =>
            mode == ProviderResumeMode.Full
                ? new CodexResumeChoice(CodexResumeMode.Full, null, 0)
                : new CodexResumeChoice(CodexResumeRules.FromShared(mode), level, turns, _owner.SelectedModel());

        public bool StartReady(CodexResumeChoice? choice)
        {
            var mode = choice?.SharedMode ?? ProviderResumeMode.Summary;
            return IsCross(choice) || mode == ProviderResumeMode.Full || _owner._modelsReady;
        }

        public void RefreshOwnControls(CodexResumeChoice? choice, ProviderResumeMode mode, bool free)
        {
            var modelRow = mode != ProviderResumeMode.Full;
            _owner.ModelBlock.Visibility = modelRow && !IsCross(choice) ? Visibility.Visible : Visibility.Collapsed;
            var enabled = free && modelRow && _owner._modelsReady;
            _owner.ModelBox.IsEnabled = enabled;
            _owner.ModelBox.Opacity = enabled ? 1 : 0.5;
        }
    }
}

using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
{
    /// <summary>A choice whose target is Copilot, the screen's own platform. The target is a static field, not a constant, so it cannot be a default value.</summary>
    public CopilotResumeChoice(CopilotResumeMode Mode, SummaryLevel? Level, int Turns, bool Remember = false)
        : this(Mode, Level, Turns, Remember, ProviderId.Copilot) { }
}

/// <summary>
/// What the screen's preview callback gives: the start text (empty when the session has no messages), the summary it made or reused (null for none),
/// and the reason no text was made (null when there is text, or when the session has no messages).
/// </summary>
public sealed record CopilotPreview(string Text, KeptSummary? KeptSummary, string? Problem = null);

/// <summary>
/// The Copilot resume screen (step G3.1): a copy of the Codex resume screen for a Copilot session, with three ways to continue. Nothing runs until Start.
/// The last-turns text is built when the mode, the turns or the window changes (no model call). A summary is made only by the Make summary button, and it
/// is kept for the level and turns it was made at. The start text in the preview is the text Start sends (<see cref="PreviewText"/>). Cancel is the
/// default. The callback runs off the UI thread and can be cancelled: a change or the close cancels the one in progress.
/// </summary>
public partial class CopilotResumePrompt : Window
{
    private readonly Func<CopilotResumeChoice, KeptSummary?, CancellationToken, Task<CopilotPreview>>? _previewAsync;
    private CancellationTokenSource? _cts;
    private int _request;
    private bool _busy;
    private string? _previewText;
    private KeptSummary? _kept;

    /// <summary>The platform the Start menu picks (RS-D3): Copilot by default, and back to Copilot when the mode leaves a summary.</summary>
    private ProviderId _target = ProviderId.Copilot;

    /// <summary>The folder notice shown in red (<see cref="ResumeFolderNotice"/>), or null. Start is off only with no folder on record; the choices and Copy stay on.</summary>
    private readonly string? _folderNotice;

    /// <summary>True while the folder notice blocks Start: only a session with no folder on record. A missing folder does not block it: Start makes it again.</summary>
    private readonly bool _blocksStart;

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
        _previewAsync = previewAsync;
        _folderNotice = folderExists is { } exists ? ResumeFolderNotice.For(folder, exists) : null;
        _blocksStart = ResumeFolderNotice.BlocksStart(folder, folderExists);
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
        FullChoice.Content = CopilotResumeRules.ModeLabel(CopilotResumeMode.Full);
        TailChoice.Content = CopilotResumeRules.ModeLabel(CopilotResumeMode.LastTurns);
        SummaryChoice.Content = CopilotResumeRules.ModeLabel(CopilotResumeMode.Summary);
        TurnsBox.Text = CopilotResumeRules.DefaultTurns.ToString(CultureInfo.InvariantCulture);

        // The start mode is set before the handlers below are attached, so the screen's first build runs once, for the mode it opens on.
        // Null selects nothing: the XAML's last-turns radio stays as it is.
        switch (ProviderResumeRules.StartMode(initialMode))
        {
            case ProviderResumeMode.Full: FullChoice.IsChecked = true; break;
            case ProviderResumeMode.Summary: SummaryChoice.IsChecked = true; break;
            case ProviderResumeMode.LastTurns: TailChoice.IsChecked = true; break;
        }

        foreach (var level in new[] { SummaryLevel.Minimal, SummaryLevel.Normal, SummaryLevel.Complete })
        {
            var segment = Segment(CopilotResumeRules.LevelLabel(level), "CopilotLevel", level == SummaryLevel.Normal);
            segment.Tag = level.ToString();
            segment.Checked += (_, _) => OnChoiceChanged();
            LevelRow.Children.Add(segment);
        }
        foreach (var radio in new[] { FullChoice, TailChoice, SummaryChoice }) radio.Checked += (_, _) => OnChoiceChanged();
        TurnsBox.TextChanged += (_, _) => OnChoiceChanged();

        OnChoiceChanged();
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public CopilotResumeChoice? Choice { get; private set; }

    /// <summary>
    /// The start text the window sends: exactly the text that was shown, so nothing is built again for the start. Null for In full, which sends no
    /// text.
    /// </summary>
    public string? PreviewText { get; private set; }

    /// <summary>The summary the dialog holds for the choice that was started, or null when none was made for it. The window does not save it again.</summary>
    public KeptSummary? KeptSummary { get; private set; }

    /// <summary>One option of the level row: a segment that shows as pressed when it is the chosen one.</summary>
    private RadioButton Segment(string text, string group, bool isChecked) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = isChecked,
        Style = (Style)Resources["Segment"],
    };

    /// <summary>The summary level on screen. Normal when none is chosen.</summary>
    private SummaryLevel SelectedLevel()
    {
        var tag = LevelRow.Children.OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true)?.Tag as string;
        return Enum.TryParse<SummaryLevel>(tag, out var level) ? level : SummaryLevel.Normal;
    }

    /// <summary>
    /// The choice on screen, or null when no mode is checked. <paramref name="turnsValid"/> is false when the turns box is not a valid number; In full
    /// needs no turns, so it is always valid.
    /// </summary>
    private CopilotResumeChoice? Selected(out bool turnsValid)
    {
        var valid = CopilotResumeRules.TryTurns(TurnsBox.Text, out var turns);
        if (FullChoice.IsChecked == true)
        {
            turnsValid = true;
            return Targeted(new CopilotResumeChoice(CopilotResumeMode.Full, null, 0));
        }
        turnsValid = valid;
        if (TailChoice.IsChecked == true) return Targeted(new CopilotResumeChoice(CopilotResumeMode.LastTurns, null, turns));
        if (SummaryChoice.IsChecked == true) return Targeted(new CopilotResumeChoice(CopilotResumeMode.Summary, SelectedLevel(), turns));
        return null;
    }

    /// <summary>True when the summary the dialog holds is for the choice's level. It is kept only for the level and turns it was made at (R-D3).</summary>
    private bool KeptMatches(CopilotResumeChoice? choice) => ProviderResumeRules.KeptMatches(choice?.Level, _kept);

    /// <summary>The choice with its target (RS-D3, RS-D14): the preview is built for the target on screen, and Start sends the same one.</summary>
    private CopilotResumeChoice Targeted(CopilotResumeChoice choice) => choice with { Target = StartTarget(choice.Mode) };

    /// <summary>
    /// The platform the new session starts in, for the mode (<see cref="ResumeTarget.ChosenTarget"/>): the Start menu's pick in a summary, else Copilot.
    /// </summary>
    private ProviderId StartTarget(CopilotResumeMode mode) =>
        ResumeTarget.ChosenTarget(ProviderId.Copilot, RememberChoice.ModeOf(mode), _target);

    /// <summary>
    /// Shows the target on screen (RS-D3, RS-D4, RS-O2): the line "Summary of ... session, new ... session" when a summary starts in another platform, and
    /// the Start button, which names the target.
    /// </summary>
    private void ShowTarget(ProviderId target)
    {
        var summary = SummaryChoice.IsChecked == true;
        var line = ResumeTarget.SummaryLine(ProviderId.Copilot, target);
        TargetLine.Text = line ?? string.Empty;
        TargetLine.Visibility = summary && line is not null ? Visibility.Visible : Visibility.Collapsed;
        var startLabel = ResumeTarget.ButtonOf(target);
        StartButton.Content = startLabel;
        AutomationProperties.SetName(StartButton, startLabel);
    }

    /// <summary>The tooltip of the narrow part of the Start split button: in a summary, and outside one (RS-D2).</summary>
    private const string TargetMenuTip = "Start in another platform: only from a summary";

    /// <summary>The tooltip of the narrow part of the Start split button while it is off: the mode is not a summary (RS-D2).</summary>
    private const string TargetMenuOffTip = "Switching platform is only for a summary.";

    /// <summary>
    /// Runs when the owner picks a platform in the Start menu (RS-D3, RS-D18). No model call. A pick that changes the target cancels a request in progress,
    /// which is for the old target, and takes the preview on screen away. Make summary builds the text again for the target on screen, with the kept summary.
    /// </summary>
    private void TargetItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResumeTargetMenu.PlatformOf(sender) is not { } platform) return;
        if (platform != _target)
        {
            CancelRequest();
            _busy = false;
            HidePreview();
        }
        _target = platform;
        Refresh();
    }

    /// <summary>The narrow part of the Start split button: opens the Start menu above it. Nothing is started.</summary>
    private void TargetMenuButton_Click(object sender, RoutedEventArgs e)
    {
        TargetMenu.PlacementTarget = TargetMenuButton;
        TargetMenu.IsOpen = true;
    }

    /// <summary>
    /// Sets the Start menu (RS-D3, RS-D8): the pick is checked, and each platform item is on only where the rule allows it for a summary.
    /// <paramref name="menuOn"/> is false outside a summary, and while a summary is made.
    /// </summary>
    private void ShowTargetMenu(bool menuOn)
    {
        ResumeTargetMenu.Apply(TargetClaudeItem, ProviderId.Claude, ProviderId.Copilot, _target, menuOn);
        ResumeTargetMenu.Apply(TargetCodexItem, ProviderId.Codex, ProviderId.Copilot, _target, menuOn);
        ResumeTargetMenu.Apply(TargetCopilotItem, ProviderId.Copilot, ProviderId.Copilot, _target, menuOn);
    }

    /// <summary>
    /// Runs when the mode, the level or the turns change. The preview on screen is for the old choice, so it is taken away. A summary depends on the
    /// session and the level only (R-D3): a change of mode or of turns keeps the kept summary, and a change of level drops it. The preview is built
    /// again at once when it needs no model call: the last turns, or a summary kept for the level on screen.
    /// </summary>
    private void OnChoiceChanged()
    {
        // RS-D3: the target goes back to the source when the mode leaves a summary; the Start menu is off then.
        if (SummaryChoice.IsChecked != true) _target = ProviderId.Copilot;
        CancelRequest();
        _busy = false;
        var choice = Selected(out var turnsValid);
        _kept = ProviderResumeRules.KeptAfterChange(choice?.Mode == CopilotResumeMode.Summary, choice?.Level, _kept);
        HidePreview();
        Refresh();
        if (!turnsValid || choice is null) return;
        if (CopilotResumeRules.RunsAtOnce(choice.Mode, KeptMatches(choice))) _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// Sets what is on and off for the choice: the cost line, the choices, the level row (summary only), the turns box (not in full), Make summary
    /// (summary only), Copy, and Start (<see cref="CopilotResumeRules.CanStart"/>). While a summary is made, the choices and the buttons are off.
    /// </summary>
    private void Refresh()
    {
        var choice = Selected(out var turnsValid);
        var mode = choice?.Mode ?? CopilotResumeMode.LastTurns;
        var free = !_busy;
        var turnsApply = mode != CopilotResumeMode.Full;
        CostText.Text = CopilotResumeRules.CostLine(mode) + (turnsApply && !turnsValid ? " " + CopilotResumeRules.TurnsMessage : string.Empty);
        FolderNotice.Text = _folderNotice ?? string.Empty;
        FolderNotice.Visibility = _folderNotice is null ? Visibility.Collapsed : Visibility.Visible;

        Enable(FullChoice, free);
        Enable(TailChoice, free);
        Enable(SummaryChoice, free);
        Enable(LevelRow, free && mode == CopilotResumeMode.Summary);
        Enable(TurnsBox, free && turnsApply);
        ShowTarget(choice?.Target ?? ProviderId.Copilot);
        var inSummary = mode == CopilotResumeMode.Summary;
        Enable(TargetMenuButton, free && inSummary);
        TargetMenuButton.ToolTip = inSummary ? TargetMenuTip : TargetMenuOffTip;
        ShowTargetMenu(free && inSummary);
        Enable(MakeSummaryButton, free && mode == CopilotResumeMode.Summary && turnsValid);
        Enable(RememberBox, free);
        var previewReady = _previewText is not null;
        Enable(CopyButton, free && previewReady);
        Enable(StartButton, free && !_blocksStart && CopilotResumeRules.CanStart(mode, previewReady, KeptMatches(choice)));
    }

    private static void Enable(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : 0.5;
    }

    /// <summary>The Make summary button: makes the summary now, once, for the level and turns on screen.</summary>
    private void MakeSummary_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out var turnsValid);
        if (choice is { Mode: CopilotResumeMode.Summary } && turnsValid && !_busy) _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// For the screenshots only (the App's <c>--copilot-resume summary</c>): chooses the summary mode, at the level on screen, and presses Make summary
    /// once. Nothing else calls it.
    /// </summary>
    public void MakeSummaryForScreenshot()
    {
        SummaryChoice.IsChecked = true;
        MakeSummary_Click(this, new RoutedEventArgs());
    }

    /// <summary>For the screenshots only (the App's <c>--copilot-resume full</c>): chooses In full, which sends no text. Nothing else calls it.</summary>
    public void ChooseFullForScreenshot() => FullChoice.IsChecked = true;

    /// <summary>
    /// Makes the start text for the choice, off the UI thread, and shows it. A summary holds the choices and the buttons until it is made. A request
    /// replaces the one before it: the older one is cancelled and its result is dropped. A failure is shown in the preview area; a cancel shows
    /// nothing. Never throws: every failure is shown on screen.
    /// </summary>
    private async Task RunRequestAsync(CopilotResumeChoice choice)
    {
        if (_previewAsync is null) return;
        CancelRequest();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var id = _request;
        _busy = choice.Mode == CopilotResumeMode.Summary;
        ShowWorking(choice);
        Refresh();
        try
        {
            var preview = await _previewAsync(choice, _kept, cts.Token);
            if (id != _request) return;
            if (preview.KeptSummary is { } made) _kept = made;
            if (preview.Problem is { } problem && preview.Text.Length == 0) ShowFailedSummary(choice, problem);
            else ShowPreview(preview);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a change of the choice, or by the dialog closing: nothing is shown, and the selection stays.
        }
        catch (Exception ex)
        {
            if (id == _request) ShowFailedSummary(choice, $"The start text could not be made ({ex.GetType().Name}).");
        }
        finally
        {
            if (id == _request)
            {
                _busy = false;
                _cts = null;
                Refresh();
            }
        }
    }

    /// <summary>Cancels the request in progress, if any, and makes its result stale, so it is dropped when it arrives.</summary>
    private void CancelRequest()
    {
        _request++;
        _cts?.Cancel();
        _cts = null;
    }

    /// <summary>Shows that the text for the choice is being made. Summary says a Copilot call is made.</summary>
    private void ShowWorking(CopilotResumeChoice choice) => ShowMessage(
        choice.Mode == CopilotResumeMode.Summary
            ? "Making the summary: one Copilot call. Nothing is sent to a session."
            : "Reading the session. Nothing is sent to a session.",
        problem: false);

    /// <summary>
    /// Shows a built text: its size line, and the text read-only. A preview with no text says why there is none: the window's problem, or that the
    /// session has no messages to start from (Start stays off).
    /// </summary>
    private void ShowPreview(CopilotPreview preview)
    {
        if (preview.Text.Length == 0)
        {
            ShowMessage(preview.Problem ?? "This session has no messages to continue from, so Start is off.", problem: preview.Problem is not null);
            return;
        }
        _previewText = preview.Text;
        PreviewHeader.Text = CopilotResumeRules.SizeLine(preview.Text.Length);
        PreviewHeader.ClearValue(TextBlock.ForegroundProperty);
        PreviewBox.Text = preview.Text;
        PreviewBox.CaretIndex = 0;
        PreviewBox.ScrollToHome();
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
    }

    /// <summary>Shows one line in the preview area and no text. A problem is shown in the danger colour. There is then no text for Start.</summary>
    private void ShowMessage(string message, bool problem)
    {
        _previewText = null;
        PreviewHeader.Text = message;
        if (problem) PreviewHeader.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        else PreviewHeader.ClearValue(TextBlock.ForegroundProperty);
        PreviewBox.Text = string.Empty;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows a failed preview: the problem in the danger colour, and no text. A summary that was not made falls back to In full (ResumeDefaults): In full
    /// is selected, and the problem is shown with the line that says so. Nothing is started.
    /// </summary>
    private void ShowFailedSummary(CopilotResumeChoice choice, string problem)
    {
        if (!ResumeDefaults.FallBackToFull(choice.Mode == CopilotResumeMode.Summary, cancelled: false))
        {
            ShowMessage(problem, problem: true);
            return;
        }
        // Selecting In full clears the preview (OnChoiceChanged), so the problem is shown after it.
        FullChoice.IsChecked = true;
        ShowMessage($"{problem} {ResumeDefaults.FullFallbackLine}", problem: true);
    }

    /// <summary>Takes the preview away. The text is cleared too, so no stale text stays in the dialog.</summary>
    private void HidePreview()
    {
        _previewText = null;
        PreviewBlock.Visibility = Visibility.Collapsed;
        PreviewBox.Text = string.Empty;
        PreviewNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>The Copy button: puts the shown text on the clipboard. Nothing is sent to a session.</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_previewText is not { } text) return;
        try
        {
            Clipboard.SetText(text);
            PreviewNote.Text = "Copied to the clipboard. Nothing was sent to a session.";
            PreviewNote.ClearValue(TextBlock.ForegroundProperty);
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard: the owner can press Copy again.
            PreviewNote.Text = "The clipboard is busy. Press Copy again.";
            PreviewNote.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        }
        PreviewNote.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Start: the choice and the text shown, and the dialog closes. Start is off unless the choice is ready (<see cref="CopilotResumeRules.CanStart"/>),
    /// so a text is never sent that was not shown.
    /// </summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_blocksStart) return;
        var choice = Selected(out _);
        if (choice is null || _busy) return;
        if (!CopilotResumeRules.CanStart(choice.Mode, _previewText is not null, KeptMatches(choice))) return;
        Choice = choice with { Remember = RememberBox.IsChecked == true, Target = StartTarget(choice.Mode) };
        PreviewText = choice.Mode == CopilotResumeMode.Full ? null : _previewText;
        KeptSummary = _kept;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = null;
        Close();
    }

    /// <summary>A request still being made is cancelled with the dialog: nothing waits for it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        CancelRequest();
        base.OnClosed(e);
    }
}

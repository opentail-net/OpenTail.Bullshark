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
using OpenTail.Bullshark.Core.Setup;

namespace OpenTail.Bullshark.App;

/// <summary>How a past session continues.</summary>
public enum ResumeMode
{
    /// <summary>The same session, with its whole conversation as context.</summary>
    Full,

    /// <summary>A new session that starts from a summary, followed by the last turns.</summary>
    Summary,

    /// <summary>A new session that starts from the last turns only. No model call.</summary>
    TailOnly,
}

/// <summary>
/// The choice made in the resume prompt. Level is set for a summary; Turns is the number of turns kept verbatim. Model and Effort
/// are the model and effort of the resumed or new session (the summary itself is always Haiku at low effort). Remember is the
/// "Remember for this session" tick: the prompt only returns it, and the window writes the mark (Continue, stage C6). Target is the platform
/// the new session starts in (RS-D14). Until the target chooser is built it is always Claude, the screen's own platform.
/// </summary>
public sealed record ResumeChoice(ResumeMode Mode, SummaryLevel? Level, int Turns, string Model, string Effort, bool Remember, ProviderId Target)
{
    /// <summary>A choice whose target is Claude, the screen's own platform. The target is a static field, not a constant, so it cannot be a default value.</summary>
    public ResumeChoice(ResumeMode Mode, SummaryLevel? Level, int Turns, string Model, string Effort, bool Remember = false)
        : this(Mode, Level, Turns, Model, Effort, Remember, ProviderId.Claude) { }
}

/// <summary>
/// What the Claude resume prompt's preview callback gives: the built preview, or, when the launch is refused, null with the refusal in
/// <see cref="Problem"/>. A refused launch builds no text and makes no summary (R4d), so Preview is null exactly when Problem is set.
/// </summary>
public sealed record ResumePromptPreview(ResumePreview? Preview, string? Problem = null);

/// <summary>
/// The resume prompt (D14, D-S11). One list of ways to continue, with the recommended one already selected: a full resume only
/// when the context is small (ResumeAdvice), otherwise a summary. It shows what each choice would send before anything runs. The Resume
/// menu previews that text, and copies it, through the window's own method, so the text shown is the text sent. A summary made for the
/// preview is kept, and Resume uses it without a second model call. The owner picks the model and effort of the session that continues.
/// Cancel is the default.
/// </summary>
public partial class ResumePrompt : Window
{
    private const int MaxTurns = 200;
    private readonly int _contextTokens;
    private readonly Func<ResumeChoice, KeptSummary?, CancellationToken, Task<ResumePromptPreview>>? _previewAsync;
    private CancellationTokenSource? _previewCts;
    private bool _previewing;

    /// <summary>True while the preview on screen is a refusal of the launch: Resume and Copy stay off, as the Codex Start does (R4d).</summary>
    private bool _refused;

    public ResumePrompt(string heading, string facts, long contextTokens, RailPalette palette, string defaultModel, string defaultEffort,
        Func<ResumeChoice, KeptSummary?, CancellationToken, Task<ResumePromptPreview>>? previewAsync = null, ContinueMode? initialMode = null)
    {
        InitializeComponent();
        // The title names the source and the button names the target (RS-D4, RS-O2, RS-O4). The words come from ResumeTarget, the one place.
        Title = ResumeTarget.TitleOf(ProviderId.Claude);
        var startLabel = ResumeTarget.ButtonOf(ProviderId.Claude);
        ResumeButton.Content = startLabel;
        AutomationProperties.SetName(ResumeButton, startLabel);
        _previewAsync = previewAsync;
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

        Heading.Text = heading;
        Facts.Text = facts;
        _contextTokens = (int)Math.Min(contextTokens, int.MaxValue);
        TurnsBox.Text = ResumeText.DefaultTurns.ToString(CultureInfo.InvariantCulture);

        var fullRecommended = ResumeAdvice.FullIsRecommended(contextTokens);
        Recommendation.Text = fullRecommended
            ? $"Recommended: in full. The context is about {_contextTokens:N0} tokens, small enough to re-read before the first reply."
            : $"Recommended: from a summary. The context is about {_contextTokens:N0} tokens; a full resume would re-read all of it " +
              "before the first reply.";
        FullChoice.Content = "In full: the whole conversation as context" + (fullRecommended ? " 👍 (recommended)" : string.Empty);
        SummaryChoice.Content = "From a summary, then a new session" + (fullRecommended ? string.Empty : " 👍 (recommended)");
        // The start: the mode given (CN-D9, stage C4), or with none the recommendation, as before. The recommendation text above does not change.
        (ProviderResumeRules.StartMode(initialMode) switch
        {
            ProviderResumeMode.Full => FullChoice,
            ProviderResumeMode.LastTurns => TailChoice,
            ProviderResumeMode.Summary => SummaryChoice,
            _ => fullRecommended ? FullChoice : SummaryChoice,
        }).IsChecked = true;

        foreach (var (level, label) in new[]
                 {
                     (SummaryLevel.Minimal, "Minimal · about 120 words"),
                     (SummaryLevel.Normal, "Normal · about 400 words"),
                     (SummaryLevel.Complete, "Complete · about 1,200 words"),
                 })
        {
            var segment = Segment(label, "Level", level == SummaryLevel.Normal);
            segment.Tag = level.ToString();
            segment.Checked += (_, _) => UpdateCost();
            LevelRow.Children.Add(segment);
        }

        foreach (var radio in new[] { FullChoice, SummaryChoice, TailChoice }) radio.Checked += (_, _) => UpdateCost();
        TurnsBox.TextChanged += (_, _) => UpdateCost();

        foreach (var model in Choices.Models) ModelRow.Children.Add(Segment(model, "Model", model == defaultModel));
        foreach (var effort in Choices.Efforts) EffortRow.Children.Add(Segment(effort, "Effort", effort == defaultEffort));

        UpdateCost();
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public ResumeChoice? Choice { get; private set; }

    /// <summary>
    /// The summary the preview made (or reused). Resume passes it on, so the summary is not made twice. Null when none was made, or when the
    /// level changed after the preview.
    /// </summary>
    public KeptSummary? KeptSummary { get; private set; }

    /// <summary>One option of a row: a segment that shows as pressed when it is the chosen one.</summary>
    private RadioButton Segment(string text, string group, bool isChecked) => new()
    {
        Content = text,
        Tag = text,
        GroupName = group,
        IsChecked = isChecked,
        Style = (Style)Resources["Segment"],
    };

    private static string? SelectedTag(Panel row) =>
        row.Children.OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true)?.Tag as string;

    /// <summary>The summary level on screen, or null when none is chosen.</summary>
    private SummaryLevel? SelectedLevelOrNull() =>
        Enum.TryParse<SummaryLevel>(SelectedTag(LevelRow), out var level) ? level : (SummaryLevel?)null;

    /// <summary>The summary level on screen. Normal when none is chosen.</summary>
    private SummaryLevel SelectedLevel() => SelectedLevelOrNull() ?? SummaryLevel.Normal;

    /// <summary>The choice on screen, or null when none is selected. turnsValid is false when the turns are not a whole number in range.</summary>
    private ResumeChoice? Selected(out bool turnsValid)
    {
        var parsed = int.TryParse(TurnsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var turns);
        turnsValid = parsed && turns is >= 0 and <= MaxTurns;
        var kept = turnsValid ? turns : ResumeText.DefaultTurns;
        var model = SelectedTag(ModelRow) ?? Choices.Models[0];
        var effort = SelectedTag(EffortRow) ?? Choices.Efforts[0];

        if (FullChoice.IsChecked == true) return new ResumeChoice(ResumeMode.Full, null, 0, model, effort);
        if (SummaryChoice.IsChecked == true) return new ResumeChoice(ResumeMode.Summary, SelectedLevel(), kept, model, effort);
        if (TailChoice.IsChecked == true) return new ResumeChoice(ResumeMode.TailOnly, null, kept, model, effort);
        return null;
    }

    /// <summary>
    /// Runs when the choice, its level or its turns change. A preview on screen is then stale, so it is taken away. A kept summary stays only
    /// while its level is the one chosen. The model and the effort do not change the text, so they do not come here.
    /// </summary>
    private void UpdateCost()
    {
        HidePreview();
        if (KeptSummary is { } kept && !kept.MatchesLevel(SelectedLevelOrNull())) KeptSummary = null;
        Refresh();
    }

    /// <summary>
    /// Says what the selected choice costs and sends, before the owner presses Resume. It also shows only the options that apply to
    /// the choice: the summary levels for a summary, and the turns for anything but a full resume. And it sets what can be pressed: while a
    /// preview is being made, the choice, the Resume split button and its menu are off.
    /// </summary>
    private void Refresh()
    {
        var choice = Selected(out var turnsValid);
        LevelBlock.Visibility = SummaryChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TurnsBlock.Visibility = FullChoice.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

        var sizes = turnsValid ? string.Empty : $" Enter a whole number from 0 to {MaxTurns} for the turns.";
        var runs = choice is null ? string.Empty : $" The new session runs at {choice.Model} {choice.Effort}.";
        CostText.Text = _previewing
            ? (NeedsSummaryCall(choice) ? "Summarising: one model call…" : "Building the prompt…")
            : (choice?.Mode switch
            {
                ResumeMode.Full =>
                    $"Resuming in full re-reads all of the context, about {_contextTokens:N0} tokens, before the first reply. " +
                    "A warm cache makes that cheap; a cold one charges it at full price. Nothing runs until you press Resume.",
                ResumeMode.Summary =>
                    $"One Haiku call (low effort) reads your messages and Claude's replies, not the tool output, up to about " +
                    $"{ResumeText.SummaryInputChars / 4:N0} tokens, and writes the {(choice.Level ?? SummaryLevel.Normal).ToString().ToLowerInvariant()} " +
                    "summary. Then a new session starts from it and from the last turns.",
                ResumeMode.TailOnly =>
                    "No model call. The last turns are copied word for word into a new session.",
                _ => "Choose one of the ways to continue.",
            }) + runs + sizes;

        var free = !_previewing;
        Enable(FullChoice, free);
        Enable(SummaryChoice, free);
        Enable(TailChoice, free);
        Enable(TurnsBox, free);
        foreach (var segment in LevelRow.Children.OfType<RadioButton>()) Enable(segment, free);
        ResumeButton.IsEnabled = free && !_refused;
        ResumeMenuButton.IsEnabled = free;
        Enable(RememberBox, free);

        var canBuild = free && choice is not null && choice.Mode != ResumeMode.Full && turnsValid && _previewAsync is not null;
        PreviewItem.IsEnabled = canBuild;
        CopyItem.IsEnabled = canBuild && !_refused;
        PreviewItem.ToolTip = MenuTip(choice, turnsValid, copy: false);
        CopyItem.ToolTip = MenuTip(choice, turnsValid, copy: true);
    }

    /// <summary>True when the choice needs a model call that has not been made yet: a summary, and no kept summary of its level.</summary>
    private bool NeedsSummaryCall(ResumeChoice? choice) =>
        choice?.Mode == ResumeMode.Summary && !(KeptSummary?.MatchesLevel(choice.Level) ?? false);

    /// <summary>The tooltip of the Preview and the Copy items: why an item is off, or what it does, and whether it makes the model call.</summary>
    private string MenuTip(ResumeChoice? choice, bool turnsValid, bool copy)
    {
        if (choice?.Mode == ResumeMode.Full) return "Resuming in full sends no prompt.";
        if (!turnsValid) return "Enter a whole number of turns first.";
        if (choice is null || _previewAsync is null) return "There is no prompt to preview here.";
        if (choice.Mode == ResumeMode.TailOnly)
        {
            return copy
                ? "Copies the last turns, word for word, to the clipboard. No model call, and nothing is sent to a session."
                : "Shows the last turns, word for word, as the new session would get them. No model call.";
        }
        if (!NeedsSummaryCall(choice))
        {
            return copy
                ? "Copies the start text, with the summary already made. No model call."
                : "Shows the start text, with the summary already made. No model call.";
        }
        return copy
            ? "Makes the summary now (one Haiku call), keeps it for Resume, and copies the start text to the clipboard. Nothing is sent to a session."
            : "Makes the summary now (one Haiku call) and keeps it: Resume then uses it without a second call. Cancelling after this still spends the call.";
    }

    private static void Enable(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : 0.5;
    }

    /// <summary>The Preview item: builds the prompt and shows it. Nothing is started.</summary>
    private async void PreviewItem_Click(object sender, RoutedEventArgs e) => await BuildAsync(copy: false);

    /// <summary>The Copy item: builds the prompt, shows it, and puts it on the clipboard. Nothing is sent to a session.</summary>
    private async void CopyItem_Click(object sender, RoutedEventArgs e) => await BuildAsync(copy: true);

    /// <summary>The narrow part of the split button: opens the menu under it.</summary>
    private void ResumeMenuButton_Click(object sender, RoutedEventArgs e)
    {
        ResumeMenu.PlacementTarget = ResumeMenuButton;
        ResumeMenu.IsOpen = true;
    }

    /// <summary>
    /// Builds the prompt for the chosen way, through the window's method that the start uses, shows it, and copies it when asked. A failure is
    /// shown in the pane and the dialog stays open. A build cancelled because the dialog closed shows nothing.
    /// </summary>
    private async Task BuildAsync(bool copy)
    {
        var choice = Selected(out var turnsValid);
        if (_previewAsync is null || _previewing || choice is null || choice.Mode == ResumeMode.Full || !turnsValid) return;

        _previewing = true;
        _refused = false;
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        Refresh();
        try
        {
            var built = await _previewAsync(choice, KeptSummary, cts.Token);
            if (built.Preview is not { } preview)
            {
                _refused = true;
                ShowRefusal(built.Problem ?? "The prompt could not be built.");
                return;
            }
            if (preview.Summary is { } summary) KeptSummary = summary;
            ShowPreview(preview);
            if (copy) CopyText(preview.Text);
        }
        catch (OperationCanceledException)
        {
            // The dialog closed while the summary was being made. The window is gone, so nothing is shown.
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _previewing = false;
            _previewCts = null;
            cts.Dispose();
            Refresh();
        }
    }

    /// <summary>Shows a built prompt: its size line (in the danger colour when it does not fit), and the text, read-only.</summary>
    private void ShowPreview(ResumePreview preview)
    {
        PreviewHeader.Text = ResumePreviewText.Header(preview.Text.Length, preview.CutCharacters, preview.Fits);
        PreviewHeader.SetResourceReference(TextBlock.ForegroundProperty, preview.Fits ? "PromptMuted" : "UiDanger");
        PreviewText.Text = preview.Text;
        PreviewText.Visibility = Visibility.Visible;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
        PreviewText.CaretIndex = 0;
        PreviewText.ScrollToHome();
    }

    /// <summary>
    /// Shows why the launch is refused: the refusal in the danger colour, and no prompt text. It is the Codex preview's refusal, shown the same
    /// way (R4d). Resume and Copy stay off while it shows; the dialog stays open, so the owner can change the choice, update the tool or cancel.
    /// </summary>
    private void ShowRefusal(string problem)
    {
        PreviewHeader.Text = problem;
        PreviewHeader.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        PreviewText.Text = string.Empty;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
    }

    /// <summary>Shows why the prompt could not be built. The dialog stays open, so the owner can change the choice or cancel.</summary>
    private void ShowError(string message)
    {
        PreviewHeader.Text = $"The prompt could not be built: {message}";
        PreviewHeader.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        PreviewText.Text = string.Empty;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
    }

    /// <summary>Puts the prompt on the clipboard and says so under the text. The clipboard is the only place it goes.</summary>
    private void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            PreviewNote.Text = "Copied to the clipboard. Nothing was sent to a session.";
            PreviewNote.SetResourceReference(TextBlock.ForegroundProperty, "PromptMuted");
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard: the owner can press Copy again.
            PreviewNote.Text = "The clipboard is busy. Press Copy the prompt again.";
            PreviewNote.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        }
        PreviewNote.Visibility = Visibility.Visible;
    }

    /// <summary>Takes the preview away. The text is cleared too, so no stale prompt stays in the dialog. A refusal shown there goes with it.</summary>
    private void HidePreview()
    {
        _refused = false;
        PreviewBlock.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        PreviewNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>A summary still being made is cancelled with the dialog: nothing waits for it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _previewCts?.Cancel();
        base.OnClosed(e);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out var turnsValid);
        if (choice is null) return;
        if (!turnsValid && choice.Mode != ResumeMode.Full)
        {
            UpdateCost();
            return;
        }

        Choice = choice with { Remember = RememberBox.IsChecked == true, Target = ProviderId.Claude };
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = null;
        Close();
    }
}

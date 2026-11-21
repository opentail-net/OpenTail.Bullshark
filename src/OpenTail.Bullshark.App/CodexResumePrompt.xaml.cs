using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;
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
/// id the new session is started with (P9), or null for Codex's own default (also the case for In full, which sends no model).
/// </summary>
public sealed record CodexResumeChoice(CodexResumeMode Mode, SummaryLevel? Level, int Turns, string? Model = null);

/// <summary>
/// What the screen's preview callback gives: the start text (empty when the session has no messages), the summary it made or reused (null for
/// none), and the reason no text was made (null when there is text, or when the session has no messages).
/// </summary>
public sealed record CodexPreview(string Text, KeptSummary? KeptSummary, string? Problem = null);

/// <summary>
/// The Codex resume screen (step R5): a copy of Claude's resume prompt for a Codex session, with three ways to continue. Nothing runs until Start.
/// The last-turns text is built when the mode, the turns or the window changes (no model call). A summary is made only by the Make summary button, and it
/// is kept for the level and turns it was made at. The start text in the preview is the text Start sends (<see cref="PreviewText"/>). Cancel is the
/// default. The callback runs off the UI thread and can be cancelled: a change or the close cancels the one in progress.
/// </summary>
public partial class CodexResumePrompt : Window
{
    private readonly Func<CodexResumeChoice, KeptSummary?, CancellationToken, Task<CodexPreview>>? _previewAsync;
    private CancellationTokenSource? _cts;
    private int _request;
    private bool _busy;
    private string? _previewText;
    private KeptSummary? _kept;

    /// <summary>True once the model list is in (or none was asked for). Start of a new session waits for it; In full does not need it.</summary>
    private bool _modelsReady;

    /// <param name="heading">The heading: the session's name.</param>
    /// <param name="facts">The line under the heading: turns, last activity and folder.</param>
    /// <param name="palette">The theme, as the rest of the window's dialogs use it.</param>
    /// <param name="previewAsync">Makes the start text for a choice. It is given the summary kept for the choice (or null), and returns the text and the summary it used.</param>
    /// <param name="models">The model list, read off the UI thread. The model box shows that it is loading until the list is in, and Start waits for it (except In full). Null: no list, so Codex's default.</param>
    public CodexResumePrompt(string heading, string facts, RailPalette palette,
        Func<CodexResumeChoice, KeptSummary?, CancellationToken, Task<CodexPreview>>? previewAsync = null,
        Task<CodexModelReading>? models = null)
    {
        InitializeComponent();
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
        FullChoice.Content = CodexResumeRules.ModeLabel(CodexResumeMode.Full);
        TailChoice.Content = CodexResumeRules.ModeLabel(CodexResumeMode.LastTurns);
        SummaryChoice.Content = CodexResumeRules.ModeLabel(CodexResumeMode.Summary);
        TurnsBox.Text = CodexResumeRules.DefaultTurns.ToString(CultureInfo.InvariantCulture);

        foreach (var level in new[] { SummaryLevel.Minimal, SummaryLevel.Normal, SummaryLevel.Complete })
        {
            var segment = Segment(CodexResumeRules.LevelLabel(level), "CodexLevel", level == SummaryLevel.Normal);
            segment.Tag = level.ToString();
            segment.Checked += (_, _) => OnChoiceChanged();
            LevelRow.Children.Add(segment);
        }
        foreach (var radio in new[] { FullChoice, TailChoice, SummaryChoice }) radio.Checked += (_, _) => OnChoiceChanged();
        TurnsBox.TextChanged += (_, _) => OnChoiceChanged();

        OnChoiceChanged();

        if (models is null)
        {
            _modelsReady = true;
            ModelNote.Text = CodexModelChoices.Fill(ModelBox, null);
            Refresh();
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
        Refresh();
    }

    /// <summary>The model id on screen for a new session, or null for Codex's default (the one entry of a list that could not be read).</summary>
    private string? SelectedModel() => CodexModelChoices.SelectedModel(ModelBox);

    /// <summary>Start is allowed when the model is known: In full sends no model, so it never waits for the list.</summary>
    private bool ModelReady(CodexResumeMode mode) => mode == CodexResumeMode.Full || _modelsReady;

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public CodexResumeChoice? Choice { get; private set; }

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
    private CodexResumeChoice? Selected(out bool turnsValid)
    {
        var valid = CodexResumeRules.TryTurns(TurnsBox.Text, out var turns);
        if (FullChoice.IsChecked == true)
        {
            turnsValid = true;
            return new CodexResumeChoice(CodexResumeMode.Full, null, 0);
        }
        turnsValid = valid;
        if (TailChoice.IsChecked == true) return new CodexResumeChoice(CodexResumeMode.LastTurns, null, turns, SelectedModel());
        if (SummaryChoice.IsChecked == true) return new CodexResumeChoice(CodexResumeMode.Summary, SelectedLevel(), turns, SelectedModel());
        return null;
    }

    /// <summary>True when the summary the dialog holds is for the choice's level. It is kept only for the level and turns it was made at (R-D3).</summary>
    private bool KeptMatches(CodexResumeChoice? choice) => choice?.Level is { } level && _kept is not null && _kept.MatchesLevel(level);

    /// <summary>
    /// Runs when the mode, the level or the turns change. The preview on screen is for the old choice, so it is taken away. A summary depends on the
    /// session and the level only (R-D3): a change of mode or of turns keeps the kept summary, and a change of level drops it. The preview is built
    /// again at once when it needs no model call: the last turns, or a summary kept for the level on screen.
    /// </summary>
    private void OnChoiceChanged()
    {
        CancelRequest();
        _busy = false;
        var choice = Selected(out var turnsValid);
        if (_kept is not null && choice is { Mode: CodexResumeMode.Summary } && choice.Level != _kept.Level) _kept = null;
        HidePreview();
        Refresh();
        if (!turnsValid || choice is null) return;
        if (choice.Mode == CodexResumeMode.LastTurns || (choice.Mode == CodexResumeMode.Summary && KeptMatches(choice))) _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// Sets what is on and off for the choice: the cost line, the choices, the level row (summary only), the turns box (not in full), Make summary
    /// (summary only), Copy, and Start (<see cref="CodexResumeRules.CanStart"/>). While a summary is made, the choices and the buttons are off.
    /// </summary>
    private void Refresh()
    {
        var choice = Selected(out var turnsValid);
        var mode = choice?.Mode ?? CodexResumeMode.LastTurns;
        var free = !_busy;
        var turnsApply = mode != CodexResumeMode.Full;
        CostText.Text = CodexResumeRules.CostLine(mode) + (turnsApply && !turnsValid ? " " + CodexResumeRules.TurnsMessage : string.Empty);

        Enable(FullChoice, free);
        Enable(TailChoice, free);
        Enable(SummaryChoice, free);
        Enable(LevelRow, free && mode == CodexResumeMode.Summary);
        Enable(TurnsBox, free && turnsApply);
        Enable(MakeSummaryButton, free && mode == CodexResumeMode.Summary && turnsValid);
        var previewReady = _previewText is not null;
        Enable(CopyButton, free && previewReady);
        var modelRow = mode != CodexResumeMode.Full;
        ModelBlock.Visibility = modelRow ? Visibility.Visible : Visibility.Collapsed;
        Enable(ModelBox, free && modelRow && _modelsReady);
        Enable(StartButton, free && ModelReady(mode) && CodexResumeRules.CanStart(mode, previewReady, KeptMatches(choice)));
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
        if (choice is { Mode: CodexResumeMode.Summary } && turnsValid && !_busy) _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// For the screenshots only (the App's <c>--codex-resume summary</c>): chooses the summary mode, at the level on screen, and presses Make summary
    /// once. Nothing else calls it.
    /// </summary>
    public void MakeSummaryForScreenshot()
    {
        SummaryChoice.IsChecked = true;
        MakeSummary_Click(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Makes the start text for the choice, off the UI thread, and shows it. A summary holds the choices and the buttons until it is made. A request
    /// replaces the one before it: the older one is cancelled and its result is dropped. A failure is shown in the preview area; a cancel shows
    /// nothing. Never throws: every failure is shown on screen.
    /// </summary>
    private async Task RunRequestAsync(CodexResumeChoice choice)
    {
        if (_previewAsync is null) return;
        CancelRequest();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var id = _request;
        _busy = choice.Mode == CodexResumeMode.Summary;
        ShowWorking(choice);
        Refresh();
        try
        {
            var preview = await _previewAsync(choice, _kept, cts.Token);
            if (id != _request) return;
            if (preview.KeptSummary is { } made) _kept = made;
            ShowPreview(preview);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a change of the choice, or by the dialog closing: nothing is shown.
        }
        catch (Exception ex)
        {
            if (id == _request) ShowMessage($"The start text could not be made: {ex.Message}", problem: true);
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

    /// <summary>Shows that the text for the choice is being made. Summary says a Codex call is made.</summary>
    private void ShowWorking(CodexResumeChoice choice) => ShowMessage(
        choice.Mode == CodexResumeMode.Summary
            ? "Making the summary: one Codex call. Nothing is sent to a session."
            : "Reading the session. Nothing is sent to a session.",
        problem: false);

    /// <summary>
    /// Shows a built text: its size line, and the text read-only. A preview with no text says why there is none: the window's problem, or that the
    /// session has no messages to start from (Start stays off).
    /// </summary>
    private void ShowPreview(CodexPreview preview)
    {
        if (preview.Text.Length == 0)
        {
            ShowMessage(preview.Problem ?? "This session has no messages to continue from, so Start is off.", problem: preview.Problem is not null);
            return;
        }
        _previewText = preview.Text;
        PreviewHeader.Text = CodexResumeRules.SizeLine(preview.Text.Length);
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
    /// Start: the choice and the text shown, and the dialog closes. Start is off unless the choice is ready (<see cref="CodexResumeRules.CanStart"/>),
    /// so a text is never sent that was not shown.
    /// </summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out _);
        if (choice is null || _busy) return;
        if (!ModelReady(choice.Mode)) return;
        if (!CodexResumeRules.CanStart(choice.Mode, _previewText is not null, KeptMatches(choice))) return;
        Choice = choice;
        PreviewText = choice.Mode == CodexResumeMode.Full ? null : _previewText;
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

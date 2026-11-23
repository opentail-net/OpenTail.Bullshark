using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The opencode resume screen (OC3, a copy of the Copilot resume screen with opencode's words, OC-D16). Three ways to continue: In full (the session in
/// opencode's terminal, no text), From the last turns (one model turn on the owner's login, OC-D9), and a summary (one model call on the owner's login,
/// made only by Make summary; OC-O4 decided 2026-11-23, allowed). There is no target chooser (OC-D18). Nothing runs until Start or Make summary. The
/// last-turns text is built when the choice changes, with no model call; the preview box shows exactly the text Start sends (<see cref="PreviewText"/>).
/// </summary>
public partial class OpenCodeResumePrompt : Window
{
    /// <summary>The title of the screen (OC-D18).</summary>
    public const string OpenCodeTitle = "Resume an opencode session";

    /// <summary>
    /// The Start button's text. opencode is not a cross-platform target in this version (OC-D18), so the text is not <c>ResumeTarget.ButtonOf</c>.
    /// </summary>
    public const string OpenCodeStartText = "Start in opencode";

    private readonly Func<OpenCodeResumeChoice, CancellationToken, Task<OpenCodePreview>>? _previewAsync;
    private CancellationTokenSource? _cts;
    private int _request;
    private string? _previewText;

    /// <summary>The folder notice shown in red, or null. Start is off only with no folder on record; the choices and Copy stay on.</summary>
    private readonly string? _folderNotice;

    /// <summary>True only for a session with no folder on record: Start is blocked. A missing folder does not block it: Start makes it again.</summary>
    private readonly bool _blocksStart;

    /// <param name="heading">The heading: the session's name.</param>
    /// <param name="facts">The line under the heading: turns, last activity and folder.</param>
    /// <param name="palette">The theme, as the rest of the window's dialogs use it.</param>
    /// <param name="previewAsync">
    /// Makes the start text of the choice: the last turns when the choice is made, a summary when Make summary is pressed. It is called for the choice on
    /// screen only.
    /// </param>
    /// <param name="initialMode">The mode the screen opens on. Null keeps the last turns. A summary opens its radio; nothing is made until Make summary.</param>
    /// <param name="folder">The session's folder, for the folder notice. Used only with <paramref name="folderExists"/>.</param>
    /// <param name="folderExists">Whether the folder exists. Null: no folder notice is shown.</param>
    public OpenCodeResumePrompt(string heading, string facts, RailPalette palette,
        Func<OpenCodeResumeChoice, CancellationToken, Task<OpenCodePreview>>? previewAsync = null, ContinueMode? initialMode = null,
        string? folder = null, bool? folderExists = null)
    {
        InitializeComponent();
        // The window grows to the screen's work area at most: the body scrolls and the buttons stay in view.
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        Title = OpenCodeTitle;
        StartButton.Content = OpenCodeStartText;
        AutomationProperties.SetName(StartButton, OpenCodeStartText);
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
        FullChoice.Content = OpenCodeResumeRules.ModeLabel(OpenCodeResumeMode.Full);
        TailChoice.Content = OpenCodeResumeRules.ModeLabel(OpenCodeResumeMode.LastTurns);
        SummaryChoice.Content = OpenCodeResumeRules.ModeLabel(OpenCodeResumeMode.Summary);
        MinimalLevel.Content = ProviderResumeRules.LevelLabel(SummaryLevel.Minimal);
        NormalLevel.Content = ProviderResumeRules.LevelLabel(SummaryLevel.Normal);
        CompleteLevel.Content = ProviderResumeRules.LevelLabel(SummaryLevel.Complete);
        TurnsBox.Text = OpenCodeResumeRules.DefaultTurns.ToString(CultureInfo.InvariantCulture);

        // The start mode is set before the handlers below are attached, so the first build runs once, for the mode it opens on. Null keeps the XAML's
        // last-turns radio. Opening on the summary checks its radio only: its call is made by Make summary, never by opening.
        switch (ProviderResumeRules.StartMode(initialMode))
        {
            case ProviderResumeMode.Full: FullChoice.IsChecked = true; break;
            case ProviderResumeMode.Summary: SummaryChoice.IsChecked = true; break;
            case ProviderResumeMode.LastTurns: TailChoice.IsChecked = true; break;
        }

        foreach (var radio in new[] { FullChoice, TailChoice, SummaryChoice }) radio.Checked += (_, _) => OnChoiceChanged();
        foreach (var level in new[] { MinimalLevel, NormalLevel, CompleteLevel }) level.Checked += (_, _) => OnChoiceChanged();
        TurnsBox.TextChanged += (_, _) => OnChoiceChanged();

        OnChoiceChanged();
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public OpenCodeResumeChoice? Choice { get; private set; }

    /// <summary>
    /// The start text the window sends: exactly the text that was shown, so nothing is built again for the start. Null for In full, which sends no text.
    /// </summary>
    public string? PreviewText { get; private set; }

    /// <summary>
    /// The choice on screen, or null when none is usable. <paramref name="turnsValid"/> is false when the turns box is not a valid number; In full needs no
    /// turns, so it is always valid.
    /// </summary>
    private OpenCodeResumeChoice? Selected(out bool turnsValid)
    {
        var valid = OpenCodeResumeRules.TryTurns(TurnsBox.Text, out var turns);
        if (FullChoice.IsChecked == true)
        {
            turnsValid = true;
            return new OpenCodeResumeChoice(OpenCodeResumeMode.Full, 0, Remember: false);
        }
        turnsValid = valid;
        if (TailChoice.IsChecked == true) return new OpenCodeResumeChoice(OpenCodeResumeMode.LastTurns, turns, Remember: false);
        if (SummaryChoice.IsChecked == true)
            return new OpenCodeResumeChoice(OpenCodeResumeMode.Summary, turns, false, SelectedLevel());
        return null;
    }

    /// <summary>The summary level on screen. Normal when none is chosen.</summary>
    private SummaryLevel SelectedLevel() =>
        MinimalLevel.IsChecked == true ? SummaryLevel.Minimal
        : CompleteLevel.IsChecked == true ? SummaryLevel.Complete
        : SummaryLevel.Normal;

    /// <summary>
    /// Runs when the mode, the level or the turns change. The preview on screen is for the old choice, so it is taken away. The last-turns preview is built
    /// again at once; In full and a summary build nothing (a summary is made only by Make summary).
    /// </summary>
    private void OnChoiceChanged()
    {
        CancelRequest();
        var choice = Selected(out var turnsValid);
        HidePreview();
        Refresh();
        if (!turnsValid || choice is null) return;
        if (OpenCodeResumeRules.RunsAtOnce(choice.Mode)) _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// Sets what is on and off for the choice: the cost line (for a summary, its call line, and once a summary is shown, the start line), the level row
    /// and Make summary (the summary only), the turns box (not in full), Copy, and Start (<see cref="OpenCodeResumeRules.CanStart"/>). The folder notice
    /// is shown when there is one.
    /// </summary>
    private void Refresh()
    {
        var choice = Selected(out var turnsValid);
        var mode = choice?.Mode ?? (SummaryChoice.IsChecked == true ? OpenCodeResumeMode.Summary : OpenCodeResumeMode.LastTurns);
        var previewReady = _previewText is not null;
        CostText.Text = OpenCodeResumeRules.CostLine(mode)
                        + (mode == OpenCodeResumeMode.Summary && previewReady ? " " + OpenCodeResumeRules.LastTurnsCostLine : string.Empty)
                        + (mode != OpenCodeResumeMode.Full && !turnsValid ? " " + OpenCodeResumeRules.TurnsMessage : string.Empty);
        FolderNotice.Text = _folderNotice ?? string.Empty;
        FolderNotice.Visibility = _folderNotice is null ? Visibility.Collapsed : Visibility.Visible;

        Enable(LevelRow, mode == OpenCodeResumeMode.Summary);
        Enable(TurnsBox, mode != OpenCodeResumeMode.Full);
        // Make summary is off while a request runs, so one summary call is made at a time in this screen.
        Enable(MakeSummaryButton, mode == OpenCodeResumeMode.Summary && turnsValid && _cts is null);
        Enable(CopyButton, previewReady);
        Enable(StartButton, choice is not null && !_blocksStart && OpenCodeResumeRules.CanStart(choice.Mode, previewReady));
    }

    private static void Enable(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : 0.5;
    }

    /// <summary>
    /// Makes the last-turns start text, off the UI thread, and shows it. A request replaces the one before it: the older one is cancelled and its result is
    /// dropped. A failure is shown in the preview area; a cancel shows nothing. Never throws: every failure is shown on screen.
    /// </summary>
    private async Task RunRequestAsync(OpenCodeResumeChoice choice)
    {
        if (_previewAsync is null) return;
        CancelRequest();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var id = _request;
        ShowMessage(
            choice.Mode == OpenCodeResumeMode.Summary ? OpenCodeResumeRules.SummaryRunningLine : "Reading the session. Nothing is sent to a session.",
            problem: false);
        Refresh();
        try
        {
            var preview = await _previewAsync(choice, cts.Token);
            if (id != _request) return;
            if (preview.Problem is { } problem && preview.Text.Length == 0) ShowMessage(problem, problem: true);
            else ShowPreview(preview);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a change of the choice, or by the dialog closing: nothing is shown, and the selection stays.
        }
        catch (Exception ex)
        {
            if (id == _request) ShowMessage($"The start text could not be made ({ex.GetType().Name}).", problem: true);
        }
        finally
        {
            if (id == _request)
            {
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

    /// <summary>Shows a built text: its size line, and the text read-only. A preview with no text says why there is none, and Start stays off.</summary>
    private void ShowPreview(OpenCodePreview preview)
    {
        if (preview.Text.Length == 0)
        {
            ShowMessage(preview.Problem ?? "This session has no messages to continue from, so Start is off.", problem: preview.Problem is not null);
            return;
        }
        _previewText = preview.Text;
        PreviewHeader.Text = OpenCodeResumeRules.SizeLine(preview.Text.Length);
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
    /// Make summary: makes the summary now, once, for the level and the turns on screen. It is the only way a summary is made. The shown text is the
    /// summary and then the last turns; nothing is saved.
    /// </summary>
    private void MakeSummary_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out var turnsValid);
        if (choice is not { Mode: OpenCodeResumeMode.Summary } || !turnsValid) return;
        _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// Start: the choice and the text shown, and the dialog closes. Start is off unless the choice is ready
    /// (<see cref="OpenCodeResumeRules.CanStart"/>), so a text is never sent that was not shown.
    /// </summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_blocksStart) return;
        var choice = Selected(out _);
        if (choice is null) return;
        if (!OpenCodeResumeRules.CanStart(choice.Mode, _previewText is not null)) return;
        Choice = choice with { Remember = RememberBox.IsChecked == true };
        PreviewText = choice.Mode == OpenCodeResumeMode.Full ? null : _previewText;
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

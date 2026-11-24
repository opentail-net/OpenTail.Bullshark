using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The opencode resume screen (OC3, a copy of the Copilot resume screen with opencode's words, OC-D16). Two ways to continue: Full (the session in
/// opencode's terminal, no text) and Summary (one model call on the owner's login, made only by Make summary; OC-O4). The summary has the levels Zero
/// (no summary; the tail is the start text), Minimal, Normal and Complete, and the tail, "Include last [N] turns" (RS-D3, RS-D19 to RS-D26). There is no
/// target chooser (OC-D18). Nothing runs until Start or Make summary. The Zero text is built when the choice changes, with no summary call; the preview box
/// shows exactly the text Start sends (<see cref="PreviewText"/>).
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
    private readonly Func<int, bool>? _summaryHasLines;
    private CancellationTokenSource? _cts;
    private int _request;
    private readonly ResumePreviewPane _preview;

    /// <summary>The summary made for the setting on screen, or for an earlier one; it is used again only for the setting it was made at (RS-D21).</summary>
    private OpenCodePreview? _keptPreview;

    /// <summary>The transcript's line count the screen holds: the count at open, then the count each build read (RS-D21, FIX 1 of the S6c review).</summary>
    private int? _lines;

    /// <summary>The folder notice shown in red, or null. Start is off only with no folder on record; the choices and Copy stay on.</summary>
    private readonly string? _folderNotice;

    /// <summary>True only for a session with no folder on record: Start is blocked. A missing folder does not block it: Start makes it again.</summary>
    private readonly bool _blocksStart;

    /// <param name="heading">The heading: the session's name.</param>
    /// <param name="facts">The line under the heading: turns, last activity and folder.</param>
    /// <param name="palette">The theme, as the rest of the window's dialogs use it.</param>
    /// <param name="previewAsync">
    /// Makes the start text of the choice: the Zero text when the choice is Zero, a summary and the tail when Make summary is pressed. It is called for the
    /// choice on screen only.
    /// </param>
    /// <param name="initialMode">The mode the screen opens on. Null opens Summary at Zero with the tail (no call). A stored last-turns mode opens the same way.
    /// Opening on a mode never makes a summary.</param>
    /// <param name="folder">The session's folder, for the folder notice. Used only with <paramref name="folderExists"/>.</param>
    /// <param name="folderExists">Whether the folder exists. Null: no folder notice is shown.</param>
    /// <param name="summaryHasLines">True when the summary of the session, with the given tail turns, has messages to read (RS-D26). Null: not known, so Make summary is on.</param>
    /// <param name="lines">The transcript's line count when the screen opens, or null when it is not known.</param>
    public OpenCodeResumePrompt(string heading, string facts, RailPalette palette,
        Func<OpenCodeResumeChoice, CancellationToken, Task<OpenCodePreview>>? previewAsync = null, ContinueMode? initialMode = null,
        string? folder = null, bool? folderExists = null, Func<int, bool>? summaryHasLines = null, int? lines = null)
    {
        InitializeComponent();
        _lines = lines;
        _preview = new ResumePreviewPane(PreviewHeader, PreviewBox, PreviewBlock, PreviewNote);
        // The window grows to the screen's work area at most: the body scrolls and the buttons stay in view.
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        Title = OpenCodeTitle;
        StartButton.Content = OpenCodeStartText;
        AutomationProperties.SetName(StartButton, OpenCodeStartText);
        _previewAsync = previewAsync;
        _summaryHasLines = summaryHasLines;
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
        FullChoice.Content = ResumeSummaryState.FullLabel;
        SummaryChoice.Content = ResumeSummaryState.SummaryLabel;
        ZeroLevel.Content = ResumeSummaryState.ZeroLabel;
        MinimalLevel.Content = ProviderResumeRules.LevelLabel(SummaryLevel.Minimal);
        NormalLevel.Content = ProviderResumeRules.LevelLabel(SummaryLevel.Normal);
        CompleteLevel.Content = ProviderResumeRules.LevelLabel(SummaryLevel.Complete);
        TailBox.IsChecked = true;
        TurnsBox.Text = OpenCodeResumeRules.DefaultTurns.ToString(CultureInfo.InvariantCulture);

        // The start mode is set before the handlers below are attached, so the first build runs once, for the mode it opens on. Full opens Full. A summary
        // opens at its level (Normal); a stored last-turns mode and no mode open at Zero with the tail (RS-D27), and make no call.
        var start = ProviderResumeRules.StartMode(initialMode);
        if (start == ProviderResumeMode.Full) FullChoice.IsChecked = true;
        else SummaryChoice.IsChecked = true;
        var atZero = start is null || ProviderResumeRules.OpensAtZeroWithTail(initialMode);
        (atZero ? ZeroLevel : NormalLevel).IsChecked = true;

        foreach (var radio in new[] { FullChoice, SummaryChoice }) radio.Checked += (_, _) => OnChoiceChanged();
        foreach (var level in new[] { ZeroLevel, MinimalLevel, NormalLevel, CompleteLevel }) level.Checked += (_, _) => OnChoiceChanged();
        TailBox.Checked += (_, _) => OnChoiceChanged();
        TailBox.Unchecked += (_, _) => OnChoiceChanged();
        TurnsBox.TextChanged += (_, _) => OnChoiceChanged();

        OnChoiceChanged();
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public OpenCodeResumeChoice? Choice { get; private set; }

    /// <summary>
    /// The start text the window sends: exactly the text that was shown, so nothing is built again for the start. Null for Full, which sends no text.
    /// </summary>
    public string? PreviewText { get; private set; }

    /// <summary>True when the tail is on: the box is ticked (RS-D19).</summary>
    private bool TailOn => TailBox.IsChecked == true;

    /// <summary>
    /// The choice on screen, or null when none is usable. <paramref name="turnsValid"/> is false when the tail is on and the turns box is not a valid number;
    /// Full and a tail that is off need no turns, so they are always valid.
    /// </summary>
    private OpenCodeResumeChoice? Selected(out bool turnsValid)
    {
        var valid = OpenCodeResumeRules.TryTurns(TurnsBox.Text, out var turns);
        if (FullChoice.IsChecked == true)
        {
            turnsValid = true;
            return new OpenCodeResumeChoice(OpenCodeResumeMode.Full, 0, Remember: false);
        }
        if (SummaryChoice.IsChecked != true)
        {
            turnsValid = false;
            return null;
        }
        turnsValid = !TailOn || valid;
        return new OpenCodeResumeChoice(OpenCodeResumeMode.Summary, ResumeSummaryState.TurnsOf(TailOn, turns), false, SelectedLevel());
    }

    /// <summary>The summary level on screen: null for Zero, and Normal when none is chosen.</summary>
    private SummaryLevel? SelectedLevel() =>
        ZeroLevel.IsChecked == true ? null
        : MinimalLevel.IsChecked == true ? SummaryLevel.Minimal
        : CompleteLevel.IsChecked == true ? SummaryLevel.Complete
        : SummaryLevel.Normal;

    /// <summary>
    /// Runs when the mode, the level, the tail or its turns change. The preview on screen is for the old choice, so it is taken away. Zero is built at once
    /// (no call), a summary kept for the setting on screen is shown again, and nothing else is built: a summary is made only by Make summary (RS-D21).
    /// </summary>
    private void OnChoiceChanged()
    {
        CancelRequest();
        HidePreview();
        var choice = Selected(out var turnsValid);
        Refresh();
        if (choice is null || !turnsValid || choice.Mode != OpenCodeResumeMode.Summary) return;
        if (ResumeSummaryState.ZeroRefusal(choice.Level, TailOn, choice.Turns) is not null) return;
        // Zero is not built on a change for now (no automatic reads). The return stays, so Zero never shows a kept summary.
        if (choice.Level is null) return;
        if (_keptPreview is { KeptSummary: { } kept } shown && ResumeSummaryState.KeptMatches(choice.Level, choice.Turns, _lines, kept))
        {
            ShowPreview(shown);
            Refresh();
        }
    }

    /// <summary>
    /// Sets what is on and off for the choice: the cost line and its status line (RS-D21, RS-D25, RS-D26), the level row, the tail and Make summary (the
    /// summary only), the turns box (the tail on), Copy, and Start (<see cref="ResumeSummaryState.StartAllowed"/>).
    /// </summary>
    private void Refresh()
    {
        var choice = Selected(out var turnsValid);
        var mode = choice?.Mode ?? OpenCodeResumeMode.Full;
        var inSummary = mode == OpenCodeResumeMode.Summary;
        var level = choice?.Level;
        var turns = choice is null ? 0 : choice.Turns;
        var previewReady = _preview.Text is not null;
        var kept = _keptPreview?.KeptSummary;
        var zeroRefusal = inSummary ? ResumeSummaryState.ZeroRefusal(level, TailOn, turns) : null;
        var hasLines = _summaryHasLines?.Invoke(turns) ?? true;
        var nothingToSummarise = inSummary && level is not null && turnsValid && !hasLines;
        var outOfDate = inSummary && ResumeSummaryState.IsOutOfDate(level, turns, _lines, kept);
        var status = zeroRefusal ?? (nothingToSummarise ? ResumeSummaryState.NothingToSummariseLine
            : outOfDate ? ResumeSummaryState.OutOfDateLine : null);

        var cost = !inSummary ? OpenCodeResumeRules.CostLine(mode)
            : level is null ? OpenCodeResumeRules.LastTurnsCostLine
            : OpenCodeResumeRules.SummaryCostLine + (previewReady ? " " + OpenCodeResumeRules.LastTurnsCostLine : string.Empty);
        CostText.Text = cost + (inSummary && !turnsValid ? " " + OpenCodeResumeRules.TurnsMessage : string.Empty)
            + (status is null ? string.Empty : " " + status);
        FolderNotice.Text = _folderNotice ?? string.Empty;
        FolderNotice.Visibility = _folderNotice is null ? Visibility.Collapsed : Visibility.Visible;

        LevelBlock.Visibility = inSummary ? Visibility.Visible : Visibility.Collapsed;
        TailBlock.Visibility = inSummary ? Visibility.Visible : Visibility.Collapsed;
        Enable(LevelRow, inSummary);
        Enable(TailBox, inSummary);
        Enable(TurnsBox, inSummary && TailOn);
        // Make summary is off while a request runs, so one summary call is made at a time in this screen.
        var makeAllowed = ResumeSummaryState.MakeSummaryAllowed(level, turnsValid, hasLines);
        Enable(MakeSummaryButton, inSummary && makeAllowed && _cts is null);
        Enable(CopyButton, previewReady);
        var startAllowed = choice is not null && (!inSummary
            || ResumeSummaryState.StartAllowed(level, TailOn, turns, turnsValid, previewReady, _lines, kept));
        Enable(StartButton, startAllowed && !_blocksStart);
    }

    private static void Enable(UIElement element, bool enabled) => ResumeScreenElements.Enable(element, enabled);

    /// <summary>
    /// Makes the start text of the choice, off the UI thread, and shows it. A request replaces the one before it: the older one is cancelled and its result is
    /// dropped. A failure is shown in the preview area; a cancel shows nothing. Never throws: every failure is shown on screen.
    /// </summary>
    private async Task RunRequestAsync(OpenCodeResumeChoice choice)
    {
        if (_previewAsync is null) return;
        CancelRequest();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var id = _request;
        var makesSummary = choice.Mode == OpenCodeResumeMode.Summary && choice.Level is not null;
        ShowMessage(makesSummary ? OpenCodeResumeRules.SummaryRunningLine : "Reading the session. Nothing is sent to a session.", problem: false);
        Refresh();
        try
        {
            var preview = await _previewAsync(choice, cts.Token);
            if (id != _request) return;
            if (preview.Lines is { } read) _lines = read;
            if (preview.Problem is { } problem && preview.Text.Length == 0) ShowMessage(problem, problem: true);
            else
            {
                if (preview.KeptSummary is not null) _keptPreview = preview;
                ShowPreview(preview);
            }
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
        _preview.Show(preview.Text, OpenCodeResumeRules.SizeLine(preview.Text.Length));
    }

    /// <summary>Shows one line in the preview area and no text. A problem is shown in the danger colour. There is then no text for Start.</summary>
    private void ShowMessage(string message, bool problem) => _preview.ShowMessage(message, problem);

    /// <summary>Takes the preview away. The text is cleared too, so no stale text stays in the dialog.</summary>
    private void HidePreview() => _preview.Hide();

    /// <summary>The Copy button: puts the shown text on the clipboard. Nothing is sent to a session.</summary>
    private void Copy_Click(object sender, RoutedEventArgs e) => _preview.Copy();

    /// <summary>
    /// Make summary: makes the summary now, once, for the level and the tail on screen (RS-D21). It is the only way a summary is made. The shown text is the
    /// summary and then the tail; nothing is saved by the dialog.
    /// </summary>
    private void MakeSummary_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out var turnsValid);
        if (choice is not { Mode: OpenCodeResumeMode.Summary, Level: not null } || !turnsValid) return;
        if (_summaryHasLines?.Invoke(choice.Turns) == false) return;
        _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// Start: the choice and the text shown, and the dialog closes. Start is off unless the choice is ready (<see cref="ResumeSummaryState.StartAllowed"/>),
    /// so a text is never sent that was not shown, and Start makes no summary.
    /// </summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_blocksStart) return;
        var choice = Selected(out var turnsValid);
        if (choice is null) return;
        var allowed = choice.Mode == OpenCodeResumeMode.Full
            || ResumeSummaryState.StartAllowed(choice.Level, TailOn, choice.Turns, turnsValid, _preview.Text is not null, _lines, _keptPreview?.KeptSummary);
        if (!allowed) return;
        Choice = choice with { Remember = RememberBox.IsChecked == true };
        PreviewText = choice.Mode == OpenCodeResumeMode.Full ? null : _preview.Text;
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

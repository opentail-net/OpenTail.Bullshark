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

    /// <summary>A new session that starts from a summary (its level, or Zero for none), followed by the tail when it is on (RS-D19).</summary>
    Summary,

    /// <summary>A new session that starts from the last turns only. No model call. The screen no longer offers it; a stored last-turns mode reads as Summary at Zero.</summary>
    TailOnly,
}

/// <summary>
/// The choice made in the resume prompt. Level is set for a summary, and null for Zero (no summary, so no summary call). Turns is the number of turns kept
/// verbatim, zero when the tail is off (RS-D19, RS-D21). Model and Effort are the model and effort of the resumed or new session (the summary itself is
/// always Haiku at low effort). Remember is the "Remember for this session" tick: the prompt only returns it, and the window writes the mark (Continue, stage
/// C6). Target is the platform the new session starts in (RS-D14). In a summary it is the chooser's pick; otherwise it is Claude, the screen's own platform.
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
public sealed record ResumePromptPreview(ResumePreview? Preview, string? Problem = null, bool SummaryFailed = false);

/// <summary>
/// The resume prompt (D14, D-S11, RS-D3 to RS-D28). Two ways to continue: Full (the same session, with its whole conversation) and Summary. A summary has
/// the level row (Zero, Minimal, Normal, Complete) and the tail, "Include last [N] turns". Zero is built at once, with no summary call; the other levels are
/// made only by Make summary, and a summary made for another tail or level is out of date (RS-D21). The owner picks the model and effort of the session
/// that continues. Cancel is the default.
/// </summary>
public partial class ResumePrompt : Window
{
    private const int MaxTurns = 200;

    /// <summary>The level row's tag of Zero. Zero is no summary, so it has no level (RS-D22).</summary>
    private const string ZeroTag = "Zero";

    private readonly int _contextTokens;
    private readonly Func<ResumeChoice, KeptSummary?, bool, CancellationToken, Task<ResumePromptPreview>>? _previewAsync;

    /// <summary>Claude's usage limit is in force, as the last scan read it (RS-O5). Null: unknown, so no limit is assumed.</summary>
    private readonly Func<bool>? _sourceAtLimit;

    /// <summary>True after a Make summary of Claude failed with another platform chosen: the dialog offers the target's summary (RS-D28).</summary>
    private bool _failedOnSource;
    private readonly Func<int, bool>? _summaryHasLines;

    /// <summary>The transcript's line count the dialog holds: the lines read when it opened (Claude's summary reads only those, RS-D21, FIX 1 of the S6c review).</summary>
    private readonly int? _lines;
    private CancellationTokenSource? _previewCts;

    /// <summary>The platform the Start menu picks (RS-D3): Claude by default, and back to Claude when the mode leaves a summary.</summary>
    private ProviderId _target = ProviderId.Claude;
    private bool _previewing;

    /// <summary>The start text on screen, or null when none is shown. Copy copies it, and Start is allowed only for the text shown (RS-D21).</summary>
    private string? _previewText;

    /// <summary>True while the preview on screen is a refusal of the launch: Resume and Copy stay off, as the Codex Start does (R4d).</summary>
    private bool _refused;

    /// <summary>
    /// The folder notice shown in red (<see cref="ResumeFolderNotice"/>), or null when there is none. Start (Resume) is off only with no folder on record; the chooser
    /// and the preview stay on.
    /// </summary>
    private readonly string? _folderNotice;

    /// <summary>True while the folder notice blocks Start: only a session with no folder on record. A missing folder does not block it: Start makes it again.</summary>
    private readonly bool _blocksStart;

    /// <param name="folder">The session's folder, for the folder notice. Used only with <paramref name="folderExists"/>.</param>
    /// <param name="folderExists">Whether the folder exists (the screen's folder check, Missing is false). Null: no folder notice is shown.</param>
    /// <param name="summaryHasLines">True when the summary of the session, with the given tail turns, has messages to read (RS-D26). Null: not known, so Make summary is on.</param>
    public ResumePrompt(string heading, string facts, long contextTokens, RailPalette palette, string defaultModel, string defaultEffort,
        Func<ResumeChoice, KeptSummary?, bool, CancellationToken, Task<ResumePromptPreview>>? previewAsync = null, ContinueMode? initialMode = null,
        string? folder = null, bool? folderExists = null, Func<int, bool>? summaryHasLines = null, int? lines = null, Func<bool>? sourceAtLimit = null)
    {
        InitializeComponent();
        // The window grows to the screen's work area at most: the body scrolls and the buttons stay in view (owner, 2026-11-22).
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        // The title names the source and the button names the target (RS-D4, RS-O2, RS-O4). The words come from ResumeTarget, the one place.
        Title = ResumeTarget.TitleOf(ProviderId.Claude);
        var startLabel = ResumeTarget.ButtonOf(ProviderId.Claude);
        ResumeButton.Content = startLabel;
        AutomationProperties.SetName(ResumeButton, startLabel);
        _previewAsync = previewAsync;
        _summaryHasLines = summaryHasLines;
        _lines = lines;
        _sourceAtLimit = sourceAtLimit;
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

        Heading.Text = heading;
        Facts.Text = facts;
        _contextTokens = (int)Math.Min(contextTokens, int.MaxValue);
        TurnsBox.Text = ProviderResumeRules.StartTurns(null).ToString(CultureInfo.InvariantCulture);
        TailBox.IsChecked = true;

        var fullRecommended = ResumeAdvice.FullIsRecommended(contextTokens);
        Recommendation.Text = fullRecommended
            ? $"Recommended: in full. The context is about {_contextTokens:N0} tokens, small enough to re-read before the first reply."
            : $"Recommended: from a summary. The context is about {_contextTokens:N0} tokens; a full resume would re-read all of it " +
              "before the first reply.";
        FullChoice.Content = ResumeSummaryState.FullLabel + (fullRecommended ? " 👍 (recommended)" : string.Empty);
        SummaryChoice.Content = ResumeSummaryState.SummaryLabel + (fullRecommended ? string.Empty : " 👍 (recommended)");
        // The start: the mode given (CN-D9, stage C4), or with none the recommendation, as before. A stored last-turns mode opens at Zero with the tail
        // (RS-D27); a summary with no stored level opens at Normal, and a recommendation of a summary opens at Normal too.
        var mode = ProviderResumeRules.StartMode(initialMode) ?? (fullRecommended ? ProviderResumeMode.Full : ProviderResumeMode.Summary);
        if (mode == ProviderResumeMode.Full) FullChoice.IsChecked = true;
        else SummaryChoice.IsChecked = true;
        SummaryLevel? openLevel = ProviderResumeRules.OpensAtZeroWithTail(initialMode) ? null : SummaryLevel.Normal;

        foreach (SummaryLevel? level in new SummaryLevel?[] { null, SummaryLevel.Minimal, SummaryLevel.Normal, SummaryLevel.Complete })
        {
            var text = level is { } chosen ? ProviderResumeRules.LevelLabel(chosen) : ResumeSummaryState.ZeroLabel;
            var segment = Segment(text, "Level", level == openLevel);
            segment.Tag = level?.ToString() ?? ZeroTag;
            segment.Checked += (_, _) => UpdateCost();
            LevelRow.Children.Add(segment);
        }

        foreach (var radio in new[] { FullChoice, SummaryChoice }) radio.Checked += (_, _) => UpdateCost();
        TailBox.Checked += (_, _) => UpdateCost();
        TailBox.Unchecked += (_, _) => UpdateCost();
        TurnsBox.TextChanged += (_, _) => UpdateCost();

        foreach (var model in Choices.Models) ModelRow.Children.Add(Segment(model, "Model", model == defaultModel));
        foreach (var effort in Choices.Efforts) EffortRow.Children.Add(Segment(effort, "Effort", effort == defaultEffort));

        UpdateCost();
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public ResumeChoice? Choice { get; private set; }

    /// <summary>
    /// The summary the preview made (or reused). Resume passes it on, so the summary is not made twice. Null when none was made. It is kept for the tail
    /// and the level it was made at; Start is off for any other setting (RS-D21).
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

    /// <summary>The summary level on screen: null for Zero, and Normal when none is chosen.</summary>
    private SummaryLevel? SelectedLevel()
    {
        var tag = SelectedTag(LevelRow);
        if (tag == ZeroTag) return null;
        return Enum.TryParse<SummaryLevel>(tag, out var level) ? level : SummaryLevel.Normal;
    }

    /// <summary>True when the tail is on: the box is ticked (RS-D19).</summary>
    private bool TailOn => TailBox.IsChecked == true;

    /// <summary>
    /// The choice on screen, or null when none is selected. turnsValid is false when the tail is on and the turns are not a whole number in range. Full and a
    /// tail that is off need no turns, so they are valid.
    /// </summary>
    private ResumeChoice? Selected(out bool turnsValid)
    {
        var parsed = int.TryParse(TurnsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var turns);
        var valid = parsed && turns is >= 0 and <= MaxTurns;
        var kept = valid ? turns : ResumeText.DefaultTurns;
        var model = SelectedTag(ModelRow) ?? Choices.Models[0];
        var effort = SelectedTag(EffortRow) ?? Choices.Efforts[0];

        if (FullChoice.IsChecked == true)
        {
            turnsValid = true;
            return Targeted(new ResumeChoice(ResumeMode.Full, null, 0, model, effort));
        }
        turnsValid = !TailOn || valid;
        if (SummaryChoice.IsChecked == true)
            return Targeted(new ResumeChoice(ResumeMode.Summary, SelectedLevel(), ResumeSummaryState.TurnsOf(TailOn, kept), model, effort));
        return null;
    }

    /// <summary>The choice with its target (RS-D3, RS-D14): the preview is built for the target on screen, and Start sends the same one.</summary>
    private ResumeChoice Targeted(ResumeChoice choice) => choice with { Target = StartTarget(choice.Mode) };

    /// <summary>
    /// The platform the new session starts in, for the mode (<see cref="ResumeTarget.ChosenTarget"/>): the Start menu's pick in a summary, else Claude.
    /// </summary>
    private ProviderId StartTarget(ResumeMode mode) =>
        ResumeTarget.ChosenTarget(ProviderId.Claude, RememberChoice.ModeOf(mode), _target);

    /// <summary>True when the start on screen goes to another platform than Claude: the model and effort rows do not apply to it (RS-D12).</summary>
    private static bool IsCross(ResumeChoice? choice) => choice is not null && !CrossModelRow.ShowsModel(ProviderId.Claude, choice.Target);

    /// <summary>
    /// Shows the target on screen (RS-D3, RS-D4, RS-O2): the line "Summary of ... session, new ... session" when a summary starts in another platform, and
    /// the Resume button, which names the target.
    /// </summary>
    private void ShowTarget(ProviderId target)
    {
        var summary = SummaryChoice.IsChecked == true;
        var line = ResumeTarget.SummaryLine(ProviderId.Claude, target);
        TargetLine.Text = line ?? string.Empty;
        TargetLine.Visibility = summary && line is not null ? Visibility.Visible : Visibility.Collapsed;
        var startLabel = ResumeTarget.ButtonOf(target);
        ResumeButton.Content = startLabel;
        AutomationProperties.SetName(ResumeButton, startLabel);
    }

    /// <summary>
    /// Runs when the owner picks a platform in the Resume menu (RS-D3, RS-D18). No model is called. A pick that changes the target takes the preview on
    /// screen away: nothing is rebuilt and no model is called, and Make summary builds the text again for the target on screen.
    /// </summary>
    private void TargetItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResumeTargetMenu.PlatformOf(sender) is not { } platform) return;
        if (platform != _target)
        {
            _failedOnSource = false;
            HidePreview();
        }
        _target = platform;
        Refresh();
    }

    /// <summary>
    /// Sets the platform items of the Resume menu (RS-D2, RS-D3, RS-D8): the pick is checked, and each item is on only in a summary, where the rule allows
    /// it. <paramref name="menuOn"/> is false outside a summary, and while a summary is made.
    /// </summary>
    private void ShowTargetMenu(bool menuOn)
    {
        ResumeTargetMenu.Apply(TargetClaudeItem, ProviderId.Claude, ProviderId.Claude, _target, menuOn);
        ResumeTargetMenu.Apply(TargetCodexItem, ProviderId.Codex, ProviderId.Claude, _target, menuOn);
        ResumeTargetMenu.Apply(TargetCopilotItem, ProviderId.Copilot, ProviderId.Claude, _target, menuOn);
    }

    /// <summary>
    /// Runs when the mode, the level, the tail or its turns change. A preview on screen is then stale, so it is taken away. A summary kept for another
    /// setting is out of date (RS-D21) and Start waits for Make summary. Zero, and a summary kept for the setting on screen, are shown at once.
    /// </summary>
    private void UpdateCost()
    {
        // RS-D3: the target goes back to the source when the mode leaves a summary; the Resume menu's platform items are off then.
        if (SummaryChoice.IsChecked != true) _target = ProviderId.Claude;
        _previewCts?.Cancel();
        _failedOnSource = false;
        HidePreview();
        Refresh();
        var choice = Selected(out var turnsValid);
        if (choice is null || !turnsValid || choice.Mode != ResumeMode.Summary) return;
        if (ResumeSummaryState.ZeroRefusal(choice.Level, TailOn, choice.Turns) is not null) return;
        if (ResumeSummaryState.RunsAtOnce(choice.Level, choice.Turns, _lines, KeptSummary)) _ = BuildAsync(choice);
    }

    /// <summary>
    /// Says what the selected choice costs and sends, before the owner presses Resume. It also shows only the options that apply to the choice: the level row
    /// and the tail for a summary, and the model and effort rows unless the start goes to another platform. It sets what can be pressed: while a preview is
    /// being made, the choices and the buttons are off, and Start waits for a summary of the setting on screen (<see cref="ResumeSummaryState.StartAllowed"/>).
    /// </summary>
    private void Refresh()
    {
        var choice = Selected(out var turnsValid);
        var summary = choice?.Mode == ResumeMode.Summary;
        var level = choice?.Level;
        var turns = choice is null ? 0 : choice.Turns;
        var previewReady = _previewText is not null;
        FolderNotice.Text = _folderNotice ?? string.Empty;
        FolderNotice.Visibility = _folderNotice is null ? Visibility.Collapsed : Visibility.Visible;
        LevelBlock.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
        TailBlock.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
        ShowTarget(choice?.Target ?? ProviderId.Claude);
        // RS-D12: a cross start runs at the target's own default model and effort, so the model and effort rows do not apply and are hidden.
        var cross = IsCross(choice);
        ModelHeader.Visibility = ModelLine.Visibility = EffortLine.Visibility = cross ? Visibility.Collapsed : Visibility.Visible;

        // RS-D21, RS-D25, RS-D26: the status line after the cost line. The turns message comes after the cost, as before.
        var zeroRefusal = summary ? ResumeSummaryState.ZeroRefusal(level, TailOn, turns) : null;
        var hasLines = _summaryHasLines?.Invoke(turns) ?? true;
        var nothingToSummarise = summary && level is not null && turnsValid && !hasLines;
        var outOfDate = summary && ResumeSummaryState.IsOutOfDate(level, turns, _lines, KeptSummary);
        var status = zeroRefusal ?? (nothingToSummarise ? ResumeSummaryState.NothingToSummariseLine
            : outOfDate ? ResumeSummaryState.OutOfDateLine : null);

        var sizes = turnsValid ? string.Empty : $" Enter a whole number from 0 to {MaxTurns} for the turns.";
        var runs = choice is null ? string.Empty
            : cross ? " " + CrossModelRow.CostLineForCross(choice.Target)
            : $" The new session runs at {choice.Model} {choice.Effort}.";
        // RS-D28: the target's CLI writes the summary when Claude is at its limit, and the cost line says so before the call. After a failed Claude summary,
        // with another platform chosen, the button offers the target's summary, and the line says it is one call there.
        var writesByTarget = choice is not null && level is not null && WritesByTarget(choice);
        var writeShown = choice is { Mode: ResumeMode.Summary } && level is not null && _failedOnSource && choice.Target != ProviderId.Claude;
        var costLine = choice?.Mode switch
        {
            ResumeMode.Full =>
                $"Resuming in full re-reads all of the context, about {_contextTokens:N0} tokens, before the first reply. " +
                "A warm cache makes that cheap; a cold one charges it at full price. Nothing runs until you press Resume.",
            ResumeMode.Summary when level is null =>
                "No summary call. The last turns are copied word for word into a new session.",
            ResumeMode.Summary when writesByTarget => SummaryWriterRules.AtLimitCostLine(choice!.Target),
            ResumeMode.Summary =>
                $"One Haiku call (low effort) reads your messages and Claude's replies, not the tool output, up to about " +
                $"{ResumeText.SummaryInputChars / 4:N0} tokens, and writes the {level.Value.ToString().ToLowerInvariant()} " +
                "summary. Then a new session starts from it and from the last turns.",
            _ => "Choose one of the ways to continue.",
        };
        CostText.Text = (_previewing
            ? (NeedsSummaryCall(choice) ? (writesByTarget ? SummaryWriterRules.OneCallLine(choice!.Target) : "Summarising: one model call…") : "Building the prompt…")
            : costLine + (writeShown ? " " + SummaryWriterRules.OneCallLine(choice!.Target) : string.Empty)) + runs + sizes + (status is null ? string.Empty : " " + status);

        var free = !_previewing;
        Enable(FullChoice, free);
        Enable(SummaryChoice, free);
        Enable(TailBox, free && summary);
        Enable(TurnsBox, free && summary && TailOn);
        ShowTargetMenu(free && summary);
        foreach (var segment in LevelRow.Children.OfType<RadioButton>()) Enable(segment, free && summary);
        var makeAllowed = ResumeSummaryState.MakeSummaryAllowed(level, turnsValid, hasLines);
        Enable(MakeSummaryButton, free && summary && makeAllowed);
        WriteByTargetButton.Content = SummaryWriterRules.ButtonOf(choice?.Target ?? ProviderId.Claude);
        WriteByTargetButton.Visibility = writeShown ? Visibility.Visible : Visibility.Collapsed;
        Enable(WriteByTargetButton, free && writeShown && makeAllowed);
        var startAllowed = choice is not null && (!summary
            || ResumeSummaryState.StartAllowed(level, TailOn, turns, turnsValid, previewReady, _lines, KeptSummary));
        ResumeButton.IsEnabled = free && !_refused && !_blocksStart && startAllowed;
        ResumeMenuButton.IsEnabled = free;
        Enable(RememberBox, free);
        CopyItem.IsEnabled = free && !_refused && previewReady;
        CopyItem.ToolTip = previewReady
            ? "Copies the start text shown above to the clipboard. Nothing is sent to a session."
            : "Makes a start text first: Zero, or Make summary.";
    }

    /// <summary>True when the choice needs a model call that has not been made yet: a summary (a level), and no kept summary for its setting.</summary>
    private bool NeedsSummaryCall(ResumeChoice? choice) =>
        choice?.Mode == ResumeMode.Summary && choice.Level is not null && !ResumeSummaryState.KeptMatches(choice.Level, choice.Turns, _lines, KeptSummary);

    private static void Enable(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : 0.5;
    }

    /// <summary>True when the target writes this summary because Claude is at its limit (RS-D28, RS-O5). The limit state is read when it is asked.</summary>
    private bool WritesByTarget(ResumeChoice choice) =>
        SummaryWriterRules.ClaudeAtLimitWritesByTarget(ProviderId.Claude, choice.Target, _sourceAtLimit?.Invoke() == true);

    /// <summary>The "Make summary with &lt;Target&gt;" button (RS-D28): the target writes the summary after the source's failed. Its checks run first.</summary>
    private async void WriteByTarget_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out var turnsValid);
        if (choice is not { Mode: ResumeMode.Summary, Level: not null } || !turnsValid || _previewing || !_failedOnSource) return;
        await BuildAsync(choice, writeByTarget: true);
    }

    /// <summary>The Make summary button: makes the summary now, once, for the level and tail on screen (RS-D21).</summary>
    private async void MakeSummary_Click(object sender, RoutedEventArgs e)
    {
        var choice = Selected(out var turnsValid);
        if (choice is not { Mode: ResumeMode.Summary, Level: not null } || !turnsValid || _previewing) return;
        await BuildAsync(choice);
    }

    /// <summary>The narrow part of the split button: opens the menu under it.</summary>
    private void ResumeMenuButton_Click(object sender, RoutedEventArgs e)
    {
        ResumeMenu.PlacementTarget = ResumeMenuButton;
        ResumeMenu.IsOpen = true;
    }

    /// <summary>
    /// Builds the start text for the choice, through the window's method that the start uses, and shows it. A failure is shown in the preview area and the
    /// dialog stays open. A failed summary of the session's own platform falls back to Full (ResumeDefaults); one that starts in another platform keeps the
    /// summary, and the refusal stays, so Start is off. A build cancelled because the dialog closed shows nothing.
    /// </summary>
    private async Task BuildAsync(ResumeChoice choice, bool writeByTarget = false)
    {
        if (_previewAsync is null || _previewing) return;

        _previewing = true;
        _refused = false;
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        Refresh();
        try
        {
            var built = await _previewAsync(choice, KeptSummary, writeByTarget || WritesByTarget(choice), cts.Token);
            if (built.Preview is not { } preview)
            {
                var problem = built.Problem ?? "The prompt could not be built.";
                _failedOnSource = built.SummaryFailed && !writeByTarget && choice.Target != ProviderId.Claude;
                if (choice.Target == ProviderId.Claude && ResumeDefaults.FallBackToFull(choice.Mode == ResumeMode.Summary && choice.Level is not null, cancelled: false))
                    FallBackToFull(problem);
                else
                {
                    _refused = true;
                    ShowRefusal(problem);
                }
                return;
            }
            _failedOnSource = false;
            if (preview.Summary is { } summary) KeptSummary = summary;
            ShowPreview(preview);
        }
        catch (OperationCanceledException)
        {
            // The dialog closed, or the choice changed, while the summary was being made: nothing is shown.
        }
        catch (Exception ex)
        {
            if (choice.Target == ProviderId.Claude && ResumeDefaults.FallBackToFull(choice.Mode == ResumeMode.Summary && choice.Level is not null, cancelled: false))
                FallBackToFull($"The prompt could not be built: {ex.Message}");
            else ShowError(ex.Message);
        }
        finally
        {
            _previewing = false;
            _previewCts = null;
            cts.Dispose();
            Refresh();
        }
    }

    /// <summary>
    /// A summary that was not made (ResumeDefaults): In full is selected, which takes the preview away, and the problem is shown with the line that says
    /// so, as a refusal is shown. Nothing is started, and Resume is on, as In full has no prompt to refuse.
    /// </summary>
    private void FallBackToFull(string problem)
    {
        FullChoice.IsChecked = true;
        ShowRefusal($"{problem} {ResumeDefaults.FullFallbackLine}");
    }

    /// <summary>Shows a built prompt: its size line (in the danger colour when it does not fit), and the text, read-only.</summary>
    private void ShowPreview(ResumePreview preview)
    {
        _previewText = preview.Text;
        // RS-D28: a summary another platform wrote says so, after the size line.
        var written = preview.Summary?.WrittenBy is { } writer && writer != ProviderId.Claude ? " " + SummaryWriterRules.WrittenByLine(writer) : string.Empty;
        PreviewHeader.Text = ResumePreviewText.Header(preview.Text.Length, preview.CutCharacters, preview.Fits) + written;
        PreviewHeader.SetResourceReference(TextBlock.ForegroundProperty, preview.Fits ? "PromptMuted" : "UiDanger");
        PreviewText.Text = preview.Text;
        PreviewText.Visibility = Visibility.Visible;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
        PreviewText.CaretIndex = 0;
        PreviewText.ScrollToHome();
    }

    /// <summary>
    /// Shows why the launch is refused: the refusal in the danger colour, and no prompt text. Resume and Copy stay off while it shows; the dialog stays open, so
    /// the owner can change the choice, update the tool or cancel.
    /// </summary>
    private void ShowRefusal(string problem)
    {
        _previewText = null;
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
        _previewText = null;
        PreviewHeader.Text = $"The prompt could not be built: {message}";
        PreviewHeader.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        PreviewText.Text = string.Empty;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBlock.Visibility = Visibility.Visible;
    }

    /// <summary>The Copy item: puts the start text on screen on the clipboard, and says so under the text. Nothing is sent to a session.</summary>
    private void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        if (_previewText is { } text) CopyText(text);
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
        _previewText = null;
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
        if (_blocksStart) return;
        var choice = Selected(out var turnsValid);
        if (choice is null) return;
        if (choice.Mode == ResumeMode.Summary
            && !ResumeSummaryState.StartAllowed(choice.Level, TailOn, choice.Turns, turnsValid, _previewText is not null, _lines, KeptSummary))
        {
            UpdateCost();
            return;
        }

        Choice = choice with { Remember = RememberBox.IsChecked == true, Target = StartTarget(choice.Mode) };
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = null;
        Close();
    }
}

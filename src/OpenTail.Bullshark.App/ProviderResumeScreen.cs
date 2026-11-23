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

/// <summary>
/// A resume choice as the shared screen logic sees it (dedup plan 2026-11-23, D-S3). Each provider's choice record implements it: the shared mode
/// (the rules take the shared one), the level (null is Zero), the turns (zero when the tail is off), the Remember tick and the target.
/// </summary>
public interface IProviderResumeChoice<TSelf> where TSelf : class
{
    /// <summary>The mode as the shared rules see it.</summary>
    ProviderResumeMode SharedMode { get; }

    /// <summary>The summary level, or null for Zero (no summary, so no summary call).</summary>
    SummaryLevel? Level { get; }

    /// <summary>The tail's turns, zero when the tail is off (RS-D21).</summary>
    int Turns { get; }

    bool Remember { get; }

    ProviderId Target { get; }

    /// <summary>The same choice with another target.</summary>
    TSelf WithTarget(ProviderId target);

    /// <summary>The same choice with the Remember tick set as given.</summary>
    TSelf WithRemember(bool remember);
}

/// <summary>What a preview callback gives (dedup plan D-S3). Each provider's preview record implements it.</summary>
public interface IProviderPreview
{
    string Text { get; }

    KeptSummary? KeptSummary { get; }

    string? Problem { get; }

    /// <summary>The transcript's line count the build read, or null when it read none (FIX 1 of the S6c review).</summary>
    int? Lines { get; }

    /// <summary>True when a kept summary was for the setting but not for the transcript the build read: no summary was made, and the screen says it is out of date.</summary>
    bool Stale { get; }
}

/// <summary>
/// What differs between the providers' resume screens (dedup plan D-S4). Everything else on the screen is the same for each provider, and lives in
/// <see cref="ProviderResumeScreen{TChoice, TPreview}"/>.
/// </summary>
public interface IProviderResumeScreenHooks<TChoice> where TChoice : class
{
    /// <summary>The provider whose screen this is: its platform, its title words and its level group.</summary>
    ProviderId Provider { get; }

    /// <summary>The provider's words: its name, its resume command and its summary cost line.</summary>
    ProviderResumeWording Wording { get; }

    /// <summary>The choice for a mode, a level (null for Zero) and a number of turns. A provider adds its own fields to it (Codex: the model).</summary>
    TChoice MakeChoice(ProviderResumeMode mode, SummaryLevel? level, int turns);

    /// <summary>True when Start may go on for the choice. Codex waits for its model list (unless the choice needs none); Copilot never waits.</summary>
    bool StartReady(TChoice? choice);

    /// <summary>Refreshes the provider's own controls for the choice (Codex: the model row). Nothing for Copilot.</summary>
    void RefreshOwnControls(TChoice? choice, ProviderResumeMode mode, bool free);
}

/// <summary>
/// The controls the shared screen logic uses. Each window builds one from its own <c>x:Name</c> controls, so the names in the XAML stay as they are
/// (the tests find them by name). <see cref="LevelBlock"/> and <see cref="TailBlock"/> are shown in Summary only (RS-D3, RS-D19).
/// </summary>
public sealed record ResumeScreenControls(
    RadioButton FullChoice,
    RadioButton SummaryChoice,
    Panel LevelBlock,
    Panel LevelRow,
    CheckBox TailBox,
    TextBox TurnsBox,
    Panel TailBlock,
    TextBlock TargetLine,
    Button StartButton,
    Button TargetMenuButton,
    ContextMenu TargetMenu,
    MenuItem TargetClaudeItem,
    MenuItem TargetCodexItem,
    MenuItem TargetCopilotItem,
    Button MakeSummaryButton,
    CheckBox RememberBox,
    TextBlock CostText,
    TextBlock FolderNotice,
    TextBlock PreviewHeader,
    TextBox PreviewBox,
    UIElement PreviewBlock,
    TextBlock PreviewNote,
    Button CopyButton,
    Style SegmentStyle);

/// <summary>
/// The resume screen's logic, shared by the providers' screens (dedup plan 2026-11-23, D-S4 to D-S6). It holds what the Codex and Copilot screens
/// did one each: the choice on screen (Full, or Summary with its level, the tail and its turns), what is on and off, the preview and its request, the
/// summary kept for its setting, the Start menu, Copy and Start. A window keeps its own XAML handlers and forwards them here.
/// </summary>
public sealed class ProviderResumeScreen<TChoice, TPreview>
    where TChoice : class, IProviderResumeChoice<TChoice>
    where TPreview : class, IProviderPreview
{
    /// <summary>The level row's tag of Zero. Zero is no summary, so it has no level (RS-D22).</summary>
    private const string ZeroTag = "Zero";

    private readonly ResumeScreenControls _c;
    private readonly IProviderResumeScreenHooks<TChoice> _hooks;
    private readonly Func<TChoice, KeptSummary?, CancellationToken, Task<TPreview>>? _previewAsync;
    private readonly Func<int, bool>? _summaryHasLines;
    private readonly string? _folderNotice;
    private readonly bool _blocksStart;
    private readonly Action _close;
    private CancellationTokenSource? _cts;
    private int _request;
    private bool _busy;
    private readonly ResumePreviewPane _preview;
    private KeptSummary? _kept;

    /// <summary>The transcript's line count the screen holds: the count at open, then the count each build read (RS-D21, FIX 1 of the S6c review).</summary>
    private int? _lines;

    /// <summary>The platform the Start menu picks (RS-D3): the provider's own by default, and back to it when the mode leaves a summary.</summary>
    private ProviderId _target;

    /// <param name="controls">The window's controls, as the screen uses them.</param>
    /// <param name="hooks">What differs for the provider.</param>
    /// <param name="previewAsync">Makes the start text for a choice (off the UI thread), given the kept summary; returns the text and the summary it used.</param>
    /// <param name="summaryHasLines">True when the summary of the session, with the given tail turns, has messages to read (RS-D26). False disables Make summary
    /// with its status line. Null: not known here, so Make summary is on.</param>
    /// <param name="initialMode">The mode the screen opens on. Null keeps the screen's own start: Summary at Zero with the tail (no summary call). A stored
    /// last-turns mode opens at Zero with the tail (RS-D27). Opening on a mode never makes a summary.</param>
    /// <param name="folderNotice">The folder notice shown in red, or null.</param>
    /// <param name="blocksStart">True when the folder notice blocks Start (a session with no folder on record).</param>
    /// <param name="close">Closes the window (Start and Cancel).</param>
    /// <param name="lines">The transcript's line count the screen holds when it opens, or null when it is not known. Each build updates it (FIX 1 of the S6c review).</param>
    public ProviderResumeScreen(
        ResumeScreenControls controls,
        IProviderResumeScreenHooks<TChoice> hooks,
        Func<TChoice, KeptSummary?, CancellationToken, Task<TPreview>>? previewAsync,
        Func<int, bool>? summaryHasLines,
        ContinueMode? initialMode,
        string? folderNotice,
        bool blocksStart,
        Action close,
        int? lines = null)
    {
        _c = controls;
        _hooks = hooks;
        _previewAsync = previewAsync;
        _summaryHasLines = summaryHasLines;
        _folderNotice = folderNotice;
        _blocksStart = blocksStart;
        _close = close;
        _lines = lines;
        _target = hooks.Provider;
        _preview = new ResumePreviewPane(controls.PreviewHeader, controls.PreviewBox, controls.PreviewBlock, controls.PreviewNote);

        _c.FullChoice.Content = ResumeSummaryState.FullLabel;
        _c.SummaryChoice.Content = ResumeSummaryState.SummaryLabel;
        _c.TurnsBox.Text = ProviderResumeRules.StartTurns(null).ToString(CultureInfo.InvariantCulture);
        _c.TailBox.IsChecked = true;

        // The start mode is set before the handlers below are attached, so the screen's first build runs once, for the mode it opens on.
        // Full opens Full. Summary opens at its own level (Normal); a stored last-turns mode, and no mode at all, open at Zero with the tail (RS-D27).
        var start = ProviderResumeRules.StartMode(initialMode);
        var atZero = start is null || ProviderResumeRules.OpensAtZeroWithTail(initialMode);
        if (start == ProviderResumeMode.Full) _c.FullChoice.IsChecked = true;
        else _c.SummaryChoice.IsChecked = true;
        SummaryLevel? openLevel = atZero ? null : SummaryLevel.Normal;

        foreach (SummaryLevel? level in new SummaryLevel?[] { null, SummaryLevel.Minimal, SummaryLevel.Normal, SummaryLevel.Complete })
        {
            var text = level is { } chosen ? ProviderResumeRules.LevelLabel(chosen) : ResumeSummaryState.ZeroLabel;
            var segment = Segment(text, $"{hooks.Provider}Level", level == openLevel);
            segment.Tag = level?.ToString() ?? ZeroTag;
            segment.Checked += (_, _) => OnChoiceChanged();
            _c.LevelRow.Children.Add(segment);
        }
        foreach (var radio in new[] { _c.FullChoice, _c.SummaryChoice }) radio.Checked += (_, _) => OnChoiceChanged();
        _c.TailBox.Checked += (_, _) => OnChoiceChanged();
        _c.TailBox.Unchecked += (_, _) => OnChoiceChanged();
        _c.TurnsBox.TextChanged += (_, _) => OnChoiceChanged();

        OnChoiceChanged();
    }

    /// <summary>The choice, or null when the owner cancelled (or none was made).</summary>
    public TChoice? Choice { get; private set; }

    /// <summary>
    /// The start text the window sends: exactly the text that was shown, so nothing is built again for the start. Null for Full, which sends no
    /// text.
    /// </summary>
    public string? PreviewText { get; private set; }

    /// <summary>The summary the screen holds for the choice that was started, or null when none was made for it. The window does not save it again.</summary>
    public KeptSummary? KeptSummary { get; private set; }

    /// <summary>One option of the level row: a segment that shows as pressed when it is the chosen one.</summary>
    private RadioButton Segment(string text, string group, bool isChecked) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = isChecked,
        Style = _c.SegmentStyle,
    };

    /// <summary>The summary level on screen: null for Zero, and Normal when no segment is chosen.</summary>
    private SummaryLevel? SelectedLevel()
    {
        var tag = _c.LevelRow.Children.OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true)?.Tag as string;
        if (tag == ZeroTag) return null;
        return Enum.TryParse<SummaryLevel>(tag, out var level) ? level : SummaryLevel.Normal;
    }

    /// <summary>True when the tail is on: the box is ticked (RS-D19).</summary>
    private bool TailOn => _c.TailBox.IsChecked == true;

    /// <summary>
    /// The choice on screen, or null when no mode is checked. <paramref name="turnsValid"/> is false when the tail is on and the turns box is not a valid
    /// number; Full and a tail that is off need no turns, so they are always valid.
    /// </summary>
    private TChoice? Selected(out bool turnsValid)
    {
        var valid = ProviderResumeRules.TryTurns(_c.TurnsBox.Text, out var turns);
        if (_c.FullChoice.IsChecked == true)
        {
            turnsValid = true;
            return Targeted(_hooks.MakeChoice(ProviderResumeMode.Full, null, 0));
        }
        if (_c.SummaryChoice.IsChecked != true)
        {
            turnsValid = false;
            return null;
        }
        turnsValid = !TailOn || valid;
        return Targeted(_hooks.MakeChoice(ProviderResumeMode.Summary, SelectedLevel(), ResumeSummaryState.TurnsOf(TailOn, turns)));
    }

    /// <summary>The choice with its target (RS-D3, RS-D14): the preview is built for the target on screen, and Start sends the same one.</summary>
    private TChoice Targeted(TChoice choice) => choice.WithTarget(StartTarget(choice.SharedMode));

    /// <summary>The platform the new session starts in, for the mode (<see cref="ResumeTarget.ChosenTarget"/>): the Start menu's pick in a summary, else the provider.</summary>
    private ProviderId StartTarget(ProviderResumeMode mode) =>
        ResumeTarget.ChosenTarget(_hooks.Provider, RememberChoice.ModeOf(mode), _target);

    /// <summary>
    /// Shows the target on screen (RS-D3, RS-D4, RS-O2): the line "Summary of ... session, new ... session" when a summary starts in another platform, and
    /// the Start button, which names the target.
    /// </summary>
    private void ShowTarget(ProviderId target)
    {
        var summary = _c.SummaryChoice.IsChecked == true;
        var line = ResumeTarget.SummaryLine(_hooks.Provider, target);
        _c.TargetLine.Text = line ?? string.Empty;
        _c.TargetLine.Visibility = summary && line is not null ? Visibility.Visible : Visibility.Collapsed;
        var startLabel = ResumeTarget.ButtonOf(target);
        _c.StartButton.Content = startLabel;
        AutomationProperties.SetName(_c.StartButton, startLabel);
    }

    /// <summary>The tooltip of the narrow part of the Start split button: in a summary, and outside one (RS-D2).</summary>
    private const string TargetMenuTip = "Start in another platform: only from a summary";

    /// <summary>The tooltip of the narrow part of the Start split button while it is off: the mode is not a summary (RS-D2).</summary>
    private const string TargetMenuOffTip = "Switching platform is only for a summary.";

    /// <summary>
    /// Runs when the owner picks a platform in the Start menu (RS-D3, RS-D18). No model call. A pick that changes the target cancels a request in progress,
    /// which is for the old target, and takes the preview on screen away. Make summary builds the text again for the target on screen, with the kept summary.
    /// </summary>
    public void TargetItemClick(object sender)
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
    public void OpenTargetMenu()
    {
        _c.TargetMenu.PlacementTarget = _c.TargetMenuButton;
        _c.TargetMenu.IsOpen = true;
    }

    /// <summary>
    /// Sets the Start menu (RS-D3, RS-D8): the pick is checked, and each platform item is on only where the rule allows it for a summary.
    /// <paramref name="menuOn"/> is false outside a summary, and while a summary is made.
    /// </summary>
    private void ShowTargetMenu(bool menuOn)
    {
        var source = _hooks.Provider;
        ResumeTargetMenu.Apply(_c.TargetClaudeItem, ProviderId.Claude, source, _target, menuOn);
        ResumeTargetMenu.Apply(_c.TargetCodexItem, ProviderId.Codex, source, _target, menuOn);
        ResumeTargetMenu.Apply(_c.TargetCopilotItem, ProviderId.Copilot, source, _target, menuOn);
    }

    /// <summary>
    /// Runs when the mode, the level, the tail or its turns change. The preview on screen is for the old choice, so it is taken away, and the summary
    /// kept for another setting is out of date (RS-D21): Start waits for Make summary. The preview is built again at once when it needs no model call:
    /// Zero with the tail on, or a summary kept for the setting on screen.
    /// </summary>
    private void OnChoiceChanged()
    {
        // RS-D3: the target goes back to the source when the mode leaves a summary; the Start menu is off then.
        if (_c.SummaryChoice.IsChecked != true) _target = _hooks.Provider;
        CancelRequest();
        _busy = false;
        HidePreview();
        Refresh();
        var choice = Selected(out var turnsValid);
        if (choice is null || !turnsValid || choice.SharedMode != ProviderResumeMode.Summary) return;
        if (ResumeSummaryState.ZeroRefusal(choice.Level, TailOn, choice.Turns) is not null) return;
        if (ResumeSummaryState.RunsAtOnce(choice.Level, choice.Turns, _lines, _kept)) _ = RunRequestAsync(choice);
    }

    /// <summary>
    /// Sets what is on and off for the choice: the cost line and its status line, the choices, the level row and the tail (Summary only), the turns box
    /// (Summary with the tail on), Make summary (<see cref="ResumeSummaryState.MakeSummaryAllowed"/>), Copy, and Start
    /// (<see cref="ResumeSummaryState.StartAllowed"/>). While a summary is made, the choices and the buttons are off.
    /// </summary>
    public void Refresh()
    {
        var choice = Selected(out var turnsValid);
        var mode = choice?.SharedMode ?? ProviderResumeMode.Full;
        var inSummary = mode == ProviderResumeMode.Summary;
        var free = !_busy;
        var level = choice?.Level;
        var turns = choice is null ? 0 : choice.Turns;
        var previewReady = _preview.Text is not null;
        var zeroRefusal = inSummary ? ResumeSummaryState.ZeroRefusal(level, TailOn, turns) : null;
        var hasLines = _summaryHasLines?.Invoke(turns) ?? true;
        var nothingToSummarise = inSummary && level is not null && turnsValid && !hasLines;
        var outOfDate = inSummary && ResumeSummaryState.IsOutOfDate(level, turns, _lines, _kept);

        // The status line (RS-D21, RS-D25, RS-D26) is shown after the cost line, as the other messages of the screen are.
        var status = zeroRefusal ?? (nothingToSummarise ? ResumeSummaryState.NothingToSummariseLine
            : outOfDate ? ResumeSummaryState.OutOfDateLine : null);
        var costLine = !inSummary ? ProviderResumeRules.CostLine(mode, _hooks.Wording)
            : level is null ? ProviderResumeRules.NoSummaryCallLine
            : _hooks.Wording.SummaryCostLine();
        _c.CostText.Text = costLine + (inSummary && !turnsValid ? " " + ProviderResumeRules.TurnsMessage : string.Empty)
            + (status is null ? string.Empty : " " + status);
        _c.FolderNotice.Text = _folderNotice ?? string.Empty;
        _c.FolderNotice.Visibility = _folderNotice is null ? Visibility.Collapsed : Visibility.Visible;

        Enable(_c.FullChoice, free);
        Enable(_c.SummaryChoice, free);
        _c.LevelBlock.Visibility = inSummary ? Visibility.Visible : Visibility.Collapsed;
        _c.TailBlock.Visibility = inSummary ? Visibility.Visible : Visibility.Collapsed;
        Enable(_c.LevelRow, free && inSummary);
        Enable(_c.TailBox, free && inSummary);
        Enable(_c.TurnsBox, free && inSummary && TailOn);
        ShowTarget(choice?.Target ?? _hooks.Provider);
        Enable(_c.TargetMenuButton, free && inSummary);
        _c.TargetMenuButton.ToolTip = inSummary ? TargetMenuTip : TargetMenuOffTip;
        ShowTargetMenu(free && inSummary);
        var makeAllowed = ResumeSummaryState.MakeSummaryAllowed(level, turnsValid, hasLines);
        Enable(_c.MakeSummaryButton, free && inSummary && makeAllowed);
        Enable(_c.RememberBox, free);
        Enable(_c.CopyButton, free && previewReady);
        _hooks.RefreshOwnControls(choice, mode, free);
        var startAllowed = choice is not null && (!inSummary
            || ResumeSummaryState.StartAllowed(level, TailOn, turns, turnsValid, previewReady, _lines, _kept));
        Enable(_c.StartButton, free && !_blocksStart && _hooks.StartReady(choice) && startAllowed);
    }

    private static void Enable(UIElement element, bool enabled) => ResumeScreenElements.Enable(element, enabled);

    /// <summary>The Make summary button: makes the summary now, once, for the level and tail on screen (RS-D21). Zero makes none.</summary>
    public void MakeSummary()
    {
        var choice = Selected(out var turnsValid);
        if (choice is { SharedMode: ProviderResumeMode.Summary, Level: not null } && turnsValid && !_busy
            && (_summaryHasLines?.Invoke(choice.Turns) ?? true))
            _ = RunRequestAsync(choice);
    }

    /// <summary>For the screenshots only: chooses the summary mode, at the level on screen, and presses Make summary once. Nothing else calls it.</summary>
    public void MakeSummaryForScreenshot()
    {
        _c.SummaryChoice.IsChecked = true;
        MakeSummary();
    }

    /// <summary>For the screenshots only: chooses Full, which sends no text. Nothing else calls it.</summary>
    public void ChooseFullForScreenshot() => _c.FullChoice.IsChecked = true;

    /// <summary>
    /// Makes the start text for the choice, off the UI thread, and shows it. A summary (a level other than Zero) holds the choices and the buttons until it is
    /// made. A request replaces the one before it: the older one is cancelled and its result is dropped. A failure is shown in the preview area; a cancel
    /// shows nothing. Never throws: every failure is shown on screen.
    /// </summary>
    private async Task RunRequestAsync(TChoice choice)
    {
        if (_previewAsync is null) return;
        CancelRequest();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var id = _request;
        var makesSummary = choice.SharedMode == ProviderResumeMode.Summary && choice.Level is not null;
        _busy = makesSummary;
        ShowWorking(makesSummary);
        Refresh();
        try
        {
            // The kept summary is given only when it is for this setting on the transcript the screen holds (RS-D21): a Make summary press then reuses it,
            // and a build that finds the transcript changed says so (Stale) rather than making a call for a choice change.
            var kept = choice.Level is not null && ResumeSummaryState.KeptMatches(choice.Level, choice.Turns, _lines, _kept) ? _kept : null;
            var preview = await _previewAsync(choice, kept, cts.Token);
            if (id != _request) return;
            if (preview.Lines is { } read) _lines = read;
            if (preview.Stale)
            {
                HidePreview();
                return;
            }
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

    /// <summary>Shows that the text is being made. A summary says one call of the provider is made; Zero reads the session only.</summary>
    private void ShowWorking(bool makesSummary) => ShowMessage(
        makesSummary
            ? $"Making the summary: one {_hooks.Wording.CliName} call. Nothing is sent to a session."
            : "Reading the session. Nothing is sent to a session.",
        problem: false);

    /// <summary>
    /// Shows a built text: its size line, and the text read-only. A preview with no text says why there is none: the window's problem, or that the
    /// session has no messages to start from (Start stays off).
    /// </summary>
    private void ShowPreview(TPreview preview)
    {
        if (preview.Text.Length == 0)
        {
            ShowMessage(preview.Problem ?? "This session has no messages to continue from, so Start is off.", problem: preview.Problem is not null);
            return;
        }
        _preview.Show(preview.Text, ProviderResumeRules.SizeLine(preview.Text.Length));
    }

    /// <summary>Puts a built preview on screen without a request (the window's test seam: TargetChooserTests). Nothing is built or sent.</summary>
    public void ShowBuiltPreview(TPreview preview) => ShowPreview(preview);

    /// <summary>Shows one line in the preview area and no text. A problem is shown in the danger colour. There is then no text for Start.</summary>
    private void ShowMessage(string message, bool problem) => _preview.ShowMessage(message, problem);

    /// <summary>
    /// Shows a failed summary. A summary of the session's own platform that is not made falls back to Full (ResumeDefaults): Full is selected, and the
    /// problem is shown with the line that says so. A summary that starts in another platform does not fall back (that would start another session than
    /// the one chosen): it stays in Summary, the problem shows, and the window's "Make summary with" button is the way on (RS-D28).
    /// </summary>
    private void ShowFailedSummary(TChoice choice, string problem)
    {
        var crossTarget = _target != _hooks.Provider;
        if (crossTarget || !ResumeDefaults.FallBackToFull(choice.SharedMode == ProviderResumeMode.Summary, cancelled: false))
        {
            ShowMessage(problem, problem: true);
            return;
        }
        // Selecting Full clears the preview (OnChoiceChanged), so the problem is shown after it.
        _c.FullChoice.IsChecked = true;
        ShowMessage($"{problem} {ResumeDefaults.FullFallbackLine}", problem: true);
    }

    /// <summary>Takes the preview away. The text is cleared too, so no stale text stays in the dialog.</summary>
    private void HidePreview() => _preview.Hide();

    /// <summary>The Copy button: puts the shown text on the clipboard. Nothing is sent to a session.</summary>
    public void Copy() => _preview.Copy();

    /// <summary>
    /// Start: the choice and the text shown, and the dialog closes. Start is off unless the choice is ready (<see cref="ResumeSummaryState.StartAllowed"/>),
    /// so a text is never sent that was not shown, and no summary is made by Start.
    /// </summary>
    public void Start()
    {
        if (_blocksStart) return;
        var choice = Selected(out var turnsValid);
        if (choice is null || _busy) return;
        var inSummary = choice.SharedMode == ProviderResumeMode.Summary;
        var allowed = !inSummary || ResumeSummaryState.StartAllowed(choice.Level, TailOn, choice.Turns, turnsValid, _preview.Text is not null, _lines, _kept);
        if (!allowed) return;
        if (!_hooks.StartReady(choice)) return;
        Choice = choice.WithTarget(StartTarget(choice.SharedMode)).WithRemember(_c.RememberBox.IsChecked == true);
        PreviewText = choice.SharedMode == ProviderResumeMode.Full ? null : _preview.Text;
        // A Zero start made no summary, so none is kept for it (the kept one is for another setting, if any).
        KeptSummary = choice.Level is null ? null : _kept;
        _close();
    }

    /// <summary>Cancel: no choice, and the dialog closes.</summary>
    public void Cancel()
    {
        Choice = null;
        _close();
    }

    /// <summary>A request still being made is cancelled with the dialog: nothing waits for it.</summary>
    public void OnClosed() => CancelRequest();
}

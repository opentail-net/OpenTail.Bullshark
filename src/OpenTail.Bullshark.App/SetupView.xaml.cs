using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The setup page (S4): the pattern, the folder, the models and efforts per role, the optional assistant for the coders (D-S41),
/// the coder count, the task, and the editable per-pattern briefs. Start checks the run and saves the settings for the next launch.
/// It starts nothing: the launch is built in S5. Reading the folder list and checking a folder write nothing.
/// The page fits one screen (owner, 2026-11-16): the task, the folder and the pattern at the top; models and an editable preview;
/// and Start in a footer that never scrolls, beside one line that says what would start.
/// </summary>
public partial class SetupView : UserControl
{
    private readonly PatternStore _patterns;
    private readonly string _claudeJson = TrustCheck.DefaultClaudeJsonPath;
    private bool _loading = true;
    private bool _updatingBriefEditors;
    private string? _plannerBrief;
    private string? _coderBrief;
    private string _plannerExtra = string.Empty;
    private string _coderExtra = string.Empty;

    /// <summary>The reviewers on offer (D-S36). "self" is marked, because it is the baseline that costs the most.</summary>
    private sealed record ReviewerChoice(string Value, string Label);

    private static readonly IReadOnlyList<ReviewerChoice> ReviewerChoices =
        Choices.Reviewers.Select(v => new ReviewerChoice(v, v == "self" ? "self *" : v)).ToList();

    /// <summary>The assistant choices: none, then the models on offer (D-S41). None is the default.</summary>
    private static readonly IReadOnlyList<string> AssistantOptions = ["none", "reviewer", .. Choices.Models];

    /// <summary>Start was pressed on a valid run, and the settings are saved. The window writes the run and starts it.</summary>
    public event Action<SetupRequest>? RunRequested;

    /// <summary>The folder is untrusted. The window opens its Trust step for this repository root.</summary>
    public event Action<string>? TrustNeeded;

    public SetupView(string stateFile, string defaultTask)
    {
        InitializeComponent();
        _patterns = PatternStore.Beside(stateFile);

        PatternBox.ItemsSource = PatternSpec.Shipped;
        ReviewerBox.ItemsSource = ReviewerChoices;
        PlannerModelBox.ItemsSource = Choices.Models;
        CoderModelBox.ItemsSource = Choices.Models;
        PlannerEffortBox.ItemsSource = Choices.Efforts;
        CoderEffortBox.ItemsSource = Choices.Efforts;
        AssistantEffortBox.ItemsSource = Choices.Efforts;
        foreach (var option in AssistantOptions)
            AssistantBox.Items.Add(new ComboBoxItem { Content = option, Tag = option, ToolTip = AssistantTip(option) });
        TaskBox.Text = defaultTask;
        FolderBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));

        // The folder list: trusted repositories, most recently used first (D-S2). Read only: no run file is written.
        var runs = new RunStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "runs.json")).Load();
        FolderBox.ItemsSource = FolderSuggestions.Build(runs, _claudeJson);

        PatternBox.SelectedIndex = 0;
        _loading = false;
        LoadPattern();
    }

    /// <summary>
    /// Sets the setup's colours from the rail's palette, so the setup follows dark and light with the rail. Replacing a brush
    /// updates every control that uses it. The system colours are set too, because the default combo box and menu templates use them.
    /// </summary>
    public void ApplyTheme(RailPalette palette)
    {
        var resources = Resources;
        resources["SetupBg"] = RailPalette.Brush(palette.Background);
        resources["SetupPanel"] = RailPalette.Brush(palette.Panel);
        resources["SetupText"] = RailPalette.Brush(palette.Text);
        resources["SetupMuted"] = RailPalette.Brush(palette.Muted);
        resources["SetupBorder"] = RailPalette.Brush(palette.Border);
        resources["SetupSelection"] = RailPalette.Brush(palette.Selection);

        // The Start button's greens come from the palette, so Start and the resume prompt's Resume are the same green.
        resources["SetupGo"] = RailPalette.Brush(palette.Go);
        resources["SetupGoHover"] = RailPalette.Brush(palette.GoHover);
        resources["SetupGoPressed"] = RailPalette.Brush(palette.GoPressed);
        resources["SetupGoText"] = RailPalette.Brush(palette.GoText);

        resources[SystemColors.WindowBrushKey] = RailPalette.Brush(palette.Panel);
        resources[SystemColors.ControlBrushKey] = RailPalette.Brush(palette.Panel);
        resources[SystemColors.ControlLightBrushKey] = RailPalette.Brush(palette.Panel);
        resources[SystemColors.ControlDarkBrushKey] = RailPalette.Brush(palette.Border);
        resources[SystemColors.WindowTextBrushKey] = RailPalette.Brush(palette.Text);
        resources[SystemColors.ControlTextBrushKey] = RailPalette.Brush(palette.Text);
        resources[SystemColors.GrayTextBrushKey] = RailPalette.Brush(palette.Muted);
        resources[SystemColors.HighlightBrushKey] = RailPalette.Brush(palette.Selection);
        resources[SystemColors.HighlightTextBrushKey] = RailPalette.Brush(palette.Text);

        Background = RailPalette.Brush(palette.Background);
    }

    /// <summary>Sets the folder field only. The other fields keep what the owner typed.</summary>
    public void ShowFolder(string folder)
    {
        if (string.Equals(FolderBox.Text.Trim(), folder, StringComparison.OrdinalIgnoreCase)) return;
        FolderBox.Text = folder;
    }

    private PatternSpec Pattern => (PatternSpec)PatternBox.SelectedItem;

    private bool IsMany => Pattern.Kind == PatternKind.PlanAndManyCoders;

    private bool IsSolo => Pattern.Kind == PatternKind.Solo;

    /// <summary>The hover text of an assistant choice.</summary>
    private static string AssistantTip(string option) => option switch
    {
        "none" => "No assistant. The default: nothing extra runs or costs.",
        "reviewer" => "The reviewer answers the coders' questions. A coder asks back when a question needs an answer, rather than guessing. " +
                      "With reviewer self, the planner answers; with a model, a session on the reviewer's model does.",
        _ => $"{option}: a helper the coders can ask a quick question. It answers in a few lines, and it costs only when a coder asks.",
    };

    /// <summary>The assistant choice on screen, or none.</summary>
    private string SelectedAssistant => (AssistantBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";

    /// <summary>Opens one of the details tabs: "preview" shows the sessions and the briefs. Used by the screenshots of the self-test.</summary>
    public void ShowDetail(string name)
    {
        if (string.Equals(name, "preview", StringComparison.OrdinalIgnoreCase)) PreviewTab.IsChecked = true;
        else ModelsTab.IsChecked = true;
    }

    private void SelectAssistant(string value)
    {
        var items = AssistantBox.Items.OfType<ComboBoxItem>().ToList();
        AssistantBox.SelectedItem = items.FirstOrDefault(item => string.Equals(item.Tag as string, value, StringComparison.Ordinal))
                                    ?? items.FirstOrDefault();
    }

    /// <summary>Shows the panel of the chosen details tab.</summary>
    private void DetailTab_Checked(object sender, RoutedEventArgs e)
    {
        // The first tab is checked while the page is still being built, before the panels exist.
        if (ModelsPanel is null || PreviewPanel is null) return;
        ModelsPanel.Visibility = ModelsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanel.Visibility = PreviewTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One line for the footer: the sessions that would start and where, or what is missing. It is the short form of the Preview tab.
    /// </summary>
    private static string SummaryOf(SetupRequest request, SetupCheck check)
    {
        if (!check.IsValid) return "Not ready: " + string.Join("  ", check.Errors);

        var sessions = SetupPlan.Build(request, "xxxx")
            .GroupBy(s => $"{s.Role.ToString().ToLowerInvariant()} {s.Model}/{s.Effort}")
            .Select(group => group.Count() > 1 ? $"{group.Count()} × {group.Key}" : group.Key);
        var folder = Path.GetFileName(request.Folder.TrimEnd('\\', '/'));
        // The reviewer is not a session of its own (it is the planner, or a subagent of it), so it is named after the sessions.
        var solo = PatternSpec.Shipped.FirstOrDefault(p => p.Key == request.PatternKey)?.Kind == PatternKind.Solo;
        if (!solo) sessions = sessions.Append($"reviews by {request.Reviewer}");
        return string.Join("  ·  ", sessions) + (folder.Length > 0 ? $"  ·  in {folder}" : string.Empty);
    }

    /// <summary>Shows a result beside Start, under the summary line. An empty text hides the line.</summary>
    private void SetStartStatus(string text)
    {
        StartStatus.Text = text;
        StartStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Loads the saved settings for the chosen pattern, and shows only the rows that pattern uses.</summary>
    private void LoadPattern()
    {
        _loading = true;
        var saved = _patterns.For(Pattern.Key);

        PatternDescription.Text = Pattern.Description;
        PlannerModelBox.SelectedItem = saved.PlannerModel;
        PlannerEffortBox.SelectedItem = saved.PlannerEffort;
        CoderModelBox.SelectedItem = saved.CoderModel;
        CoderEffortBox.SelectedItem = saved.CoderEffort;
        _plannerExtra = saved.PlannerExtra;
        _coderExtra = saved.CoderExtra;
        _plannerBrief = saved.PlannerBrief;
        _coderBrief = saved.CoderBrief;

        CodersRow.Visibility = IsMany ? Visibility.Visible : Visibility.Collapsed;
        CodersBox.ItemsSource = IsMany
            ? Enumerable.Range(Choices.MinManyCoders, Choices.MaxCoders - Choices.MinManyCoders + 1).ToList()
            : null;
        if (IsMany) CodersBox.SelectedItem = saved.Coders;

        // The reviewer belongs to the planner's row, so it shows only where there are coder reports to review.
        ReviewerRow.Visibility = IsSolo ? Visibility.Collapsed : Visibility.Visible;
        ReviewerFootnote.Visibility = ReviewerRow.Visibility;
        ReviewerBox.SelectedItem = ReviewerChoices.FirstOrDefault(c => c.Value == saved.Reviewer) ?? ReviewerChoices[1];
        CoderRow.Visibility = IsSolo ? Visibility.Collapsed : Visibility.Visible;

        // The assistant helps the coders, so it shows only where there are coders. Solo has none.
        AssistantRow.Visibility = IsSolo ? Visibility.Collapsed : Visibility.Visible;
        SelectAssistant(saved.Assistant);
        AssistantEffortBox.SelectedItem = saved.AssistantEffort;

        CoderBriefEditor.Visibility = IsSolo ? Visibility.Collapsed : Visibility.Visible;

        _loading = false;
        Refresh();
    }

    private SetupRequest CurrentRequest() => new(
        PatternKey: Pattern.Key,
        Folder: FolderBox.Text.Trim(),
        Task: TaskBox.Text,
        Coders: IsMany ? (int)(CodersBox.SelectedItem ?? Choices.MinManyCoders) : 1,
        PlannerModel: PlannerModelBox.SelectedItem as string ?? RunPlan.PlannerModel,
        PlannerEffort: PlannerEffortBox.SelectedItem as string ?? RunPlan.PlannerEffort,
        CoderModel: CoderModelBox.SelectedItem as string ?? RunPlan.CoderModel,
        CoderEffort: CoderEffortBox.SelectedItem as string ?? RunPlan.CoderEffort,
        PlannerExtra: _plannerExtra,
        CoderExtra: _coderExtra,
        Reviewer: (ReviewerBox.SelectedItem as ReviewerChoice)?.Value ?? "sonnet",
        Assistant: IsSolo ? "none" : SelectedAssistant,
        AssistantEffort: AssistantEffortBox.SelectedItem as string ?? RunPlan.AssistantEffort,
        PlannerBrief: _plannerBrief,
        CoderBrief: _coderBrief);

    /// <summary>Checks the folder as the owner types, and shows editable briefs when the run would be valid.</summary>
    private void Refresh()
    {
        if (_loading) return;
        var request = CurrentRequest();
        var check = SetupPlan.Validate(request, _claudeJson);
        FolderMessage.Text = check.Folder?.Message ?? string.Empty;
        FolderMessage.Visibility = FolderMessage.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PreviewEmpty.Visibility = check.IsValid ? Visibility.Collapsed : Visibility.Visible;
        PreviewContent.Visibility = check.IsValid ? Visibility.Visible : Visibility.Collapsed;
        SaveBriefsButton.IsEnabled = check.IsValid;
        ResetBriefsButton.IsEnabled = check.IsValid;
        if (check.IsValid)
        {
            var generated = SetupPlan.Build(request with { PlannerBrief = null, CoderBrief = null }, "xxxx");
            _updatingBriefEditors = true;
            SetEditorText(PlannerBriefBox, _plannerBrief ?? SetupPlan.DefaultBriefTemplate(request, SessionRole.Planner));
            if (generated.Any(s => s.Role == SessionRole.Coder))
                SetEditorText(CoderBriefBox, _coderBrief ?? SetupPlan.DefaultBriefTemplate(request, SessionRole.Coder));
            _updatingBriefEditors = false;
            PreviewSessionsText.Text = string.Join("\n", generated.Select(s =>
                $"{s.Name}   {s.Role.ToString().ToLowerInvariant()}   {s.Model} / {s.Effort}   {s.PermissionMode}"));
        }
        SummaryText.Text = SummaryOf(request, check);
        SummaryText.ToolTip = SummaryText.Text;
    }

    private static void SetEditorText(TextBox editor, string text)
    {
        if (!string.Equals(editor.Text, text, StringComparison.Ordinal)) editor.Text = text;
    }

    private void PlannerBrief_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingBriefEditors || _loading) return;
        _plannerBrief = PlannerBriefBox.Text;
        Refresh();
    }

    private void CoderBrief_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingBriefEditors || _loading) return;
        _coderBrief = CoderBriefBox.Text;
        Refresh();
    }

    private void SaveBriefs_Click(object sender, RoutedEventArgs e)
    {
        _plannerBrief = PlannerBriefBox.Text;
        _coderBrief = IsSolo ? null : CoderBriefBox.Text;
        PersistBriefs("Briefs saved.");
    }

    private void ResetBriefs_Click(object sender, RoutedEventArgs e)
    {
        _plannerBrief = null;
        _coderBrief = null;
        _plannerExtra = string.Empty;
        _coderExtra = string.Empty;
        Refresh();
        PersistBriefs("Briefs reset to the templates.", clearLegacyExtras: true);
    }

    private void PersistBriefs(string successMessage, bool clearLegacyExtras = false)
    {
        try
        {
            var saved = _patterns.For(Pattern.Key);
            _patterns.Save(saved with
            {
                PlannerBrief = _plannerBrief,
                CoderBrief = _coderBrief,
                PlannerExtra = clearLegacyExtras ? string.Empty : saved.PlannerExtra,
                CoderExtra = clearLegacyExtras ? string.Empty : saved.CoderExtra,
            });
            SetStartStatus(successMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"briefs not saved: {ex.Message}");
            SetStartStatus($"Briefs not saved: {ex.Message}");
        }
    }

    private void PatternBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PatternBox.SelectedItem is PatternSpec) LoadPattern();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>
    /// Checks the run and, if it is valid, saves the settings for the next launch. Nothing is started, and the run file is
    /// not written: the launch comes in S5.
    /// </summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var request = CurrentRequest();
        var check = SetupPlan.Validate(request, _claudeJson);

        // An untrusted folder opens its Trust step. No run is written; the owner presses Start again after trust.
        if (check.Folder?.Status == FolderStatus.NotTrusted && check.Folder.RepositoryRoot is { } root)
            TrustNeeded?.Invoke(root);

        if (!check.IsValid)
        {
            SetStartStatus("Not started: " + string.Join("  ", check.Errors));
            PreviewContent.Visibility = Visibility.Collapsed;
            PreviewEmpty.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _patterns.Save(new PatternSettings(request.PatternKey, request.PlannerModel, request.PlannerEffort,
                request.CoderModel, request.CoderEffort, request.Coders, request.PlannerExtra, request.CoderExtra, request.Reviewer,
                request.Assistant, request.AssistantEffort, request.PlannerBrief, request.CoderBrief));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"settings not saved: {ex.Message}");
            SetStartStatus($"Not saved: {ex.Message}");
            return;
        }

        PreviewContent.Visibility = Visibility.Visible;
        PreviewEmpty.Visibility = Visibility.Collapsed;
        SetStartStatus("Starting…");
        StartButton.IsEnabled = false;
        RunRequested?.Invoke(request);
    }

    /// <summary>Called when a run request is finished or refused: the Start button works again.</summary>
    public void RunFinished() => StartButton.IsEnabled = true;

    /// <summary>Shows the outcome of a started run. The window calls this after it writes the run.</summary>
    public void ShowStarted(string text) => SetStartStatus(text);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the run" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) FolderBox.Text = dialog.FolderName;
    }
}

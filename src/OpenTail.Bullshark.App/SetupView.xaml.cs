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
/// the coder count, the task, and the owner's extra text per brief. Start checks the run and saves the settings for the next
/// launch. It starts nothing: the launch is built in S5. Reading the folder list and checking a folder write nothing.
/// </summary>
public partial class SetupView : UserControl
{
    private readonly PatternStore _patterns;
    private readonly string _claudeJson = TrustCheck.DefaultClaudeJsonPath;
    private bool _loading = true;

    /// <summary>The reviewers on offer (D-S36). "self" is marked, because it is the baseline that costs the most.</summary>
    private sealed record ReviewerChoice(string Value, string Label);

    private static readonly IReadOnlyList<ReviewerChoice> ReviewerChoices =
        Choices.Reviewers.Select(v => new ReviewerChoice(v, v == "self" ? "self *" : v)).ToList();

    /// <summary>The assistant choices: none, then the models on offer (D-S41). None is the default.</summary>
    private static readonly IReadOnlyList<string> AssistantOptions = ["none", .. Choices.Models];

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
        foreach (var option in AssistantOptions) AssistantTabs.Children.Add(AssistantTab(option));
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

    /// <summary>One assistant choice, as a tab. Its hover text says what the choice does.</summary>
    private RadioButton AssistantTab(string option)
    {
        var tab = new RadioButton
        {
            Content = option,
            Tag = option,
            GroupName = "Assistant",
            Style = (Style)Resources["AssistantTab"],
            ToolTip = option == "none"
                ? "No assistant. The default: nothing extra runs or costs."
                : $"{option}: a helper the coders can ask a quick question. It answers in a few lines, and it costs only when a coder asks.",
        };
        tab.Checked += (_, _) => Refresh();
        return tab;
    }

    /// <summary>The assistant choice on screen: the chosen tab, or none.</summary>
    private string SelectedAssistant =>
        AssistantTabs.Children.OfType<RadioButton>().FirstOrDefault(tab => tab.IsChecked == true)?.Tag as string ?? "none";

    private void SelectAssistant(string value)
    {
        foreach (var tab in AssistantTabs.Children.OfType<RadioButton>())
            tab.IsChecked = string.Equals(tab.Tag as string, value, StringComparison.Ordinal);
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
        PlannerExtraBox.Text = saved.PlannerExtra;
        CoderExtraBox.Text = saved.CoderExtra;

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

        CoderExtraLabel.Visibility = IsSolo ? Visibility.Collapsed : Visibility.Visible;
        CoderExtraBox.Visibility = IsSolo ? Visibility.Collapsed : Visibility.Visible;
        PlannerExtraLabel.Text = IsSolo ? "Extra for the solo session's brief (optional)" : "Extra for the planner's brief (optional)";

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
        PlannerExtra: PlannerExtraBox.Text,
        CoderExtra: CoderExtraBox.Text,
        Reviewer: (ReviewerBox.SelectedItem as ReviewerChoice)?.Value ?? "sonnet",
        Assistant: IsSolo ? "none" : SelectedAssistant,
        AssistantEffort: AssistantEffortBox.SelectedItem as string ?? RunPlan.AssistantEffort);

    /// <summary>Checks the folder as the owner types, and shows the preview when the run would be valid.</summary>
    private void Refresh()
    {
        if (_loading) return;
        var request = CurrentRequest();
        var check = SetupPlan.Validate(request, _claudeJson);
        FolderMessage.Text = check.Folder?.Message ?? string.Empty;
        PreviewText.Text = check.IsValid ? PreviewOf(request) : string.Empty;
    }

    /// <summary>
    /// The sessions that would start, with the full brief and resume prompt of each. Names get their run id at launch,
    /// so the preview uses a placeholder.
    /// </summary>
    private static string PreviewOf(SetupRequest request)
    {
        var sessions = SetupPlan.Build(request, "xxxx");
        return string.Join("\n\n", sessions.Select(s =>
            $"{s.Name}   {s.Role.ToString().ToLowerInvariant()}   {s.Model} / {s.Effort}   {s.PermissionMode}\n" +
            $"brief:\n{s.Brief}\n" +
            $"resume prompt:\n{s.ResumePrompt}"));
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
            StartStatus.Text = "Not started: " + string.Join("  ", check.Errors);
            PreviewText.Text = string.Empty;
            return;
        }

        try
        {
            _patterns.Save(new PatternSettings(request.PatternKey, request.PlannerModel, request.PlannerEffort,
                request.CoderModel, request.CoderEffort, request.Coders, request.PlannerExtra, request.CoderExtra, request.Reviewer,
                request.Assistant, request.AssistantEffort));
        }
        catch (IOException ex)
        {
            AppLog.Write($"settings not saved: {ex.Message}");
            StartStatus.Text = $"Not saved: {ex.Message}";
            return;
        }

        PreviewText.Text = PreviewOf(request);
        StartStatus.Text = "Starting…";
        RunRequested?.Invoke(request);
    }

    /// <summary>Shows the outcome of a started run. The window calls this after it writes the run.</summary>
    public void ShowStarted(string text) => StartStatus.Text = text;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the run" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) FolderBox.Text = dialog.FolderName;
    }
}

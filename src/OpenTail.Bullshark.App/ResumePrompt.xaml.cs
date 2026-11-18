using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
/// are the model and effort of the resumed or new session (the summary itself is always Haiku at low effort).
/// </summary>
public sealed record ResumeChoice(ResumeMode Mode, SummaryLevel? Level, int Turns, string Model, string Effort);

/// <summary>
/// The resume prompt (D14, D-S11). One list of ways to continue, with the recommended one already selected: a full resume only
/// when the context is small (ResumeAdvice), otherwise a summary. It shows what each choice would send before anything runs.
/// The owner picks the model and effort of the session that continues. Cancel is the default.
/// </summary>
public partial class ResumePrompt : Window
{
    private const int MaxTurns = 200;
    private readonly int _contextTokens;

    public ResumePrompt(string heading, string facts, long contextTokens, RailPalette palette, string defaultModel, string defaultEffort)
    {
        InitializeComponent();
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
        (fullRecommended ? FullChoice : SummaryChoice).IsChecked = true;

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

    /// <summary>The summary level on screen. Normal when none is chosen.</summary>
    private SummaryLevel SelectedLevel() =>
        Enum.TryParse<SummaryLevel>(SelectedTag(LevelRow), out var level) ? level : SummaryLevel.Normal;

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
    /// Says what the selected choice costs and sends, before the owner presses Resume. It also shows only the options that apply to
    /// the choice: the summary levels for a summary, and the turns for anything but a full resume.
    /// </summary>
    private void UpdateCost()
    {
        var choice = Selected(out var turnsValid);
        LevelBlock.Visibility = SummaryChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TurnsBlock.Visibility = FullChoice.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

        var sizes = turnsValid ? string.Empty : $" Enter a whole number from 0 to {MaxTurns} for the turns.";
        var runs = choice is null ? string.Empty : $" The new session runs at {choice.Model} {choice.Effort}.";
        CostText.Text = (choice?.Mode switch
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

        Choice = choice;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = null;
        Close();
    }
}

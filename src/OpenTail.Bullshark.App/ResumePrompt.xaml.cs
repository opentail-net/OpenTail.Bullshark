using System.Globalization;
using System.Windows;
using OpenTail.Bullshark.Core.Resume;

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

/// <summary>The choice made in the resume prompt. Level is set for a summary; Turns is the number of turns kept verbatim.</summary>
public sealed record ResumeChoice(ResumeMode Mode, SummaryLevel? Level, int Turns);

/// <summary>
/// The resume prompt (D14, D-S11). It shows what a resume would send before anything runs. A full resume re-reads the whole
/// context. A summary makes one model call. The last turns cost nothing extra. Cancel is the default.
/// </summary>
public partial class ResumePrompt : Window
{
    private const int MaxTurns = 200;
    private readonly int _contextTokens;

    public ResumePrompt(string heading, string facts, long contextTokens, RailPalette palette, ResumeMode startWith)
    {
        InitializeComponent();
        Resources["PromptBg"] = RailPalette.Brush(palette.Background);
        Resources["PromptPanel"] = RailPalette.Brush(palette.Panel);
        Resources["PromptText"] = RailPalette.Brush(palette.Text);
        Resources["PromptMuted"] = RailPalette.Brush(palette.Muted);
        Resources["PromptBorder"] = RailPalette.Brush(palette.Border);
        Resources["PromptSelection"] = RailPalette.Brush(palette.Selection);

        Heading.Text = heading;
        Facts.Text = facts;
        _contextTokens = (int)Math.Min(contextTokens, int.MaxValue);
        TurnsBox.Text = ResumeText.DefaultTurns.ToString(CultureInfo.InvariantCulture);

        foreach (var radio in new[] { FullChoice, MinimalChoice, NormalChoice, CompleteChoice, TailChoice })
            radio.Checked += (_, _) => UpdateCost();
        TurnsBox.TextChanged += (_, _) => UpdateCost();

        (startWith == ResumeMode.Full ? FullChoice : NormalChoice).IsChecked = true;
        UpdateCost();
    }

    /// <summary>The choice, or null when the owner cancelled.</summary>
    public ResumeChoice? Choice { get; private set; }

    /// <summary>The choice on screen, or null when none is selected or the turns are not a whole number.</summary>
    private ResumeChoice? Selected(out bool turnsValid)
    {
        var parsed = int.TryParse(TurnsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var turns);
        turnsValid = parsed && turns is >= 0 and <= MaxTurns;
        var kept = turnsValid ? turns : ResumeText.DefaultTurns;

        if (FullChoice.IsChecked == true) return new ResumeChoice(ResumeMode.Full, null, 0);
        if (MinimalChoice.IsChecked == true) return new ResumeChoice(ResumeMode.Summary, SummaryLevel.Minimal, kept);
        if (NormalChoice.IsChecked == true) return new ResumeChoice(ResumeMode.Summary, SummaryLevel.Normal, kept);
        if (CompleteChoice.IsChecked == true) return new ResumeChoice(ResumeMode.Summary, SummaryLevel.Complete, kept);
        if (TailChoice.IsChecked == true) return new ResumeChoice(ResumeMode.TailOnly, null, kept);
        return null;
    }

    /// <summary>Says what the selected choice costs and sends, before the owner presses Resume.</summary>
    private void UpdateCost()
    {
        var choice = Selected(out var turnsValid);
        var sizes = turnsValid ? string.Empty : $" Enter a whole number from 0 to {MaxTurns} for the turns.";
        CostText.Text = (choice?.Mode switch
        {
            ResumeMode.Full =>
                $"Resuming in full re-reads all of the context, about {_contextTokens:N0} tokens, before the first reply. " +
                "The cache has gone cold, so that is charged at full price. Nothing runs until you press Resume.",
            ResumeMode.Summary =>
                $"One model call (Haiku, low effort) reads up to about {ResumeText.SummaryInputChars / 4:N0} tokens of this " +
                "session and writes the summary. Then a new session starts from it.",
            ResumeMode.TailOnly =>
                "No model call. The last turns are copied word for word into a new session.",
            _ => "Choose one of the ways to resume.",
        }) + sizes;
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

using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The small dialog of "New Codex session…" (P9, decision M-D4 b): the model of the new session, asked after the folder is chosen. The dropdown is filled
/// when the model list is in (the window reads it once per run), and OK is off until then. Cancel starts nothing. <see cref="Accepted"/> and
/// <see cref="Model"/> hold the answer: the caller reads them after <see cref="Window.ShowDialog"/> returns.
/// </summary>
public partial class CodexModelPrompt : Window
{
    /// <param name="heading">The heading: the folder the session starts in.</param>
    /// <param name="palette">The theme, as the rest of the window's dialogs use it.</param>
    /// <param name="models">The model list, read off the UI thread. Its failure shows as Codex's default (see <see cref="CodexModelChoices"/>).</param>
    public CodexModelPrompt(string heading, RailPalette palette, Task<CodexModelReading> models)
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
        ModelBox.Items.Add(new ComboBoxItem { Content = "Reading the model list...", IsEnabled = false });
        ModelBox.SelectedIndex = 0;
        ModelBox.IsEnabled = false;
        ModelNote.Text = "Reading the model list.";
        _ = LoadAsync(models);
    }

    /// <summary>True when OK was pressed. False when the dialog was cancelled or closed.</summary>
    public bool Accepted { get; private set; }

    /// <summary>The model id to start with, or null for Codex's own default (the one entry of a list that could not be read).</summary>
    public string? Model { get; private set; }

    /// <summary>Fills the dropdown when the list is in, and turns OK on. Never throws: a failure of the read shows as Codex's default.</summary>
    private async Task LoadAsync(Task<CodexModelReading> models)
    {
        CodexModelReading reading;
        try
        {
            reading = await models;
        }
        catch (Exception ex)
        {
            AppLog.Write($"codex model list not read in the dialog: {ex.GetType().Name}");
            reading = new CodexModelReading([], 0, 0, CodexModelProblem.Unexpected);
        }
        ModelNote.Text = CodexModelChoices.Fill(ModelBox, reading);
        ModelBox.IsEnabled = true;
        OkButton.IsEnabled = true;
    }

    /// <summary>OK: the chosen model (or null for Codex's default) is kept, and the dialog closes.</summary>
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!OkButton.IsEnabled) return;
        Model = CodexModelChoices.SelectedModel(ModelBox);
        Accepted = true;
        Close();
    }

    /// <summary>Cancel: nothing is kept, and the caller starts nothing.</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}

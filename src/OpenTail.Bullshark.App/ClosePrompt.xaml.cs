using System.Windows;

namespace OpenTail.Bullshark.App;

/// <summary>What the owner chose when closing a session's tab.</summary>
public enum ClosePromptChoice
{
    Cancel,
    CloseTab,
    CloseSessionAndTab,
}

/// <summary>
/// The prompt that opens when a session's tab is closed: stop the session and close its tab, close the tab only, or cancel.
/// It is modal and takes the rail's palette, so it follows the dark and light themes.
/// </summary>
public partial class ClosePrompt : Window
{
    public ClosePrompt(string sessionName, RailPalette palette)
    {
        InitializeComponent();
        UiTheme.Apply(palette);
        Resources["PromptBg"] = RailPalette.Brush(palette.Background);
        Resources["PromptPanel"] = RailPalette.Brush(palette.Panel);
        Resources["PromptText"] = RailPalette.Brush(palette.Text);
        Resources["PromptMuted"] = RailPalette.Brush(palette.Muted);
        Resources["PromptBorder"] = RailPalette.Brush(palette.Border);
        Resources["PromptSelection"] = RailPalette.Brush(palette.Selection);
        Heading.Text = $"Close {sessionName}?";
    }

    /// <summary>The choice made. Cancel unless a button was pressed.</summary>
    public ClosePromptChoice Choice { get; private set; } = ClosePromptChoice.Cancel;

    private void CloseSessionAndTab_Click(object sender, RoutedEventArgs e) => Finish(ClosePromptChoice.CloseSessionAndTab);

    private void CloseTab_Click(object sender, RoutedEventArgs e) => Finish(ClosePromptChoice.CloseTab);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(ClosePromptChoice.Cancel);

    private void Finish(ClosePromptChoice choice)
    {
        Choice = choice;
        Close();
    }
}
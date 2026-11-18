using System.Windows;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The command palette (ideas D3): a box and a list of entries, filtered by <see cref="CommandPalette.Filter"/> as the owner types. It is a
/// modal window owned by the session window, not a panel drawn in it: the terminal is a native window, and WPF content cannot be drawn over
/// it (README, Airspace). A window also takes the keyboard reliably, as the close prompt does. Up and Down move, Enter or a double click
/// chooses, Esc closes. <see cref="ChosenId"/> is the entry chosen, or null when the palette closed without one.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly IReadOnlyList<PaletteEntry> _entries;

    public CommandPaletteWindow(IReadOnlyList<PaletteEntry> entries, RailPalette palette)
    {
        InitializeComponent();
        _entries = entries;
        Resources["PalBg"] = RailPalette.Brush(palette.Background);
        Resources["PalPanel"] = RailPalette.Brush(palette.Panel);
        Resources["PalText"] = RailPalette.Brush(palette.Text);
        Resources["PalMuted"] = RailPalette.Brush(palette.Muted);
        Resources["PalBorder"] = RailPalette.Brush(palette.Border);
        Resources["PalSelection"] = RailPalette.Brush(palette.Selection);
        Resources["PalSelectionText"] = RailPalette.Brush(palette.SelectionText);
        Loaded += (_, _) =>
        {
            Activate();
            QueryBox.Focus();
        };
        Refresh();
    }

    /// <summary>The Id of the entry the owner chose, or null when the palette closed without one.</summary>
    public string? ChosenId { get; private set; }

    /// <summary>Lists what the query matches, and selects the first row.</summary>
    private void Refresh()
    {
        var shown = CommandPalette.Filter(_entries, QueryBox.Text);
        Results.ItemsSource = shown;
        Results.SelectedIndex = shown.Count > 0 ? 0 : -1;
        NoMatch.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QueryBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    /// <summary>Keys work wherever the focus is in the palette, so a click on a row does not take them away from the box.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Enter:
                e.Handled = true;
                Choose();
                break;
            case Key.Down:
                e.Handled = true;
                Move(1);
                break;
            case Key.Up:
                e.Handled = true;
                Move(-1);
                break;
        }
    }

    private void Results_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Choose();

    private void Move(int step)
    {
        if (Results.Items.Count == 0) return;
        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + step, 0, Results.Items.Count - 1);
        Results.ScrollIntoView(Results.SelectedItem);
    }

    /// <summary>Chooses the selected row and closes. With no row, nothing happens.</summary>
    private void Choose()
    {
        if (Results.SelectedItem is not PaletteEntry entry) return;
        ChosenId = entry.Id;
        Close();
    }
}

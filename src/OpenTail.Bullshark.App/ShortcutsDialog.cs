using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The keyboard shortcut sheet (firstrun batch B7), opened from the command palette. It lists <see cref="Shortcuts.All"/> grouped by where each works,
/// in the rail's colours. It is read only: it sends nothing and changes no setting.
/// </summary>
public sealed class ShortcutsDialog : Window
{
    public ShortcutsDialog(RailPalette palette)
    {
        Title = "Keyboard shortcuts";
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RailPalette.Brush(palette.Background);
        Foreground = RailPalette.Brush(palette.Text);

        var rows = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var scope in new[] { Shortcuts.Window, Shortcuts.Transcript, Shortcuts.Find, Shortcuts.Results })
        {
            rows.Children.Add(new TextBlock
            {
                Text = scope,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4),
            });
            foreach (var shortcut in Shortcuts.In(scope)) rows.Children.Add(Row(shortcut));
        }

        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Thickness(16) };
        var note = new TextBlock
        {
            Text = "The chords work as shown. Ctrl+plus and Ctrl+minus change the session text; nothing here is sent to a session.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = RailPalette.Brush(palette.Muted),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows });
        Content = root;
    }

    /// <summary>One shortcut: the chord in a fixed-width face, then what it does.</summary>
    private static UIElement Row(Shortcut shortcut)
    {
        var chord = new TextBlock
        {
            Text = shortcut.Chord,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold,
            Width = 150,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var does = new TextBlock
        {
            Text = shortcut.Does,
            TextWrapping = TextWrapping.Wrap,
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(chord, Dock.Left);
        row.Children.Add(chord);
        row.Children.Add(does);
        System.Windows.Automation.AutomationProperties.SetName(row, $"{shortcut.Chord}: {shortcut.Does}");
        return row;
    }
}

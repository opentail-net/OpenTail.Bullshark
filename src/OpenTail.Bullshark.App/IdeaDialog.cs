using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The box for "Add idea…" (batch B3), built in code in the rail's palette. Enter saves and Esc cancels. The dialog writes nothing: the
/// window reads <see cref="Text"/> when the dialog closes with a result, and appends it to ideas.md.
/// </summary>
public sealed class IdeaDialog : Window
{
    private readonly TextBox _box;

    public IdeaDialog(RailPalette palette)
    {
        Title = "Add idea";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RailPalette.Brush(palette.Panel);
        Foreground = RailPalette.Brush(palette.Text);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "The idea goes to ideas.md beside the state file. Nothing is sent to a session. Enter saves, Esc cancels.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = RailPalette.Brush(palette.Muted),
            Margin = new Thickness(0, 0, 0, 8),
        });

        _box = new TextBox
        {
            Padding = new Thickness(6, 4, 6, 4),
            MaxLength = IdeaNotes.MaxLength,
        };
        panel.Children.Add(_box);

        var save = new Button { Content = "Save", Width = 90, Margin = new Thickness(0, 10, 8, 0), IsDefault = true, Padding = new Thickness(6, 3, 6, 3) };
        save.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 10, 0, 0), IsCancel = true, Padding = new Thickness(6, 3, 6, 3) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) =>
        {
            Activate();
            _box.Focus();
        };
    }

    /// <summary>The idea as typed. Read by the window after the dialog closes with a result.</summary>
    public string Text => _box.Text;
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenTail.Bullshark.Core.Library;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The dialog that edits one session's name, tags, note and colour (session library, D-L10). It is built in code, in the rail's
/// palette. Save sets <see cref="Result"/>; Cancel leaves it null, and nothing is written.
/// </summary>
public sealed class LibraryEditDialog : Window
{
    private readonly LibraryEntry _current;
    private readonly TextBox _name;
    private readonly TextBox _tags;
    private readonly TextBox _note;
    private readonly ComboBox _colour;

    public LibraryEditDialog(LibraryEntry current, string fallbackTitle, RailPalette palette)
    {
        _current = current;
        Title = "Edit session";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RailPalette.Brush(palette.Panel);
        Foreground = RailPalette.Brush(palette.Text);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Caption($"Name. Blank shows the title from the transcript: {fallbackTitle}", palette));
        _name = Box(current.Name ?? string.Empty, palette);
        panel.Children.Add(_name);

        panel.Children.Add(Caption("Tags, separated by spaces or commas. Written in lower case.", palette));
        _tags = Box(string.Join(" ", current.Tags ?? []), palette);
        panel.Children.Add(_tags);

        panel.Children.Add(Caption("Note", palette));
        _note = Box(current.Note ?? string.Empty, palette);
        _note.AcceptsReturn = true;
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Height = 80;
        _note.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        panel.Children.Add(_note);

        panel.Children.Add(Caption("Colour. It shows as a bar at the edge of the row and of the tab.", palette));
        _colour = new ComboBox { Margin = new Thickness(0, 4, 0, 12), HorizontalAlignment = HorizontalAlignment.Left, Width = 200 };
        _colour.Items.Add(new ComboBoxItem { Content = "None", Tag = string.Empty });
        foreach (var (name, dark, light) in RailPalette.SessionColours)
        {
            var swatch = new Rectangle { Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), Fill = RailPalette.Brush(palette.IsLight ? light : dark) };
            _colour.Items.Add(new ComboBoxItem
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, new TextBlock { Text = name } } },
                Tag = name,
            });
        }
        _colour.SelectedIndex = Math.Max(0, _colour.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => (string)item.Tag == (current.Colour ?? string.Empty)));
        panel.Children.Add(_colour);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true, Padding = new Thickness(6, 3, 6, 3) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true, Padding = new Thickness(6, 3, 6, 3) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
    }

    /// <summary>What Save returned, or null when the dialog was cancelled. Pinned and archived keep their values from before.</summary>
    public LibraryEntry? Result { get; private set; }

    private void Save()
    {
        var colour = _colour.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag.Length > 0 ? tag : null;
        Result = new LibraryEntry(
            Name: _name.Text,
            Pinned: _current.Pinned,
            Archived: _current.Archived,
            Tags: SessionLibrary.TagsOf(_tags.Text),
            Note: _note.Text,
            Colour: colour);
        DialogResult = true;
    }

    private static TextBlock Caption(string text, RailPalette palette) => new()
    {
        Text = text,
        Margin = new Thickness(0, 8, 0, 3),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = RailPalette.Brush(palette.Muted),
    };

    private static TextBox Box(string text, RailPalette palette) => new()
    {
        Text = text,
        Padding = new Thickness(6, 4, 6, 4),
        Background = RailPalette.Brush(palette.Background),
        Foreground = RailPalette.Brush(palette.Text),
        CaretBrush = RailPalette.Brush(palette.Text),
        BorderBrush = RailPalette.Brush(palette.Muted),
    };
}

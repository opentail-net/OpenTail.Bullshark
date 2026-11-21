using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenTail.Bullshark.Core.Library;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The dialog that edits one session's name, tags, note, plan file and colour (session library, D-L10; plan progress, Part 2). It is built in code,
/// in the rail's palette. Save sets <see cref="Result"/>; Cancel leaves it null, and nothing is written. A plan file must be a full path: a
/// relative one is refused here, so Save keeps the dialog open.
/// </summary>
public sealed class LibraryEditDialog : Window
{
    private readonly LibraryEntry _current;
    private readonly TextBox _name;
    private readonly TextBox _tags;
    private readonly TextBox _note;
    private readonly TextBox _plan;
    private readonly TextBlock _planError;
    private readonly ComboBox _colour;
    private readonly string _fallbackTitle;

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

        panel.Children.Add(Caption("Name. It starts as the title shown now: change it, or clear it to show the title from the transcript.", palette));
        // The box starts with the name shown now, so an edit is a change of a word, not a retype. With no name of the owner's, that is the title from
        // the transcript; Save does not turn it into a name of the owner's unless it was changed (see NameToSave).
        _name = Box(string.IsNullOrEmpty(current.Name) ? fallbackTitle : current.Name, palette);
        _fallbackTitle = fallbackTitle;
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
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

        // The plan file is attached by hand (plan progress, Part 2). Its checkboxes show as "3/7" on the row. Browse fills the box; nothing is read here.
        panel.Children.Add(Caption("Plan file (optional, a Markdown file with checkboxes). Its ticked items show as 3/7 on the row.", palette));
        var planRow = new DockPanel();
        var browse = new Button { Content = "Browse…", Width = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
        browse.Click += (_, _) => BrowsePlan();
        DockPanel.SetDock(browse, Dock.Right);
        _plan = Box(current.PlanFile ?? string.Empty, palette);
        planRow.Children.Add(browse);
        planRow.Children.Add(_plan);
        panel.Children.Add(planRow);
        _planError = new TextBlock
        {
            Text = "The plan file must be a full path, such as C:\\work\\plan.md.",
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = RailPalette.Brush(palette.IsLight ? "#B83227" : "#E5534B"),
        };
        panel.Children.Add(_planError);

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
        var plan = PlanProgress.PathOf(_plan.Text);
        // A relative plan path would be read from the app's own folder, so the dialog stays open until it is a full path (or empty).
        if (plan is not null && !PlanProgress.IsFullPath(plan))
        {
            _planError.Visibility = Visibility.Visible;
            return;
        }

        var colour = _colour.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag.Length > 0 ? tag : null;
        Result = new LibraryEntry(
            Name: NameToSave(_name.Text, _current.Name, _fallbackTitle),
            Pinned: _current.Pinned,
            Archived: _current.Archived,
            Tags: SessionLibrary.TagsOf(_tags.Text),
            Note: _note.Text,
            Colour: colour,
            PlanFile: plan);
        DialogResult = true;
    }

    /// <summary>
    /// The name Save keeps. The box starts with the title shown now, so a name of the owner's is stored only when the text differs from it: an
    /// untouched box over the transcript's title (the session had no name of the owner's) stays blank, and the title keeps following the transcript.
    /// A cleared box is blank too. A session that already had a name keeps whatever the box holds.
    /// </summary>
    internal static string NameToSave(string text, string? currentName, string fallbackTitle) =>
        string.IsNullOrEmpty(currentName) && string.Equals(text, fallbackTitle, StringComparison.Ordinal) ? string.Empty : text;

    /// <summary>Chooses a plan file with the system's open dialog. The path goes in the box; the file is read only after Save.</summary>
    private void BrowsePlan()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the plan file",
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        _plan.Text = dialog.FileName;
        _planError.Visibility = Visibility.Collapsed;
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
    };
}

using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Storage;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Storage page (gems item 6): the project folders by size, the transcripts of a chosen folder with checkboxes, and the archive
/// with its own checkboxes and Restore. It only draws and raises events; the window reads and moves the files on a pool thread.
/// </summary>
public sealed class StorageView : UserControl
{
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };

    /// <summary>The line that says how long Claude keeps transcripts (round 7 item 5). It shows under the note.</summary>
    private readonly TextBlock _retention = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        Text = TranscriptRetention.Sentence(TranscriptRetention.DefaultDays, fromSettings: false),
    };

    /// <summary>The deletion age the transcript rows are marked against (round 7 item 5).</summary>
    private int _retentionDays = TranscriptRetention.DefaultDays;
    private readonly StackPanel _projects = new();
    private readonly StackPanel _transcripts = new();
    private readonly StackPanel _archived = new();
    private readonly TextBlock _transcriptsCaption = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _archivedCaption = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
    private readonly Button _archive;
    private readonly TextBlock _archiveWait = new()
    {
        Text = "Archive waits until claude's session list has been read, so that no running session is moved.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    public StorageView()
    {
        var refresh = new Button { Content = UiIcon.Label(UiIcon.Refresh, "Refresh"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();

        _archive = new Button { Content = UiIcon.Label(UiIcon.Archive, "Archive checked…"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        _archive.Click += (_, _) => ArchiveRequested?.Invoke(Checked(_transcripts));
        var restore = new Button { Content = UiIcon.Label(UiIcon.Restore, "Restore checked"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        restore.Click += (_, _) => RestoreRequested?.Invoke(Checked(_archived));

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(refresh);

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Projects, largest first", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        left.Children.Add(new ScrollViewer { Content = _projects, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        right.Children.Add(_transcriptsCaption);
        right.Children.Add(new ScrollViewer { Content = _transcripts, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        rightButtons.Children.Add(_archive);
        right.Children.Add(_archiveWait);
        right.Children.Add(rightButtons);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var archivedButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        archivedButtons.Children.Add(restore);
        var bottom = new StackPanel();
        bottom.Children.Add(_archivedCaption);
        bottom.Children.Add(new ScrollViewer { Content = _archived, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        bottom.Children.Add(archivedButtons);

        var root = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        // No page title here: the Insights tab's sub-tab row names the page (2026-11-18).
        root.Children.Add(_note);
        root.Children.Add(_retention);
        root.Children.Add(toolbar);
        root.Children.Add(columns);
        root.Children.Add(bottom);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _note.Text = "Archive moves a transcript out of History and search into the archive folder. Restore moves it back. Nothing is deleted. " +
                     "A session that claude lists, or that Bullshark records, is never archived.";
        _transcriptsCaption.Text = "Choose a project to see its transcripts";
        _archivedCaption.Text = "Archived";
    }

    /// <summary>Takes the rail's palette, as the other views do. The text follows the theme; the notes are muted.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        Foreground = RailPalette.Brush(palette.Text);
        var muted = RailPalette.Brush(palette.Muted);
        _note.Foreground = muted;
        _retention.Foreground = muted;
        _archiveWait.Foreground = muted;
    }

    /// <summary>The Refresh button was pressed.</summary>
    public event Action? RefreshRequested;

    /// <summary>A project folder was chosen. The argument is the folder's full path.</summary>
    public event Action<string>? ProjectChosen;

    /// <summary>The Archive button was pressed, with the checked transcripts' paths.</summary>
    public event Action<IReadOnlyList<string>>? ArchiveRequested;

    /// <summary>The Restore button was pressed, with the checked archived paths.</summary>
    public event Action<IReadOnlyList<string>>? RestoreRequested;

    /// <summary>"Open Claude's folder" was pressed, on the empty state (firstrun batch B8).</summary>
    public event Action? ClaudeFolderRequested;

    /// <summary>Shows the projects and the archive. The transcripts pane keeps what it shows until a project is chosen.</summary>
    /// <param name="names">The name each project is shown by, by its folder (P9: the working folder's leaf). A folder not in it keeps Claude's name.</param>
    /// <param name="projectsRootText">The projects folder as the owner reads it (with ~ for the profile), shown when there are no projects (firstrun batch B8).</param>
    public void ShowProjects(IReadOnlyList<ProjectStorage> projects, IReadOnlyList<TranscriptFileInfo> archived,
        IReadOnlyDictionary<string, string>? names = null, string? projectsRootText = null)
    {
        _projects.Children.Clear();
        foreach (var project in projects)
        {
            var span = project.Oldest is { } oldest && project.Newest is { } newest
                ? $"{oldest.ToLocalTime():yyyy-MM-dd} to {newest.ToLocalTime():yyyy-MM-dd}"
                : "no transcripts";
            var name = names is not null && names.TryGetValue(project.Folder, out var shown) ? shown : project.Name;
            var row = new Button
            {
                Content = $"{name}\n{project.Transcripts} transcripts · {Size(project.Bytes)} · {span}",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = project.Folder,
                ToolTip = project.Folder,
            };
            row.Click += (_, _) => ProjectChosen?.Invoke(project.Folder);
            _projects.Children.Add(row);
        }
        if (projects.Count == 0) _projects.Children.Add(EmptyProjects(projectsRootText));

        _archived.Children.Clear();
        foreach (var file in archived) _archived.Children.Add(FileRow(file, marked: false));
        _archivedCaption.Text = archived.Count == 0 ? "Archived: none" : $"Archived: {archived.Count}";
    }

    /// <summary>
    /// The empty state with a next step (firstrun batch B8): where the transcripts were looked for, when Claude Code writes them, and a button to open
    /// Claude's folder. Without a folder text it keeps the short line.
    /// </summary>
    private UIElement EmptyProjects(string? rootText)
    {
        if (rootText is null)
            return new TextBlock { Text = "No transcripts were found.", Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = $"No transcripts found under {rootText}. Claude Code writes them when a session runs.",
            TextWrapping = TextWrapping.Wrap,
        });
        var open = new Button
        {
            Content = UiIcon.Label(UiIcon.Folder, "Open Claude's folder"),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        open.Click += (_, _) => ClaudeFolderRequested?.Invoke();
        panel.Children.Add(open);
        return panel;
    }

    /// <summary>Shows the transcripts of one project, with checkboxes. The ones near their deletion, or past it, say so.</summary>
    public void ShowTranscripts(string projectName, IReadOnlyList<TranscriptFileInfo> files)
    {
        _transcriptsCaption.Text = $"Transcripts in {projectName}: {files.Count}";
        _transcripts.Children.Clear();
        foreach (var file in files) _transcripts.Children.Add(FileRow(file, marked: true));
    }

    /// <summary>
    /// Shows how long Claude keeps transcripts, under the note (round 7 item 5). The sentence says whether the age is the default or the owner's
    /// setting, and a problem with settings.json is said after it, with the default shown.
    /// </summary>
    public void SetRetention(int days, bool fromSettings, string? problem)
    {
        _retentionDays = days;
        var sentence = TranscriptRetention.Sentence(days, fromSettings);
        _retention.Text = problem is null ? sentence : $"{sentence} settings.json could not be used ({problem}), so Claude's default is shown.";
    }

    /// <summary>Shows a line under the note: the outcome of the last archive or restore.</summary>
    public void SetResult(string text) => _note.Text = text;

    /// <summary>
    /// Enables the Archive button only once the session list has been read (round 5 item 4). While it is off, a note says why.
    /// </summary>
    public void SetArchiveAvailable(bool available)
    {
        _archive.IsEnabled = available;
        _archiveWait.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// One transcript as a checkbox. A transcript that is marked (near its deletion, or past it) says so: "deleted in 2 days" or "due for
    /// deletion at the next clean-up". Its age is counted from its last write (round 7 item 5).
    /// </summary>
    private CheckBox FileRow(TranscriptFileInfo file, bool marked)
    {
        var text = $"{ClaudeCli.ShortIdOf(file.SessionId)} · {Size(file.Bytes)} · {file.Written.ToLocalTime():yyyy-MM-dd HH:mm}";
        if (marked)
        {
            var age = DateTimeOffset.UtcNow - file.Written;
            var mark = TranscriptRetention.MarkOf(age, _retentionDays);
            if (mark == RetentionMark.Near)
            {
                var left = TranscriptRetention.DaysLeft(age, _retentionDays);
                text += $" · deleted in {left} day{(left == 1 ? string.Empty : "s")}";
            }
            else if (mark == RetentionMark.Due)
            {
                text += " · due for deletion at the next clean-up";
            }
        }
        return new CheckBox { Content = text, Tag = file.Path, ToolTip = file.Path, Margin = new Thickness(0, 0, 0, 3) };
    }

    private static IReadOnlyList<string> Checked(StackPanel list) =>
        list.Children.OfType<CheckBox>().Where(box => box.IsChecked == true).Select(box => (string)box.Tag).ToList();

    /// <summary>A size for a person: bytes, KB, MB or GB, to one decimal place from KB up.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };
}

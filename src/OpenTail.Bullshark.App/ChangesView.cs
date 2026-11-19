using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Git;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The viewed ticks the tab shows (features batch G1): the stamp each changed file has now (null when it cannot be read, so it cannot be ticked),
/// and the paths ticked now. The window keeps the ticks; the tab only draws them and reports a click.
/// </summary>
public sealed record ViewedTicks(IReadOnlyDictionary<string, string?> Stamps, IReadOnlySet<string> Viewed);

/// <summary>
/// The Changes tab (item 2 of the additions plan): what a session's folder has changed, as git reports it (read-only), the diff below
/// it in the terminal font, and the files the session's transcript says it wrote beside the list. Refresh is by button only.
/// The window fills it from a pool thread's result; this view draws and keeps the last report, so a theme change redraws it.
/// </summary>
public sealed class ChangesView : UserControl
{
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };

    /// <summary>The files another session of the same run also wrote (gems item 1). Shown above the lists, and hidden when there are none.</summary>
    private readonly StackPanel _sharedList = new() { Visibility = Visibility.Collapsed };
    private readonly Button _refresh = new() { Content = "Refresh", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _changedCaption = new() { FontSize = 11, Margin = new Thickness(0, 6, 0, 2) };

    /// <summary>The count of files the big-deletion check flags (features batch G2). Hidden when there are none.</summary>
    private readonly TextBlock _flagged = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

    /// <summary>"Mark all viewed" and "Clear" beside the Changed caption (features batch G1). Off while there is nothing to tick.</summary>
    private readonly Button _markAll = new() { Content = "Mark all viewed", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
    private readonly Button _clearViewed = new() { Content = "Clear", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };

    /// <summary>The viewed ticks on screen, or null when the report has none (a message, or a read in progress).</summary>
    private ViewedTicks? _ticks;

    private readonly StackPanel _changed = new();
    private readonly TextBlock _writtenCaption = new() { FontSize = 11, Margin = new Thickness(0, 6, 0, 2) };
    private readonly StackPanel _written = new();
    private readonly RichTextBox _diff = new()
    {
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = TerminalFont.Family,
        FontSize = TerminalFont.Size,
    };

    private RailPalette _palette = RailPalette.DarkPalette;

    /// <summary>The report on screen, kept so a theme change can draw it again.</summary>
    private GitChangesReport? _report;
    private IReadOnlyList<string>? _writtenFiles;
    private string? _writtenNote;
    private IReadOnlyList<string>? _sharedLines;

    /// <summary>The copy button for a resolve prompt. It shows only when the session's merge check found conflicts (gems item 2).</summary>
    private readonly Button _copyPrompt = new()
    {
        Content = "Copy resolve prompt",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(8, 0, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    private string? _resolvePrompt;

    /// <summary>The owner's note for the review prompt, and the button that copies the prompt (batch B1). Nothing is sent to the session.</summary>
    private readonly TextBox _reviewNote = new() { Padding = new Thickness(6, 3, 6, 3) };
    private readonly TextBlock _reviewLabel = new() { Text = "What should be fixed?", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _copyReview = new()
    {
        Content = UiIcon.Label(UiIcon.Copy, "Copy review prompt"),
        Padding = new Thickness(12, 4, 12, 4),
        Margin = new Thickness(8, 0, 0, 0),
        IsEnabled = false,
    };

    private string _sessionName = string.Empty;

    public ChangesView()
    {
        // The ghost style of the window's buttons (Styles/Controls.xaml). A missing style leaves the default button, not a failure.
        if (TryFindResource("GhostButton") is Style ghost)
        {
            _markAll.Style = ghost;
            _clearViewed.Style = ghost;
        }

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_refresh, Dock.Right);
        header.Children.Add(_refresh);
        DockPanel.SetDock(_copyPrompt, Dock.Right);
        header.Children.Add(_copyPrompt);
        header.Children.Add(_title);

        var review = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(_copyReview, Dock.Right);
        review.Children.Add(_copyReview);
        DockPanel.SetDock(_reviewLabel, Dock.Left);
        review.Children.Add(_reviewLabel);
        review.Children.Add(_reviewNote);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var changedHeader = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_clearViewed, Dock.Right);
        DockPanel.SetDock(_markAll, Dock.Right);
        changedHeader.Children.Add(_clearViewed);
        changedHeader.Children.Add(_markAll);
        changedHeader.Children.Add(_changedCaption);
        var changedPane = new StackPanel();
        changedPane.Children.Add(changedHeader);
        changedPane.Children.Add(new ScrollViewer { Content = _changed, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var writtenPane = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        writtenPane.Children.Add(_writtenCaption);
        writtenPane.Children.Add(new ScrollViewer { Content = _written, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(writtenPane, 1);
        lists.Children.Add(changedPane);
        lists.Children.Add(writtenPane);

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var top = new StackPanel();
        top.Children.Add(_note);
        top.Children.Add(_flagged);
        top.Children.Add(review);
        top.Children.Add(_sharedList);
        top.Children.Add(lists);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_diff);

        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _markAll.Click += (_, _) => ViewedAllChanged?.Invoke(true);
        _clearViewed.Click += (_, _) => ViewedAllChanged?.Invoke(false);
        _copyPrompt.Click += (_, _) => CopyPrompt();
        _copyReview.Click += (_, _) => CopyReviewPrompt();
        Content = root;
        _changedCaption.Text = "Changed";
        _writtenCaption.Text = "Files written by the session";
        ApplyTheme(_palette);
    }

    /// <summary>The Refresh button was pressed. The window reads the folder again.</summary>
    public event Action? RefreshRequested;

    /// <summary>A file's "viewed" box was clicked: its path, and whether it is now ticked. The window saves it and calls <see cref="SetViewed"/>.</summary>
    public event Action<string, bool>? ViewedChanged;

    /// <summary>"Mark all viewed" (true) or "Clear" (false) was pressed. The window saves it and calls <see cref="SetViewed"/>.</summary>
    public event Action<bool>? ViewedAllChanged;

    /// <summary>Colours the view from the rail's palette, and draws the report on screen again in those colours.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Background = RailPalette.Brush(palette.Background);
        Foreground = RailPalette.Brush(palette.Text);
        _title.Foreground = RailPalette.Brush(palette.Text);
        _note.Foreground = RailPalette.Brush(palette.Muted);
        _changedCaption.Foreground = RailPalette.Brush(palette.Muted);
        _writtenCaption.Foreground = RailPalette.Brush(palette.Muted);
        _reviewLabel.Foreground = RailPalette.Brush(palette.Muted);
        _flagged.Foreground = RailPalette.Brush(palette.Warn);
        _diff.Background = RailPalette.Brush(palette.Background);
        _diff.Foreground = RailPalette.Brush(palette.Text);
        if (_report is not null) Draw();
    }

    /// <summary>
    /// The resolve prompt for this session's conflicts, or null. The button is shown only with a prompt. Pressing it copies the text
    /// to the clipboard: the prompt is never sent to the session.
    /// </summary>
    public string? ResolvePrompt
    {
        get => _resolvePrompt;
        set
        {
            _resolvePrompt = value;
            _copyPrompt.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CopyPrompt()
    {
        if (_resolvePrompt is not { } text) return;
        try
        {
            Clipboard.SetText(text);
            _note.Text = "The resolve prompt is on the clipboard. Nothing was sent to the session.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // The clipboard is held by another program at this moment: the owner presses the button again.
            _note.Text = $"The clipboard is busy, so the prompt was not copied ({ex.Message}). Press the button again.";
        }
    }

    /// <summary>
    /// The name of the session the tab shows. A different session clears the note, so one session's note is never copied into another
    /// session's review prompt. A new read of the same session keeps the note.
    /// </summary>
    public string SessionName
    {
        get => _sessionName;
        set
        {
            if (string.Equals(_sessionName, value, StringComparison.Ordinal)) return;
            _sessionName = value;
            _reviewNote.Text = string.Empty;
        }
    }

    /// <summary>
    /// Copies the review prompt for the changed files and the owner's note to the clipboard. Nothing is sent to the session. The button is
    /// off when there are no changed files. A busy clipboard is said in the status line, and the owner presses the button again.
    /// </summary>
    private void CopyReviewPrompt()
    {
        var files = _report is null ? new List<string>() : _report.Files.Select(file => file.Path).ToList();
        var text = ReviewPrompt.Build(_sessionName, files, _reviewNote.Text);
        if (text is null) return;
        try
        {
            Clipboard.SetText(text);
            _note.Text = $"Review prompt copied ({files.Count} files). Paste it into the coder's tab.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _note.Text = $"The clipboard is busy, so the review prompt was not copied ({ex.Message}). Press the button again.";
        }
    }

    /// <summary>A read has started for this folder. The old lists stay out of sight until it finishes.</summary>
    public void ShowLoading(string folder)
    {
        _title.Text = "Changes in " + folder;
        _note.Text = "Reading the changes…";
        _refresh.IsEnabled = false;
        _copyReview.IsEnabled = false;
        ResolvePrompt = null;
        ClearTicks();
        _changed.Children.Clear();
        _written.Children.Clear();
        _sharedList.Children.Clear();
        _sharedList.Visibility = Visibility.Collapsed;
        _changedCaption.Text = "Changed";
        _writtenCaption.Text = "Files written by the session";
        _diff.Document = new FlowDocument();
    }

    /// <summary>Forgets the ticks and the flag line: the buttons are off until a report with files is shown.</summary>
    private void ClearTicks()
    {
        _ticks = null;
        _markAll.IsEnabled = false;
        _clearViewed.IsEnabled = false;
        _flagged.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows a read that finished. <paramref name="writtenFiles"/> is null when the transcript is not there (see <paramref name="writtenNote"/>).
    /// <paramref name="shared"/> lists the files another session of the run also wrote (one line each); none when null or empty.
    /// <paramref name="viewed"/> is the viewed ticks of the files (features batch G1); without it the rows have no "viewed" box.
    /// </summary>
    public void Show(GitChangesReport report, IReadOnlyList<string>? writtenFiles, string? writtenNote, IReadOnlyList<string>? shared = null,
        ViewedTicks? viewed = null)
    {
        _report = report;
        _writtenFiles = writtenFiles;
        _writtenNote = writtenNote;
        _sharedLines = shared;
        _ticks = viewed;
        _refresh.IsEnabled = true;
        Draw();
    }

    /// <summary>The ticks as the window now holds them, drawn again. Nothing is saved here; the window saves before it calls this.</summary>
    public void SetViewed(IReadOnlySet<string> viewed)
    {
        if (_ticks is null) return;
        _ticks = _ticks with { Viewed = viewed };
        if (_report is not null) Draw();
    }

    /// <summary>Shows a message in place of a report: no folder is known for the session, for example.</summary>
    public void ShowMessage(string title, string message)
    {
        _report = null;
        _title.Text = title;
        _note.Text = message;
        _refresh.IsEnabled = true;
        _copyReview.IsEnabled = false;
        ResolvePrompt = null;
        ClearTicks();
        _changed.Children.Clear();
        _written.Children.Clear();
        _sharedList.Children.Clear();
        _sharedList.Visibility = Visibility.Collapsed;
        _changedCaption.Text = string.Empty;
        _writtenCaption.Text = string.Empty;
        _diff.Document = new FlowDocument();
    }

    private void Draw()
    {
        var report = _report;
        if (report is null) return;
        var palette = _palette;

        _title.Text = "Changes in " + report.Folder;
        _note.Text = report.Message;
        _copyReview.IsEnabled = report.Files.Count > 0;

        _sharedList.Children.Clear();
        var shared = _sharedLines ?? [];
        if (shared.Count > 0)
        {
            _sharedList.Children.Add(new TextBlock
            {
                Text = "Also written by another session of this run:",
                FontWeight = FontWeights.SemiBold,
                Foreground = RailPalette.Brush(palette.Text),
                Margin = new Thickness(0, 4, 0, 2),
            });
            foreach (var line in shared)
                _sharedList.Children.Add(new TextBlock
                {
                    Text = line,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = RailPalette.Brush(palette.Muted),
                    Margin = new Thickness(8, 0, 0, 2),
                });
        }
        _sharedList.Visibility = shared.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The ticks count only the files that are in the list now (a tick of a file that was committed is not counted).
        var ticks = _ticks;
        var viewedCount = ticks is null ? 0 : report.Files.Count(file => ticks.Viewed.Contains(file.Path));
        _changedCaption.Text = report.Files.Count == 0 ? "Changed: none"
            : ticks is null ? $"Changed: {report.Files.Count}"
            : $"Changed: {report.Files.Count} · {viewedCount} of {report.Files.Count} viewed";
        _markAll.IsEnabled = ticks is not null && report.Files.Count > 0;
        _clearViewed.IsEnabled = viewedCount > 0;

        // The big-deletion check (features batch G2): a count line under the header, and a label on each flagged row.
        var flags = report.Files.Select(DeletionCheck.Flag).ToList();
        var flagCount = flags.Count(flag => flag is not null);
        _flagged.Visibility = flagCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        _flagged.Text = flagCount == 1 ? "1 file lost most of its lines: check it" : $"{flagCount} files lost most of their lines: check them";

        _changed.Children.Clear();
        for (var i = 0; i < report.Files.Count; i++) _changed.Children.Add(FileRow(report.Files[i], palette, flags[i], ticks));

        if (_writtenFiles is null)
        {
            _writtenCaption.Text = "Files written by the session";
            _written.Children.Clear();
            _written.Children.Add(Plain(_writtenNote ?? "No transcript for this session.", palette.Muted));
        }
        else
        {
            _writtenCaption.Text = _writtenFiles.Count == 0 ? "Files written: none" : $"Files written: {_writtenFiles.Count}";
            _written.Children.Clear();
            foreach (var path in _writtenFiles) _written.Children.Add(Plain(path, palette.Text));
        }

        _diff.Document = DiffDocument(report, palette);
    }

    /// <summary>
    /// One changed file: its line (the path, its status, its counts and, when it is flagged, the check label), and at the right its "viewed"
    /// box when the window gave ticks. A ticked file's path is muted. The box changes nothing in git; it reports the click to the window.
    /// </summary>
    private FrameworkElement FileRow(GitFileChange file, RailPalette palette, string? flag, ViewedTicks? ticks)
    {
        var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
        var viewed = ticks is not null && ticks.Viewed.Contains(file.Path);
        line.ToolTip = file.OldPath is null ? file.Path : $"{file.OldPath} → {file.Path}";
        line.Inlines.Add(new Run(file.Path) { Foreground = RailPalette.Brush(viewed ? palette.Muted : palette.Text) });
        line.Inlines.Add(new Run("  " + file.Label) { Foreground = RailPalette.Brush(palette.Muted), FontSize = 11 });
        if (file.Binary)
        {
            line.Inlines.Add(new Run("  binary") { Foreground = RailPalette.Brush(palette.Muted), FontSize = 11 });
        }
        else if (file.Added is { } added && file.Deleted is { } deleted)
        {
            line.Inlines.Add(new Run($"  +{added}") { Foreground = RailPalette.Brush(AddedHex(palette)), FontSize = 11 });
            line.Inlines.Add(new Run($" -{deleted}") { Foreground = RailPalette.Brush(RemovedHex(palette)), FontSize = 11 });
        }
        if (flag is not null)
        {
            // A label, not an alarm: the words say what to check, and the tooltip says the rule (features batch G2).
            line.Inlines.Add(new Run("  check: " + flag) { Foreground = RailPalette.Brush(palette.Warn), FontSize = 11 });
            line.ToolTip = $"{line.ToolTip}\n{DeletionRule}";
        }

        if (ticks is null) return line;
        var tick = new CheckBox
        {
            Content = "viewed",
            IsChecked = viewed,
            // A file with no stamp (it could not be read) cannot be ticked: its box is off, so the window never saves it.
            IsEnabled = ticks.Stamps.TryGetValue(file.Path, out var stamp) && stamp is not null,
            Foreground = RailPalette.Brush(palette.Muted),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Ticks this file as viewed. The tick clears when the file changes again.",
        };
        tick.Click += (_, _) => ViewedChanged?.Invoke(file.Path, tick.IsChecked == true);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tick, Dock.Right);
        row.Children.Add(tick);
        row.Children.Add(line);
        return row;
    }

    /// <summary>The rule the check label stands for, for the tooltip. It reads the thresholds of the check, so they are named in one place.</summary>
    private static string DeletionRule =>
        $"Check: a tracked file deleted, or at least {DeletionCheck.MinDeletedLines} lines removed and at least " +
        $"{DeletionCheck.MostlyDeletedRatio} times as many removed as added. A label to check, not an error.";

    /// <summary>The diff as one paragraph, coloured line by line. The terminal font, and the terminal's own colours (TerminalColours).</summary>
    private static FlowDocument DiffDocument(GitChangesReport report, RailPalette palette)
    {
        var document = new FlowDocument
        {
            FontFamily = TerminalFont.Family,
            FontSize = TerminalFont.Size,
            PagePadding = new Thickness(0),
            Background = RailPalette.Brush(palette.Background),
            Foreground = RailPalette.Brush(palette.Text),
        };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        if (report.Kind != GitChangesKind.Changes || report.Diff.Length == 0)
        {
            document.Blocks.Add(paragraph);
            return document;
        }

        var lines = report.Diff.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(line) { Foreground = RailPalette.Brush(LineHex(line, palette)) });
        }
        document.Blocks.Add(paragraph);
        return document;
    }

    private static string LineHex(string line, RailPalette palette)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal))
            return palette.Muted;
        if (line.StartsWith("@@", StringComparison.Ordinal)) return TerminalColours.Hex(palette.IsLight ? 4 : 12);
        if (line.StartsWith('+')) return AddedHex(palette);
        if (line.StartsWith('-')) return RemovedHex(palette);
        return palette.Text;
    }

    private static string AddedHex(RailPalette palette) => TerminalColours.Hex(palette.IsLight ? 2 : 10);

    private static string RemovedHex(RailPalette palette) => TerminalColours.Hex(palette.IsLight ? 1 : 9);

    private static TextBlock Plain(string text, string hex) => new()
    {
        Text = text,
        Foreground = RailPalette.Brush(hex),
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 1, 0, 1),
        ToolTip = text,
    };
}

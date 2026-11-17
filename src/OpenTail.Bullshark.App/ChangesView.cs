using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Git;

namespace OpenTail.Bullshark.App;

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

    public ChangesView()
    {
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_refresh, Dock.Right);
        header.Children.Add(_refresh);
        DockPanel.SetDock(_copyPrompt, Dock.Right);
        header.Children.Add(_copyPrompt);
        header.Children.Add(_title);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var changedPane = new StackPanel();
        changedPane.Children.Add(_changedCaption);
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
        top.Children.Add(_sharedList);
        top.Children.Add(lists);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_diff);

        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _copyPrompt.Click += (_, _) => CopyPrompt();
        Content = root;
        _changedCaption.Text = "Changed";
        _writtenCaption.Text = "Files written by the session";
        ApplyTheme(_palette);
    }

    /// <summary>The Refresh button was pressed. The window reads the folder again.</summary>
    public event Action? RefreshRequested;

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

    /// <summary>A read has started for this folder. The old lists stay out of sight until it finishes.</summary>
    public void ShowLoading(string folder)
    {
        _title.Text = "Changes in " + folder;
        _note.Text = "Reading the changes…";
        _refresh.IsEnabled = false;
        ResolvePrompt = null;
        _changed.Children.Clear();
        _written.Children.Clear();
        _sharedList.Children.Clear();
        _sharedList.Visibility = Visibility.Collapsed;
        _changedCaption.Text = "Changed";
        _writtenCaption.Text = "Files written by the session";
        _diff.Document = new FlowDocument();
    }

    /// <summary>
    /// Shows a read that finished. <paramref name="writtenFiles"/> is null when the transcript is not there (see <paramref name="writtenNote"/>).
    /// <paramref name="shared"/> lists the files another session of the run also wrote (one line each); none when null or empty.
    /// </summary>
    public void Show(GitChangesReport report, IReadOnlyList<string>? writtenFiles, string? writtenNote, IReadOnlyList<string>? shared = null)
    {
        _report = report;
        _writtenFiles = writtenFiles;
        _writtenNote = writtenNote;
        _sharedLines = shared;
        _refresh.IsEnabled = true;
        Draw();
    }

    /// <summary>Shows a message in place of a report: no folder is known for the session, for example.</summary>
    public void ShowMessage(string title, string message)
    {
        _report = null;
        _title.Text = title;
        _note.Text = message;
        _refresh.IsEnabled = true;
        ResolvePrompt = null;
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

        _changedCaption.Text = report.Files.Count == 0 ? "Changed: none" : $"Changed: {report.Files.Count}";
        _changed.Children.Clear();
        foreach (var file in report.Files) _changed.Children.Add(FileRow(file, palette));

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

    private static TextBlock FileRow(GitFileChange file, RailPalette palette)
    {
        var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
        line.ToolTip = file.OldPath is null ? file.Path : $"{file.OldPath} → {file.Path}";
        line.Inlines.Add(new Run(file.Path) { Foreground = RailPalette.Brush(palette.Text) });
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
        return line;
    }

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

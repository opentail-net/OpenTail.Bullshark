using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Read-only view of one past session, shown in the pane (S3). It loads page by page: the last page first, and earlier
/// pages on request. Resume asks first (the resume prompt shows what would be sent), then starts the session (D14, D-S11).
/// The transcript rendering is the History tab's, moved here unchanged. The colours follow the rail's theme (owner, 2026-11-16).
/// </summary>
public sealed class TranscriptView : UserControl
{
    private const int PageSize = 200;

    private readonly TextBlock _historyTitle = new();

    /// <summary>The title's row: the title and a small mark beside it that carries the session's id and file path on its tooltip.</summary>
    private readonly StackPanel _titleRow = new();

    /// <summary>The header card: the title, the stats, the tokens, the files and the lineage links, in one block with an edge.</summary>
    private readonly Border _card = new();
    private readonly TextBlock _historyInfo = new();
    private readonly TextBlock _usageCaption = new();
    private readonly Grid _usageGrid = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly Expander _files = new();

    /// <summary>The headline table: the labels on top, the values under them (headers on top, the stat strip).</summary>
    private readonly Grid _statsGrid = new() { Margin = new Thickness(0, 4, 0, 6) };

    /// <summary>Active time as shown: a dash until the turns are read, then their total.</summary>
    private string _activeText = "…";

    /// <summary>The active-time value in the table, to fill in when the turns are read.</summary>
    private TextBlock? _activeValue;

    /// <summary>"Continued from" and "Continued in" links, under the stats (session library, L5). Empty when there are none.</summary>
    private readonly WrapPanel _lineage = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly Button _earlier = new();
    private readonly StackPanel _transcript = new();
    private readonly ScrollViewer _transcriptScroll = new();
    private TranscriptPager? _pager;
    private SessionDigest? _digest;
    private readonly Button _resume = new();
    private readonly TextBlock _resumeHint = new();
    private int _firstShown;

    /// <summary>The zoom of the transcript text as a factor (1 at 100%), set by the window (zoom.md).</summary>
    private double _zoomScale = 1;

    /// <summary>The turn facts of the transcript shown, by the line of each turn's end (TA4). Empty until they are read.</summary>
    private readonly Dictionary<int, TurnFact> _turnFacts = new();

    /// <summary>The "turn finished" lines on screen, by line number, so their facts can be added when they are read.</summary>
    private readonly Dictionary<int, (TextBlock Block, TranscriptLine Line)> _turnLines = new();
    private RailPalette _palette = RailPalette.DarkPalette;

    /// <summary>
    /// "Where the effort went" (gems item 4): collapsed until opened. It is read once for each transcript, on a pool thread, and each
    /// item jumps to its first line with the same focus-line path the search uses.
    /// </summary>
    private readonly Expander _effort = new();

    /// <summary>"Export…" beside the title: writes the session as Markdown (additions item 7).</summary>
    private readonly Button _export = new() { Content = "Export…", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The transcript the effort panel was read for, so a jump to a line does not read it again.</summary>
    private string? _effortPath;

    /// <summary>The title the page was last shown with, so a jump to a line keeps the owner's name for the session.</summary>
    private string? _shownTitle;

    /// <summary>The line a search opened this view at, once it is shown. Null for the last page.</summary>
    private TextBlock? _focus;

    /// <summary>How many times <see cref="ShowAsync"/> has been called. A call that finishes after a newer one started leaves the view alone.</summary>
    private int _showCalls;

    /// <summary>True while a resume runs, so the Resume button stays off for any session shown meanwhile.</summary>
    private bool _resumeBusy;

    public TranscriptView()
    {
        Content = BuildContent();
        ApplyTheme(_palette);
    }

    /// <summary>Colours the view from the rail's palette. The lines already shown keep the colours they were drawn with until the next page.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Background = Brush(palette.Background);
        Foreground = Brush(palette.Text);
        _historyTitle.Foreground = Brush(palette.Text);
        _card.Background = Brush(palette.Panel);
        _card.BorderBrush = Brush(palette.Border);
        _historyInfo.Foreground = Brush(palette.Muted);
        _usageCaption.Foreground = Brush(palette.Muted);
        _resumeHint.Foreground = Brush(palette.Muted);
        // The effort header takes the text colour: the default one is unreadable on the dark panel ("Files written" is coloured when it fills).
        _effort.Foreground = Brush(palette.Text);
        if (_digest is not null) ShowStats(_digest);
    }

    /// <summary>
    /// Shows a session's headline numbers at once, and a page of its transcript: the last page, or with a focus line (a search result)
    /// the page around that line, with the line marked and brought into view. Nothing else is loaded.
    /// </summary>
    public async Task ShowAsync(SessionDigest digest, int? focusLine = null, string? title = null)
    {
        var call = ++_showCalls;
        _digest = digest;
        _resume.IsEnabled = !_resumeBusy;
        _resumeHint.Text = ResumeHint(digest);
        // The title is the owner's name when there is one (session library, D-L1); the transcript's own title otherwise.
        SetTitle(title ?? digest.Title);
        // The id and the file path are for reference, so they sit on the tooltip, not in the text.
        var details = $"Session {digest.SessionId}\n{digest.FilePath}";
        _historyTitle.ToolTip = details;
        _historyInfo.ToolTip = details;
        _historyInfo.Visibility = Visibility.Visible;
        _activeText = "…";
        _files.IsExpanded = false;
        _shownTitle = title;
        // The effort is read once per transcript. A jump to a line calls this again for the same file, which keeps the panel as it is.
        if (_effortPath != digest.FilePath)
        {
            _effortPath = digest.FilePath;
            ShowEffortPending();
            _ = LoadEffortAsync(digest.FilePath);
        }
        ShowStats(digest);

        var pager = await Task.Run(() => TranscriptPager.Open(digest.FilePath));
        // A newer call started while this file was read (the rail and the search both call this). The newer one owns the view now.
        if (call != _showCalls) return;
        _pager = pager;
        _firstShown = focusLine is { } wanted
            ? Math.Max(0, Math.Clamp(wanted - 1, 0, Math.Max(0, pager.LineCount - 1)) - PageSize / 2)
            : pager.LastPageStart(PageSize);
        _transcript.Children.Clear();
        _focus = null;
        _turnFacts.Clear();
        _turnLines.Clear();
        _ = LoadTurnFactsAsync(digest.FilePath);
        foreach (var line in pager.ReadPage(_firstShown, PageSize))
        {
            var block = Render(line);
            if (focusLine == line.Number)
            {
                block.Background = Brush(_palette.IsLight ? "#CFE0F5" : "#2D3E50");
                _focus = block;
            }
            _transcript.Children.Add(block);
        }
        _earlier.IsEnabled = _firstShown > 0;
        if (_focus is null) _transcriptScroll.ScrollToBottom();
        else _ = Dispatcher.BeginInvoke(new Action(() => _focus?.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>True when the view shows this session: no later <see cref="ShowAsync"/> has replaced it. Callers draw its lineage only then.</summary>
    public bool IsShowing(SessionDigest digest) => ReferenceEquals(_digest, digest);

    /// <summary>Sets the zoom of the transcript text. The lines on screen are resized now; the lines drawn later use the same size.</summary>
    public void ApplyZoom(double scale)
    {
        _zoomScale = scale;
        foreach (var block in _transcript.Children.OfType<TextBlock>()) block.FontSize = TerminalFont.Size * scale;
    }

    /// <summary>
    /// The title, with the note that this is a past session on the same line: smaller and muted, so the title is still the first thing read.
    /// </summary>
    private void SetTitle(string title)
    {
        _historyTitle.Inlines.Clear();
        _historyTitle.Inlines.Add(new Run(title));
        _historyTitle.Inlines.Add(new Run("   ·   past session, read-only, local")
        {
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = Brush(_palette.Muted),
        });
    }

    private UIElement BuildContent()
    {
        var right = new DockPanel { Margin = new Thickness(10) };

        _historyTitle.FontSize = 17;
        _historyTitle.FontWeight = FontWeights.SemiBold;
        _historyTitle.Text = "Choose a session in the list on the left.";
        _historyTitle.TextWrapping = TextWrapping.Wrap;
        _historyTitle.VerticalAlignment = VerticalAlignment.Center;

        // The title, with a small mark beside it. The mark and the title both show the session's id and file path on hover.
        _historyInfo.Text = "ⓘ";
        _historyInfo.FontSize = 14;
        _historyInfo.Cursor = Cursors.Help;
        _historyInfo.Margin = new Thickness(8, 0, 0, 0);
        _historyInfo.VerticalAlignment = VerticalAlignment.Center;
        _historyInfo.Visibility = Visibility.Collapsed;
        _titleRow.Orientation = Orientation.Horizontal;
        _titleRow.Children.Add(_historyTitle);
        _titleRow.Children.Add(_historyInfo);
        _export.Click += (_, _) => _ = ExportAsync();
        _export.ToolTip = "Writes this session as a Markdown file: the turns, the tool calls as one-line summaries, and Claude's recap.";
        _titleRow.Children.Add(_export);

        // The header is one card: the title (with the past-session note on its line), the stats, the tokens, the files and the links.
        var header = new StackPanel();
        header.Children.Add(_titleRow);
        header.Children.Add(_statsGrid);
        _usageCaption.FontSize = 11;
        _usageCaption.Margin = new Thickness(0, 0, 0, 2);
        header.Children.Add(_usageCaption);
        header.Children.Add(_usageGrid);
        _files.FontSize = 12;
        _files.Margin = new Thickness(0, 0, 0, 6);
        header.Children.Add(_files);
        _effort.Header = "Where the effort went";
        _effort.FontSize = 12;
        _effort.Margin = new Thickness(0, 0, 0, 6);
        _effort.ToolTip = "Tokens are input, output and cache writes. Cache reads are left out: they re-read context that was already paid for.";
        header.Children.Add(_effort);
        header.Children.Add(_lineage);
        _card.Child = header;
        _card.CornerRadius = new CornerRadius(6);
        _card.BorderThickness = new Thickness(1);
        _card.Padding = new Thickness(12, 8, 12, 6);
        _card.Margin = new Thickness(0, 8, 0, 4);
        DockPanel.SetDock(_card, Dock.Top);
        right.Children.Add(_card);

        _earlier.Content = "Load earlier";
        _earlier.Padding = new Thickness(10, 2, 10, 2);
        _earlier.HorizontalAlignment = HorizontalAlignment.Left;
        _earlier.Margin = new Thickness(0, 0, 0, 6);
        _earlier.IsEnabled = false;
        _earlier.Click += (_, _) => LoadEarlier();
        DockPanel.SetDock(_earlier, Dock.Top);
        right.Children.Add(_earlier);

        // The one way to resume sits at the bottom, centred under the transcript and large enough not to be missed. The hint under it
        // says what is recommended; the choices open from the button.
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _resume.Content = "Resume session…";
        _resume.IsEnabled = false;
        _resume.MinWidth = 240;
        _resume.Padding = new Thickness(36, 10, 36, 10);
        _resume.FontSize = 15;
        _resume.FontWeight = FontWeights.SemiBold;
        _resume.HorizontalAlignment = HorizontalAlignment.Center;
        _resume.ToolTip = "Opens the ways to continue this session. Nothing runs until you confirm there.";
        _resume.Click += (_, _) => RaiseResume();
        bottom.Children.Add(_resume);
        _resumeHint.FontSize = 12;
        _resumeHint.TextWrapping = TextWrapping.Wrap;
        _resumeHint.TextAlignment = TextAlignment.Center;
        _resumeHint.HorizontalAlignment = HorizontalAlignment.Center;
        _resumeHint.MaxWidth = 560;
        _resumeHint.Margin = new Thickness(0, 6, 0, 0);
        bottom.Children.Add(_resumeHint);
        DockPanel.SetDock(bottom, Dock.Bottom);
        right.Children.Add(bottom);

        _transcript.Margin = new Thickness(0, 0, 12, 0);
        _transcriptScroll.Content = _transcript;
        _transcriptScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        right.Children.Add(_transcriptScroll);

        return right;
    }

    /// <summary>Raised when the owner asks to resume the session shown. The resume prompt decides how, and shows the cost first.</summary>
    public event Action<SessionDigest>? ResumeRequested;

    /// <summary>Raised with a session id when the owner clicks a "Continued from" or "Continued in" link.</summary>
    public event Action<string>? OpenSessionRequested;

    /// <summary>
    /// Shows the links to the session this one continues, and to the sessions that continue it (D-L8). A link is text that
    /// opens that session's transcript; the rail is not changed by it.
    /// </summary>
    public void ShowLineage(string? fromId, string? fromTitle, IReadOnlyList<(string Id, string Title)> later)
    {
        _lineage.Children.Clear();
        if (fromId is { Length: > 0 }) _lineage.Children.Add(LineageLink("Continued from ", fromTitle ?? fromId, fromId));
        foreach (var (id, title) in later) _lineage.Children.Add(LineageLink("Continued in ", title, id));
    }

    private UIElement LineageLink(string lead, string title, string sessionId)
    {
        var link = new TextBlock
        {
            Text = title,
            Foreground = Brush(_palette.Text),
            TextDecorations = TextDecorations.Underline,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 14, 0),
            FontSize = 12,
            ToolTip = $"Open session {sessionId[..Math.Min(8, sessionId.Length)]}",
        };
        link.MouseLeftButtonUp += (_, _) => OpenSessionRequested?.Invoke(sessionId);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = lead, Foreground = Brush(_palette.Muted), FontSize = 12 });
        row.Children.Add(link);
        return row;
    }

    /// <summary>Turns the Resume button off while a resume runs. The window sets it when a resume starts and when it ends.</summary>
    public void SetResumeBusy(bool busy)
    {
        _resumeBusy = busy;
        _resume.IsEnabled = !busy && _digest is not null;
    }

    private void RaiseResume()
    {
        if (_digest is not null) ResumeRequested?.Invoke(_digest);
    }

    /// <summary>What is recommended for the session, from its context size. The owner still chooses.</summary>
    private static string ResumeHint(SessionDigest digest) =>
        ResumeAdvice.FullIsRecommended(digest.ContextTokensAtLastTurn)
            ? $"Recommended: in full · context {digest.ContextTokensAtLastTurn:N0} tokens"
            : $"Recommended: from a summary · context {digest.ContextTokensAtLastTurn:N0} tokens";

    private void LoadEarlier()
    {
        if (_pager is null || _firstShown <= 0) return;

        int start = Math.Max(0, _firstShown - PageSize);
        var earlier = _pager.ReadPage(start, _firstShown - start);
        for (int i = 0; i < earlier.Count; i++) _transcript.Children.Insert(i, Render(earlier[i]));
        _firstShown = start;
        _earlier.IsEnabled = _firstShown > 0;
    }

    /// <summary>
    /// The headline numbers, the session's tokens and its files. Active time is the sum of the turns' durations, filled in when the turns
    /// are read. Compactions appear only when there are some.
    /// </summary>
    private void ShowStats(SessionDigest d)
    {
        var cells = new List<(string Label, string Value)>
        {
            ("Context at last turn", $"{d.ContextTokensAtLastTurn:N0} tokens"),
            ("Active time", _activeText),
            ("Messages", $"{d.AssistantMessages:N0}"),
            ("Files changed", $"{d.FilesWritten.Count:N0}"),
            ("Failed calls", $"{d.FailedToolCalls:N0}"),
        };
        if (d.Compactions > 0) cells.Add(("Compactions", $"{d.Compactions:N0}"));
        var values = FillTable(_statsGrid, cells);
        _activeValue = values[cells.FindIndex(c => c.Label == "Active time")];

        _usageCaption.Text = "This session's tokens";
        FillTable(_usageGrid, new List<(string Label, string Value)>
        {
            ("Input", $"{d.Usage.Input:N0}"),
            ("Output", $"{d.Usage.Output:N0}"),
            ("Cache read", $"{d.Usage.CacheRead:N0}"),
            ("Cache write", $"{d.Usage.CacheCreation:N0}"),
        });

        ShowFiles(d.FilesWritten);
    }

    /// <summary>Fills a table with the labels on top and the values under them. Returns the value cells, in order.</summary>
    private List<TextBlock> FillTable(Grid grid, List<(string Label, string Value)> cells)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var values = new List<TextBlock>();
        for (var i = 0; i < cells.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = cells[i].Label,
                Foreground = Brush(_palette.Muted),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 14, 2),
            };
            var value = new TextBlock
            {
                Text = cells[i].Value,
                Foreground = Brush(_palette.Text),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetRow(label, 0);
            Grid.SetColumn(label, i);
            Grid.SetRow(value, 1);
            Grid.SetColumn(value, i);
            grid.Children.Add(label);
            grid.Children.Add(value);
            values.Add(value);
        }
        return values;
    }

    /// <summary>
    /// The files written, grouped by folder: each folder's path once, and its files under it, one line each. The list shows about six
    /// lines and scrolls for the rest. The fold starts closed for a new session and keeps its state when the colours change.
    /// </summary>
    private void ShowFiles(IReadOnlyList<string> files)
    {
        var wasOpen = _files.IsExpanded;
        _files.Visibility = files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _files.Header = $"Files written ({files.Count:N0})";
        _files.Foreground = Brush(_palette.Text);
        var lines = new StackPanel();
        // GroupBy keeps the folders in the order their first file was written.
        foreach (var group in files.GroupBy(FolderOf))
        {
            lines.Children.Add(new TextBlock
            {
                Text = group.Key,
                ToolTip = group.Key,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11,
                Foreground = Brush(_palette.Muted),
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 4, 0, 1),
            });
            foreach (var file in group)
            {
                lines.Children.Add(new TextBlock
                {
                    Text = "↪️ " + Path.GetFileName(file),
                    ToolTip = file,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Brush(_palette.Text),
                    FontFamily = new FontFamily("Consolas"),
                    Margin = new Thickness(10, 0, 0, 1),
                });
            }
        }
        // About six file lines fit in the height; the rest scroll.
        _files.Content = new ScrollViewer
        {
            MaxHeight = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = lines,
        };
        _files.IsExpanded = wasOpen;
    }

    private static string FolderOf(string file)
    {
        var folder = Path.GetDirectoryName(file);
        return string.IsNullOrEmpty(folder) ? "(no folder)" : folder;
    }

    /// <summary>The total of the turns' durations as "2h 14m", "3m 20s" or "45s"; a dash when no turn has a duration.</summary>
    private static string ActiveTimeText(IReadOnlyList<TurnFact> facts)
    {
        var seconds = facts.Where(f => f.DurationSeconds is not null).Select(f => f.DurationSeconds!.Value).ToList();
        if (seconds.Count == 0) return "—";
        var total = TimeSpan.FromSeconds(seconds.Sum());
        if (total.TotalHours >= 1) return $"{(int)total.TotalHours}h {total.Minutes:00}m";
        if (total.TotalMinutes >= 1) return $"{total.Minutes}m {total.Seconds:00}s";
        return $"{total.Seconds}s";
    }


    private TextBlock Render(TranscriptLine line)
    {
        // The colours are darker on the light theme, so they read on it; the dark theme keeps the colours it always had.
        // The colours are the terminal's own palette (Campbell, as in TerminalColours): the bright shades on the dark theme, the dark
        // shades on the light one, so a past session reads like a live one.
        var (color, _) = line.Kind switch
        {
            "you" => (_palette.IsLight ? "#0037DA" : "#61D6D6", false),
            "claude" => (_palette.Text, false),
            "tool" => (_palette.IsLight ? "#881798" : "#3B78FF", true),
            "result" when line.Failed => (_palette.IsLight ? "#C50F1F" : "#E74856", false),
            "result" => (_palette.Muted, true),
            "compact" => (_palette.IsLight ? "#C19C00" : "#F9F1A5", true),
            "turn" => (_palette.IsLight ? "#13A10E" : "#16C60C", true),
            _ => (_palette.Text, false),
        };
        var block = new TextBlock
        {
            Text = LineText(line),
            Foreground = Brush(color),
            // Every line of the session is in the terminal's font and size (owner, 2026-11-16), so a past session reads like
            // a live one. The colour still tells the kinds apart.
            FontFamily = TerminalFont.Family,
            FontSize = TerminalFont.Size * _zoomScale,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
        };
        if (line.Kind == "compact") block.FontStyle = FontStyles.Italic;
        if (line.Kind == "turn") _turnLines[line.Number] = (block, line);
        return block;
    }

    /// <summary>A line's text: its number and text, and for a turn's end the facts of the turn once they are read (D-A2).</summary>
    private string LineText(TranscriptLine line) =>
        $"{line.Number,5}  {line.Text}" +
        (line.Kind == "turn" && _turnFacts.TryGetValue(line.Number, out var fact) ? FactsSuffix(fact) : string.Empty);

    /// <summary>" · 4 tools, 1 failed · wrote 2 files". A zero count is left out; with no counts at all, nothing is added.</summary>
    private static string FactsSuffix(TurnFact fact)
    {
        var calls = new List<string>();
        if (fact.ToolCalls > 0) calls.Add($"{fact.ToolCalls} tool{(fact.ToolCalls == 1 ? string.Empty : "s")}");
        if (fact.FailedCalls > 0) calls.Add($"{fact.FailedCalls} failed");
        var groups = new List<string>();
        if (calls.Count > 0) groups.Add(string.Join(", ", calls));
        if (fact.FilesWritten.Count > 0) groups.Add($"wrote {fact.FilesWritten.Count} file{(fact.FilesWritten.Count == 1 ? string.Empty : "s")}");
        return groups.Count == 0 ? string.Empty : " · " + string.Join(" · ", groups);
    }

    /// <summary>
    /// Reads the turns of the transcript off the UI thread, then adds their facts to the turn lines on screen (TA4). A read that
    /// fails is logged, and the lines keep their text. Facts for a session that is no longer shown are dropped.
    /// </summary>
    private async Task LoadTurnFactsAsync(string path)
    {
        IReadOnlyList<TurnFact> facts;
        try
        {
            facts = await Task.Run(() => TurnFacts.ReadFile(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"turn facts not read: {ex.Message}");
            return;
        }
        if (_digest?.FilePath != path) return;

        foreach (var fact in facts) _turnFacts[fact.EndLine] = fact;
        _activeText = ActiveTimeText(facts);
        if (_activeValue is not null) _activeValue.Text = _activeText;
        foreach (var (block, line) in _turnLines.Values) block.Text = LineText(line);
    }

    /// <summary>
    /// Writes the shown session as Markdown to a file the owner picks. The transcript is read on a pool thread. A file that cannot be
    /// written is reported in a message box; nothing else changes.
    /// </summary>
    private async Task ExportAsync()
    {
        if (_digest is not { } digest) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export session",
            FileName = digest.SessionId[..Math.Min(8, digest.SessionId.Length)] + ".md",
            Filter = "Markdown (*.md)|*.md",
            DefaultExt = ".md",
        };
        if (dialog.ShowDialog() != true) return;

        var target = dialog.FileName;
        var title = _shownTitle ?? digest.Title;
        try
        {
            var markdown = await Task.Run(() => OpenTail.Bullshark.Core.Export.TranscriptExport.Markdown(
                TranscriptFile.ReadLines(digest.FilePath), title, digest.FirstActivity, digest.ProjectFolder, digest.LastModel, digest.LastRecap));
            await File.WriteAllTextAsync(target, markdown, new System.Text.UTF8Encoding(false));
            AppLog.Write($"exported {digest.SessionId} to {target}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"export not written: {ex.Message}");
            MessageBox.Show($"The export was not written: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowEffortPending() =>
        _effort.Content = new TextBlock { Text = "Reading the transcript…", Foreground = Brush(_palette.Muted), Margin = new Thickness(0, 4, 0, 4) };

    /// <summary>
    /// Reads the effort of the transcript on a pool thread, then draws it. A read that fails is logged and shown as a line in the panel.
    /// A result for a transcript that is no longer shown is dropped.
    /// </summary>
    private async Task LoadEffortAsync(string path)
    {
        EffortReport report;
        try
        {
            report = await Task.Run(() => EffortReport.ReadFile(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"effort not read: {ex.Message}");
            if (_effortPath == path)
                _effort.Content = new TextBlock { Text = $"The transcript cannot be read: {ex.Message}", TextWrapping = TextWrapping.Wrap, Foreground = Brush(_palette.Muted) };
            return;
        }
        if (_effortPath == path) ShowEffort(report);
    }

    /// <summary>The panel: failed calls by tool, files read again, commands run again, and the turns that gave no output.</summary>
    private void ShowEffort(EffortReport report)
    {
        var panel = new StackPanel();
        panel.Children.Add(EffortCaption($"Failed tool calls: {report.FailedCalls:N0}"));
        foreach (var item in report.FailedByTool)
            panel.Children.Add(EffortJump($"{item.Label}: {item.Count:N0} failed", item.Line, null));

        panel.Children.Add(EffortCaption($"Files read more than {EffortReport.RereadThreshold} times: {report.RereadFiles.Count:N0}"));
        foreach (var item in report.RereadFiles)
            panel.Children.Add(EffortJump($"{Path.GetFileName(item.Label)}: read {item.Count:N0} times", item.Line, item.Label));

        panel.Children.Add(EffortCaption($"Commands run more than {EffortReport.RepeatThreshold} times: {report.RepeatedCommands.Count:N0}"));
        foreach (var item in report.RepeatedCommands)
            panel.Children.Add(EffortJump($"{item.Label}: run {item.Count:N0} times", item.Line, null));

        panel.Children.Add(EffortCaption(
            $"Turns with no file written and no text answer: {report.TurnsWithoutOutput.Count:N0} of {report.Turns:N0}, " +
            $"{Tokens(report.TokensWithoutOutput)} tokens"));
        foreach (var turn in report.TurnsWithoutOutput)
            panel.Children.Add(EffortJump(
                $"{(turn.Prompt.Length == 0 ? "(before the first prompt)" : turn.Prompt)} · {Tokens(turn.Tokens)} tokens",
                turn.StartLine, null));

        _effort.Content = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
    }

    private TextBlock EffortCaption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(_palette.Text),
        Margin = new Thickness(0, 6, 0, 2),
    };

    /// <summary>One item of the panel. A click shows the transcript at its first line.</summary>
    private TextBlock EffortJump(string text, int line, string? tooltip)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = tooltip ?? $"Show line {line}",
            Foreground = Brush(_palette.Muted),
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 1, 0, 1),
        };
        block.MouseLeftButtonUp += (_, _) =>
        {
            if (_digest is { } digest) _ = ShowAsync(digest, line, _shownTitle);
        };
        return block;
    }

    /// <summary>Tokens for display: input, output and cache writes (cache reads are left out, see the panel's tooltip).</summary>
    private static string Tokens(TokenUsage usage) => (usage.Input + usage.Output + usage.CacheCreation).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

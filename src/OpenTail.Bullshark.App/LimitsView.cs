using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Setup;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Limits page (A4): a table of the limit hits (local time, the limit, its reset, the tokens used in the five hours before it), a line per day
/// with the hits of each type, the usage per local day and the top projects of the same transcripts, and the line that says what is counted. It only
/// draws what the window gives it; the report was read on a pool thread.
/// </summary>
public sealed class LimitsView : UserControl
{
    private const string Footer = "Counts only sessions on this computer. Other machines and claude.ai chat use the same limit and are not shown.";

    private const string UsageCaption = "Tokens as the transcripts report them, counted once per message. New = input + output + cache written. " +
                                        "Cache read is shown apart. Not money, and not the same as what counts toward a limit.";

    /// <summary>The width of the busiest day's bar, in device-independent pixels.</summary>
    private const double BarWidth = 160;

    /// <summary>The bars are drawn at this opacity, so the figures next to them read first.</summary>
    private const double BarOpacity = 0.45;

    private readonly TextBlock _note = new()
    {
        Text = "Each row is a usage limit that Claude Code reported as reached. A limit written again while a session retries is one row.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 4),
    };

    private readonly TextBlock _caption = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), FontWeight = FontWeights.SemiBold };

    private readonly Grid _table = new();
    private readonly StackPanel _tableBlock = new();
    private readonly StackPanel _days = new();
    private readonly StackPanel _daysBlock = new();

    private readonly TextBlock _usageCaption = new() { Text = UsageCaption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
    private readonly Grid _usageTable = new();
    private readonly Grid _projectTable = new();
    private readonly StackPanel _usageBlock = new();

    /// <summary>The line under the usage section that says how many Codex rollouts this build could not read (R8 part 2). Hidden when there are none.</summary>
    private readonly TextBlock _codexSkipped = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>The bars of the usage-per-day table, kept so that a theme change can recolour them.</summary>
    private readonly List<Rectangle> _bars = [];

    /// <summary>The names of the folders, by key, as the window gives them (FolderDisplay). A key with no name shows as the key.</summary>
    private static readonly IReadOnlyDictionary<string, FolderLabel> NoLabels = new Dictionary<string, FolderLabel>();

    private IReadOnlyDictionary<string, FolderLabel> _labels = NoLabels;

    private SolidColorBrush _barFill = RailPalette.Brush(RailPalette.DarkPalette.Text);

    private readonly TextBlock _footer = new()
    {
        Text = Footer,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
    };

    public LimitsView()
    {
        var refresh = new Button { Content = UiIcon.Label(UiIcon.Refresh, "Refresh"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();

        _tableBlock.Children.Add(new TextBlock { Text = "Hits, newest first", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        _tableBlock.Children.Add(_table);

        _daysBlock.Children.Add(new TextBlock { Text = "Limit hits per day (local date)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        _daysBlock.Children.Add(_days);

        _usageBlock.Children.Add(new TextBlock { Text = "Usage per day (local date)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        _usageBlock.Children.Add(_usageCaption);
        _usageBlock.Children.Add(_usageTable);
        _usageBlock.Children.Add(new TextBlock
        {
            Text = $"Top projects, last {LimitHistory.DefaultDays} days",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4),
        });
        _usageBlock.Children.Add(_projectTable);
        _usageBlock.Children.Add(_codexSkipped);

        var root = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        // No page title here: the Insights tab's sub-tab row names the page (2026-11-18).
        root.Children.Add(_note);
        root.Children.Add(_caption);
        root.Children.Add(refresh);
        root.Children.Add(_tableBlock);
        root.Children.Add(_daysBlock);
        root.Children.Add(_usageBlock);
        root.Children.Add(_footer);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>The Refresh button was pressed. The window reads the transcripts again.</summary>
    public event Action? RefreshRequested;

    /// <summary>Takes the rail's palette, as the other views do: the text and the muted lines follow the theme, and so do the bars.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        Foreground = RailPalette.Brush(palette.Text);
        var muted = RailPalette.Brush(palette.Muted);
        _note.Foreground = muted;
        _footer.Foreground = muted;
        _usageCaption.Foreground = muted;
        _codexSkipped.Foreground = muted;
        _barFill = RailPalette.Brush(palette.Text);
        foreach (var bar in _bars) bar.Fill = _barFill;
    }

    /// <summary>
    /// Shows the hits (newest first) with their days, then the usage per day and the top projects. With no hit, the empty-state line is shown in place
    /// of the hits; the usage tables are shown either way.
    /// </summary>
    public void Show(LimitsReport report, IReadOnlyDictionary<string, FolderLabel>? labels = null)
    {
        _labels = labels ?? NoLabels;
        var window = LimitHistory.DefaultDays;
        if (report.Hits.Count == 0)
        {
            _caption.Text = $"No limit hits in the last {window} days.";
            _tableBlock.Visibility = Visibility.Collapsed;
            _daysBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            _caption.Text = $"{report.Hits.Count} limit {(report.Hits.Count == 1 ? "hit" : "hits")} in the last {window} days.";
            _tableBlock.Visibility = Visibility.Visible;
            _daysBlock.Visibility = Visibility.Visible;
            DrawTable(report.Hits);
            _days.Children.Clear();
            foreach (var day in report.Days) _days.Children.Add(DayLine(day));
        }

        _usageBlock.Visibility = Visibility.Visible;
        DrawDays(report.Usage);
        DrawProjects(report.Usage);
        var rollouts = report.CodexSkipped == 1 ? "rollout" : "rollouts";
        _codexSkipped.Text = $"{report.CodexSkipped.ToString(CultureInfo.CurrentCulture)} Codex {rollouts} not read: this build cannot decode compressed files.";
        _codexSkipped.Visibility = report.CodexSkipped > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows a line in place of the page's content, for a read that failed.</summary>
    public void ShowProblem(string text)
    {
        _caption.Text = text;
        _tableBlock.Visibility = Visibility.Collapsed;
        _daysBlock.Visibility = Visibility.Collapsed;
        _usageBlock.Visibility = Visibility.Collapsed;
    }

    /// <summary>The hits as a table: the header, then one row per hit. The tokens are the four kinds together, with the breakdown in the tooltip.</summary>
    private void DrawTable(IReadOnlyList<LimitHit> hits)
    {
        Reset(_table, 4);

        AddRow(_table, 0, bold: true, ["When", "Limit", "Resets", "Tokens in the 5 h before"], lastTip: null);
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var tokens = hit.TokensBefore;
            var total = tokens.Input + tokens.Output + tokens.CacheCreation + tokens.CacheRead;
            AddRow(_table, i + 1, bold: false, [LocalTime(hit.Time), LimitStatus.LabelOf(hit.Type), LocalTime(hit.ResetsAt), Count(total)], lastTip: Breakdown(tokens));
        }
    }

    /// <summary>
    /// The usage per local day, newest first: the date, New and Cache read, a bar for New scaled to the busiest day, and that day's top project.
    /// Every day of the window is a row, zeros included.
    /// </summary>
    private void DrawDays(UsageReport usage)
    {
        Reset(_usageTable, 5);
        _bars.Clear();
        AddRow(_usageTable, 0, [Cell("Day", bold: true), Cell("New", bold: true, right: true),
            Cell("Cache read", bold: true, right: true), Cell("", bold: true), Cell("Top project", bold: true)]);

        var busiest = usage.Days.Count == 0 ? 0 : usage.Days.Max(day => day.Total.NewTokens());
        for (var i = 0; i < usage.Days.Count; i++)
        {
            var day = usage.Days[i];
            var newTokens = day.Total.NewTokens();
            AddRow(_usageTable, i + 1,
            [
                Cell(day.Day.ToString("yyyy-MM-dd ddd", CultureInfo.CurrentCulture)),
                Cell(Count(newTokens), right: true, tip: Breakdown(day.Total)),
                Cell(Count(day.Total.CacheRead), right: true),
                Bar(newTokens, busiest),
                day.TopProject is null ? Cell("-") : Cell(NameOf(day.TopProject), tip: TipOf(day.TopProject)),
            ]);
        }
    }

    /// <summary>
    /// The top projects of the window: the name, New, Cache read, the share of all New tokens, and the days with usage. The last row is the Other row
    /// when <see cref="UsageReport.OtherProjects"/> is above 0; its name says how many projects it holds.
    /// </summary>
    private void DrawProjects(UsageReport usage)
    {
        Reset(_projectTable, 5);
        AddRow(_projectTable, 0, [Cell("Project", bold: true), Cell("New", bold: true, right: true),
            Cell("Cache read", bold: true, right: true), Cell("Share", bold: true, right: true), Cell("Active days", bold: true, right: true)]);

        if (usage.Projects.Count == 0)
        {
            var none = Cell($"No usage in the last {LimitHistory.DefaultDays} days.");
            Grid.SetColumnSpan(none, 5);
            Place(_projectTable, 1, 0, none);
            _projectTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return;
        }

        var totalNew = usage.Total.NewTokens();
        for (var i = 0; i < usage.Projects.Count; i++)
        {
            var project = usage.Projects[i];
            var isOther = usage.OtherProjects > 0 && i == usage.Projects.Count - 1;
            var newTokens = project.Total.NewTokens();
            var share = totalNew == 0 ? 0.0 : 100.0 * newTokens / totalNew;
            AddRow(_projectTable, i + 1,
            [
                isOther
                    ? Cell($"{UsageReport.OtherName} ({ProjectCount(usage.OtherProjects)})")
                    : Cell(NameOf(project.Project), tip: TipOf(project.Project)),
                Cell(Count(newTokens), right: true, tip: Breakdown(project.Total)),
                Cell(Count(project.Total.CacheRead), right: true),
                Cell($"{share.ToString("0.0", CultureInfo.CurrentCulture)}%", right: true),
                Cell(project.ActiveDays.ToString(CultureInfo.CurrentCulture), right: true),
            ]);
        }
    }

    /// <summary>A bar for a day's New tokens, scaled so that the busiest day is <see cref="BarWidth"/> wide. Plain, at reduced opacity.</summary>
    private Rectangle Bar(long value, long busiest)
    {
        var bar = new Rectangle
        {
            Height = 10,
            Width = busiest == 0 ? 0 : Math.Round(BarWidth * value / busiest),
            Fill = _barFill,
            Opacity = BarOpacity,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 18, 2),
        };
        _bars.Add(bar);
        return bar;
    }

    /// <summary>
    /// Clears a table and sets its columns, all sized to their content. The table is then filled with <see cref="AddRow"/>.
    /// </summary>
    private static void Reset(Grid table, int columns)
    {
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();
        for (var column = 0; column < columns; column++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

    /// <summary>
    /// One table row, one cell per element. Each element is placed in its column. The bars of the usage table are in the cells too, so the
    /// row takes elements rather than text.
    /// </summary>
    private static void AddRow(Grid table, int row, UIElement[] cells)
    {
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < cells.Length; column++) Place(table, row, column, cells[column]);
    }

    /// <summary>
    /// One table row of text, one cell per text. The last cell is right-aligned, and it takes the tooltip when one is given (the token breakdown).
    /// </summary>
    private static void AddRow(Grid table, int row, bool bold, string[] cells, string? lastTip)
    {
        var elements = new UIElement[cells.Length];
        for (var column = 0; column < cells.Length; column++)
        {
            var last = column == cells.Length - 1;
            elements[column] = Cell(cells[column], bold, right: last, tip: last ? lastTip : null);
        }
        AddRow(table, row, elements);
    }

    private static void Place(Grid table, int row, int column, UIElement element)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        table.Children.Add(element);
    }

    /// <summary>A table cell's text: Consolas 12, right-aligned when asked, with a tooltip when one is given.</summary>
    private static TextBlock Cell(string text, bool bold = false, bool right = false, string? tip = null) => new()
    {
        Text = text,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Margin = new Thickness(0, 2, 18, 2),
        TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
        ToolTip = tip,
    };

    /// <summary>The four kinds of tokens, as the tooltip names them.</summary>
    private static string Breakdown(TokenUsage tokens) =>
        $"Input {Count(tokens.Input)} · output {Count(tokens.Output)} · cache written {Count(tokens.CacheCreation)} · cache read {Count(tokens.CacheRead)}";

    /// <summary>A token count with a thousands separator, as the current culture writes it.</summary>
    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string ProjectCount(int projects) => projects == 1 ? "1 project" : $"{projects} projects";

    /// <summary>The name a project shows: the folder's name (FolderDisplay), or the key when no name is known. The key still groups the usage.</summary>
    private string NameOf(string key) => _labels.TryGetValue(key, out var label) ? label.Name : key;

    /// <summary>The whole folder for the tooltip, or null when no folder is known.</summary>
    private string? TipOf(string key) => _labels.TryGetValue(key, out var label) ? label.FullPath : null;

    private static TextBlock DayLine(LimitDay day)
    {
        var parts = day.HitsByType.Select(pair => $"{LimitStatus.LabelOf(pair.Key)} × {pair.Value}");
        return new TextBlock
        {
            Text = $"{day.Day.ToString("yyyy-MM-dd ddd", CultureInfo.CurrentCulture)}   {string.Join("   ", parts)}   total {day.Total}",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 2),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static string LocalTime(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Setup;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Limits page (A4): a table of the limit hits (local time, the limit, its reset, the tokens used in the five hours before it), a line per day
/// with the hits of each type, and the line that says what is counted. It only draws what the window gives it; the hits were read on a pool thread.
/// </summary>
public sealed class LimitsView : UserControl
{
    private const string Footer = "Counts only sessions on this computer. Other machines and claude.ai chat use the same limit and are not shown.";

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

    private readonly TextBlock _footer = new()
    {
        Text = Footer,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
    };

    public LimitsView()
    {
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();

        _tableBlock.Children.Add(new TextBlock { Text = "Hits, newest first", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        _tableBlock.Children.Add(_table);

        _daysBlock.Children.Add(new TextBlock { Text = "Per day (local date)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        _daysBlock.Children.Add(_days);

        var root = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        root.Children.Add(new TextBlock { Text = "Limits", FontSize = 17, FontWeight = FontWeights.SemiBold });
        root.Children.Add(_note);
        root.Children.Add(_caption);
        root.Children.Add(refresh);
        root.Children.Add(_tableBlock);
        root.Children.Add(_daysBlock);
        root.Children.Add(_footer);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>The Refresh button was pressed. The window reads the transcripts again.</summary>
    public event Action? RefreshRequested;

    /// <summary>Takes the rail's palette, as the other views do: the text and the muted lines follow the theme.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        Foreground = RailPalette.Brush(palette.Text);
        var muted = RailPalette.Brush(palette.Muted);
        _note.Foreground = muted;
        _footer.Foreground = muted;
    }

    /// <summary>Shows the hits (newest first) and the days. With no hit, the empty-state line is shown and the tables are hidden.</summary>
    public void Show(IReadOnlyList<LimitHit> hits, IReadOnlyList<LimitDay> days)
    {
        var window = LimitHistory.DefaultDays;
        if (hits.Count == 0)
        {
            _caption.Text = $"No limit hits in the last {window} days.";
            _tableBlock.Visibility = Visibility.Collapsed;
            _daysBlock.Visibility = Visibility.Collapsed;
            return;
        }

        _caption.Text = $"{hits.Count} limit {(hits.Count == 1 ? "hit" : "hits")} in the last {window} days.";
        _tableBlock.Visibility = Visibility.Visible;
        _daysBlock.Visibility = Visibility.Visible;
        DrawTable(hits);
        _days.Children.Clear();
        foreach (var day in days) _days.Children.Add(DayLine(day));
    }

    /// <summary>Shows a line in place of the page's content, for a read that failed.</summary>
    public void ShowProblem(string text)
    {
        _caption.Text = text;
        _tableBlock.Visibility = Visibility.Collapsed;
        _daysBlock.Visibility = Visibility.Collapsed;
    }

    /// <summary>The hits as a table: the header, then one row per hit. The tokens are the four kinds together, with the breakdown in the tooltip.</summary>
    private void DrawTable(IReadOnlyList<LimitHit> hits)
    {
        _table.Children.Clear();
        _table.RowDefinitions.Clear();
        _table.ColumnDefinitions.Clear();
        for (var column = 0; column < 4; column++) _table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddRow(0, bold: true, ["When", "Limit", "Resets", "Tokens in the 5 h before"], lastTip: null);
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var tokens = hit.TokensBefore;
            var total = tokens.Input + tokens.Output + tokens.CacheCreation + tokens.CacheRead;
            var breakdown = $"Input {Count(tokens.Input)} · output {Count(tokens.Output)} · " +
                            $"cache written {Count(tokens.CacheCreation)} · cache read {Count(tokens.CacheRead)}";
            AddRow(i + 1, bold: false, [LocalTime(hit.Time), LimitStatus.LabelOf(hit.Type), LocalTime(hit.ResetsAt), Count(total)], lastTip: breakdown);
        }
    }

    /// <summary>
    /// One table row, one cell per text. The last cell is right-aligned, and it takes the tooltip when one is given (the token breakdown).
    /// </summary>
    private void AddRow(int row, bool bold, string[] cells, string? lastTip)
    {
        _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < cells.Length; column++)
        {
            var last = column == cells.Length - 1;
            var cell = new TextBlock
            {
                Text = cells[column],
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 18, 2),
                TextAlignment = last ? TextAlignment.Right : TextAlignment.Left,
                ToolTip = last ? lastTip : null,
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            _table.Children.Add(cell);
        }
    }

    /// <summary>A token count with a thousands separator, as the current culture writes it.</summary>
    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

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

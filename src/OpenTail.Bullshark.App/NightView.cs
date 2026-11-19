using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Night page (night batch N6), the fifth sub-tab of Insights: the bedtime preflight as a list. The verdict is on top, then one row per check
/// with its status as a word ("ok", "check", "unknown"; "check" in the palette's warning colour), then the quiet-hours state with a button to
/// Settings. It only draws the <see cref="PreflightReport"/> the window built; nothing here starts, sends or changes anything.
/// </summary>
public sealed class NightView : UserControl
{
    private const string Footer =
        "Nothing here starts, sends or changes anything. Bullshark does not keep the computer awake: the Sleep row only reports the AC sleep " +
        "timeout. The checks are read when this page is shown, on Refresh, and every 30 seconds while it is open.";

    private RailPalette _palette = RailPalette.DarkPalette;

    private PreflightReport? _report;

    /// <summary>The line shown in place of the verdict when a read failed; null when the checks are shown.</summary>
    private string? _problem;
    private string _lastPass = string.Empty;
    private QuietState _quiet = new(null, false);

    private readonly TextBlock _verdict = new()
    {
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 2),
    };

    private readonly TextBlock _lastPassText = new()
    {
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 12, 0),
    };

    private readonly Button _refresh = new()
    {
        Content = UiIcon.Label(UiIcon.Refresh, "Refresh"),
        Padding = new Thickness(12, 4, 12, 4),
    };

    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 10, 0, 0) };

    private readonly TextBlock _quietText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 14, 0, 4),
    };

    private readonly Button _settings = new()
    {
        Content = "Set quiet hours in Settings…",
        HorizontalAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(0, 2, 0, 2),
    };

    private readonly TextBlock _footer = new()
    {
        Text = Footer,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 14, 0, 0),
    };

    public NightView()
    {
        // The ghost style of the window's buttons (Styles/Controls.xaml). A missing style leaves the default button, not a failure.
        if (TryFindResource("GhostButton") is Style ghost) _settings.Style = ghost;
        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        top.Children.Add(_lastPassText);
        top.Children.Add(_refresh);

        var root = new StackPanel { Margin = new Thickness(12, 8, 12, 12), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(_verdict);
        root.Children.Add(top);
        root.Children.Add(_rows);
        root.Children.Add(_quietText);
        root.Children.Add(_settings);
        root.Children.Add(_footer);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Raised when Refresh is pressed: the window reads the checks again.</summary>
    public event Action? RefreshRequested;

    /// <summary>Raised when the quiet-hours button is pressed: the window opens Settings on its General sub-tab.</summary>
    public event Action? SettingsRequested;

    /// <summary>Draws the report, the last pass in words ("12 s ago"), and the quiet-hours state.</summary>
    public void Show(PreflightReport report, string lastPass, QuietState quiet)
    {
        _report = report;
        _problem = null;
        _lastPass = lastPass;
        _quiet = quiet;
        Redraw();
    }

    /// <summary>Shows a line in place of the checks, for a read that failed. The rows are cleared.</summary>
    public void ShowProblem(string text)
    {
        _report = null;
        _problem = text;
        Redraw();
    }

    /// <summary>Takes the rail's palette and draws again in it.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Foreground = RailPalette.Brush(palette.Text);
        Redraw();
    }

    private void Redraw()
    {
        var palette = _palette;
        var text = RailPalette.Brush(palette.Text);
        var muted = RailPalette.Brush(palette.Muted);
        var warn = RailPalette.Brush(palette.Warn);

        _verdict.Text = _problem ?? _report?.Verdict ?? "Checking the night…";
        _verdict.Foreground = _problem is not null || (_report is { } report && report.Rows.Any(row => row.Status == PreflightStatus.Check)) ? warn : text;
        _lastPassText.Text = $"Last pass: {_lastPass}";
        _lastPassText.Foreground = muted;
        _footer.Foreground = muted;

        _rows.Children.Clear();
        if (_report is { } shown)
        {
            foreach (var row in shown.Rows) _rows.Children.Add(RowOf(row, palette));
        }

        _quietText.Text = _quiet.Window is { } window
            ? $"Quiet hours {window}: {(_quiet.Now ? "quiet now, so sound, flash and toasts are off" : "not quiet now, so sound, flash and toasts are on")}."
            : "Quiet hours are off: sound, flash and toasts are on at all times.";
        _quietText.Foreground = text;
        _settings.Foreground = RailPalette.Brush(palette.Accent);
    }

    /// <summary>One check: its status word, its name, and its line. "check" is drawn in the warning colour; the others are not.</summary>
    private static Grid RowOf(PreflightRow row, RailPalette palette)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (word, brush, bold) = row.Status switch
        {
            PreflightStatus.Check => ("check", RailPalette.Brush(palette.Warn), true),
            PreflightStatus.Unknown => ("unknown", RailPalette.Brush(palette.Muted), false),
            _ => ("ok", RailPalette.Brush(palette.Text), false),
        };
        var status = new TextBlock
        {
            Text = word,
            Foreground = brush,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 12,
            Margin = new Thickness(0, 1, 8, 0),
        };
        var name = new TextBlock
        {
            Text = row.Name,
            Foreground = RailPalette.Brush(palette.Text),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var line = new TextBlock
        {
            Text = row.Text,
            Foreground = RailPalette.Brush(row.Status == PreflightStatus.Unknown ? palette.Muted : palette.Text),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(status, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(line, 2);
        grid.Children.Add(status);
        grid.Children.Add(name);
        grid.Children.Add(line);
        return grid;
    }
}

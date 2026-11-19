using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The run timeline (additions item 5): a run chosen from a list, its sessions as a filter, and the events of the run in time order,
/// one line each. It only draws what the window gives it; the events were read on a pool thread.
/// </summary>
public sealed class TimelineView : UserControl
{
    private const string AllSessions = "All sessions";

    /// <summary>A run in the run list. The label is what the owner reads, and it is what the list shows (ToString).</summary>
    private sealed record RunChoice(RunRecord Run, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _runs = new() { MinWidth = 320 };
    private readonly ComboBox _sessions = new() { MinWidth = 240, Margin = new Thickness(8, 0, 0, 0) };
    // The list is not inside a ScrollViewer: the ListBox scrolls and virtualizes its own rows (round 5 item 10).
    private readonly ListBox _lines = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        MaxHeight = 600,
    };
    private readonly TextBlock _caption = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };

    /// <summary>The next step when no run has been started: it opens the setup tab (firstrun batch B8). Shown only then.</summary>
    private readonly Button _startRun = new()
    {
        Content = "Open the setup tab",
        Padding = new Thickness(12, 4, 12, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 0, 0, 8),
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _olderText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _showOlder = new() { Content = "Show older", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel _olderRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };

    private IReadOnlyList<SupervisorEvent> _events = [];
    private bool _loading;
    private RailPalette _palette = RailPalette.DarkPalette;

    /// <summary>How many rows of the chosen run are drawn. "Show older" raises it; a new run or session starts again at the default.</summary>
    private int _limit = TimelineRows.DefaultLimit;

    public TimelineView()
    {
        var refresh = new Button { Content = UiIcon.Label(UiIcon.Refresh, "Refresh"), Padding = new Thickness(12, 4, 12, 4) };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();

        var choosers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        choosers.Children.Add(new TextBlock { Text = "Run", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        choosers.Children.Add(_runs);
        choosers.Children.Add(new TextBlock { Text = "Session", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        choosers.Children.Add(_sessions);
        choosers.Children.Add(refresh);

        _olderRow.Children.Add(_olderText);
        _olderRow.Children.Add(_showOlder);
        _showOlder.Click += (_, _) =>
        {
            _limit += TimelineRows.DefaultLimit;
            Draw();
        };

        var root = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        // No page title here: the Insights tab's sub-tab row names the page (2026-11-18).
        root.Children.Add(_caption);
        root.Children.Add(_startRun);
        _startRun.Click += (_, _) => SetupRequested?.Invoke();
        root.Children.Add(choosers);
        root.Children.Add(_olderRow);
        root.Children.Add(_lines);
        Content = root;

        _runs.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            FillSessions();
            _limit = TimelineRows.DefaultLimit;
            Draw();
        };
        _sessions.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _limit = TimelineRows.DefaultLimit;
            Draw();
        };
    }

    /// <summary>The Refresh button was pressed. The window reads the log again.</summary>
    public event Action? RefreshRequested;

    /// <summary>"Open the setup tab" was pressed, on the empty state (firstrun batch B8).</summary>
    public event Action? SetupRequested;

    /// <summary>
    /// Takes the rail's palette, as the other views do. The list's items were drawn in the default colour, which is unreadable on the
    /// dark background, so the lines are drawn again in the palette's text colour.
    /// </summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Foreground = RailPalette.Brush(palette.Text);
        _caption.Foreground = RailPalette.Brush(palette.Muted);
        _olderText.Foreground = RailPalette.Brush(palette.Muted);
        Draw();
    }

    /// <summary>Shows the runs (newest first), and the events of the log. The run chosen before stays chosen when it is still listed.</summary>
    public void Show(IReadOnlyList<RunRecord> runs, IReadOnlyList<SupervisorEvent> events)
    {
        var previous = (_runs.SelectedItem as RunChoice)?.Run.RunId;
        _loading = true;
        _runs.Items.Clear();
        foreach (var run in runs.OrderByDescending(r => r.CreatedAt))
            _runs.Items.Add(new RunChoice(run, $"{run.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {run.PatternKey} · run {run.RunId}"));
        _runs.SelectedIndex = Math.Max(0, _runs.Items.Cast<RunChoice>().ToList().FindIndex(c => c.Run.RunId == previous));
        _events = events;
        _loading = false;

        // With no run, the caption names the next step and the button takes the owner to the setup tab (firstrun batch B8).
        _caption.Text = runs.Count == 0 ? "No runs yet. Start one from the + tab." : $"{events.Count:N0} events are recorded in all.";
        _startRun.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FillSessions();
        Draw();
    }

    /// <summary>Shows a line under the caption, for a log that could not be read.</summary>
    public void ShowProblem(string text)
    {
        _caption.Text = text;
        _startRun.Visibility = Visibility.Collapsed;
        _lines.Items.Clear();
    }

    private void FillSessions()
    {
        _sessions.Items.Clear();
        _sessions.Items.Add(AllSessions);
        if (_runs.SelectedItem is RunChoice choice)
            foreach (var name in choice.Run.Specs.Select(s => s.Name).Distinct())
                _sessions.Items.Add(name);
        _sessions.SelectedIndex = 0;
    }

    /// <summary>
    /// The events of the chosen run, in the order they were written, filtered by the chosen session. The newest rows are drawn, up to the
    /// limit; the rest wait behind "Show older". The rules are in <see cref="TimelineRows"/>, which a test reaches (round 5 item 10).
    /// </summary>
    private void Draw()
    {
        _lines.Items.Clear();
        _olderRow.Visibility = Visibility.Collapsed;
        if (_runs.SelectedItem is not RunChoice choice)
        {
            _lines.Items.Add(Line("Nothing to show yet."));
            return;
        }

        var session = _sessions.SelectedItem as string;
        var all = TimelineRows.ForRun(_events, choice.Run, session is null or AllSessions ? null : session);
        if (all.Count == 0)
        {
            _lines.Items.Add(Line("No events for this run yet."));
            return;
        }

        var page = TimelineRows.Newest(all, _limit);
        if (page.Older > 0)
        {
            _olderText.Text = $"{page.Older:N0} older events are not drawn.";
            _olderRow.Visibility = Visibility.Visible;
        }
        foreach (var e in page.Rows) _lines.Items.Add(Line(TimelineRows.Line(e)));
    }

    /// <summary>One line of the list, in the palette's text colour.</summary>
    private TextBlock Line(string text) => new() { Text = text, Foreground = RailPalette.Brush(_palette.Text) };
}

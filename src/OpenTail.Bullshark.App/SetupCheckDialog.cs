using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The setup check, opened from the status bar (docs/plans/setup-check.md). It lists what SetupCheck found: a line for each check, with
/// a dot for the result and the detail under it. It changes nothing, and it starts no hook or server. "Check again" runs it again.
/// </summary>
public sealed class SetupCheckDialog : Window
{
    private readonly Func<IReadOnlyList<SetupItem>> _run;
    private readonly RailPalette _palette;
    private readonly TextBlock _summary = new()
    {
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };
    private readonly StackPanel _rows = new();

    public SetupCheckDialog(RailPalette palette, Func<IReadOnlyList<SetupItem>> run)
    {
        _palette = palette;
        _run = run;
        Title = "Setup check";
        Width = 680;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RailPalette.Brush(palette.Background);
        Foreground = RailPalette.Brush(palette.Text);

        var root = new DockPanel { Margin = new Thickness(16) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var again = new Button { Content = UiIcon.Label(UiIcon.Refresh, "Check again"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        again.Click += (_, _) => Check();
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(again);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var note = new TextBlock
        {
            Text = "Reads the Claude Code settings on this computer and changes nothing. Hooks and MCP servers are not started.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = RailPalette.Brush(palette.Muted),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows });
        Content = root;

        Check();
    }

    private void Check()
    {
        var items = _run();
        var ok = items.Count(i => i.State == SetupState.Ok);
        var warnings = items.Count(i => i.State == SetupState.Warning);
        var problems = items.Count(i => i.State == SetupState.Problem);
        _summary.Text = $"{ok} ok · {warnings} to look at · {problems} problem{(problems == 1 ? string.Empty : "s")}";
        var worst = problems > 0 ? SetupState.Problem : warnings > 0 ? SetupState.Warning : SetupState.Ok;
        _summary.Foreground = RailPalette.Brush(Colour(worst));

        _rows.Children.Clear();
        foreach (var item in items) _rows.Children.Add(Row(item));
    }

    private UIElement Row(SetupItem item)
    {
        var dot = new TextBlock
        {
            Text = "●",
            Foreground = RailPalette.Brush(Colour(item.State)),
            FontSize = 14,
            Margin = new Thickness(0, 1, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        // The state is also a word, so the colour is not the only sign (firstrun batch B8).
        var word = new TextBlock
        {
            Text = StateWord(item.State),
            Foreground = RailPalette.Brush(Colour(item.State)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        head.Children.Add(dot);
        head.Children.Add(word);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = $"{item.Area} · {item.Name}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = item.Detail,
            Foreground = RailPalette.Brush(_palette.Muted),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(head, Dock.Left);
        row.Children.Add(head);
        row.Children.Add(text);
        // A screen reader reads the state, the check and its detail as one line (firstrun batch B8).
        System.Windows.Automation.AutomationProperties.SetName(row, $"{StateWord(item.State)}: {item.Area}, {item.Name}. {item.Detail}");
        return row;
    }

    /// <summary>The state as a word, shown beside the dot: Ok, Warning or Problem.</summary>
    private static string StateWord(SetupState state) => state switch
    {
        SetupState.Ok => "Ok",
        SetupState.Warning => "Warning",
        _ => "Problem",
    };

    private string Colour(SetupState state) => (state, _palette.IsLight) switch
    {
        (SetupState.Ok, false) => "#4CAF6A",
        (SetupState.Ok, true) => "#23803F",
        (SetupState.Warning, false) => "#E0B341",
        (SetupState.Warning, true) => "#9A6B00",
        (_, false) => "#E5534B",
        _ => "#B83227",
    };
}

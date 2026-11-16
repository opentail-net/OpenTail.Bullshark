using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The results of a history search (docs/plans/history-search.md): one block per session, newest first, with its title, folder, age,
/// how many of its messages matched, and the newest matching messages. A click on a title or on a message opens the session's
/// transcript at that line.
/// </summary>
public sealed class SearchView : UserControl
{
    private readonly TextBlock _heading = new();
    private readonly StackPanel _list = new();

    public SearchView()
    {
        Background = Brush("#1E1E1E");
        Foreground = Brush("#E6E6E6");

        var root = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        _heading.FontSize = 13;
        _heading.FontWeight = FontWeights.SemiBold;
        _heading.Foreground = Brush("#E6E6E6");
        _heading.TextWrapping = TextWrapping.Wrap;
        _heading.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(_heading, Dock.Top);
        root.Children.Add(_heading);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = root;
    }

    /// <summary>Raised with a session and the line number of a title or message the owner clicked.</summary>
    public event Action<SessionDigest, int>? OpenRequested;

    public void ShowSearching(string query)
    {
        _heading.Text = $"Searching for \"{query}\"…";
        _list.Children.Clear();
    }

    public void ShowMessage(string query, string message)
    {
        _heading.Text = $"\"{query}\"";
        _list.Children.Clear();
        _list.Children.Add(Muted(message));
    }

    public void ShowResults(string query, IReadOnlyList<SessionMatch> matches)
    {
        _list.Children.Clear();
        _heading.Text = matches.Count == 0
            ? $"No session says every word of \"{query}\"."
            : $"{matches.Count} session(s) say every word of \"{query}\", newest first" +
              (matches.Count == HistorySearch.MaxResults ? $" (the first {HistorySearch.MaxResults} are shown)" : string.Empty) + ".";
        foreach (var match in matches) _list.Children.Add(Block(match));
    }

    private UIElement Block(SessionMatch match)
    {
        var digest = match.Session;
        var stack = new StackPanel();

        var title = new TextBlock
        {
            Text = digest.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Cursor = Cursors.Hand,
            Foreground = Brush("#E6E6E6"),
            ToolTip = "Open the transcript at its newest matching message",
        };
        var newest = match.Hits.LastOrDefault()?.LineNumber ?? 1;
        title.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke(digest, newest);
        stack.Children.Add(title);

        stack.Children.Add(new TextBlock
        {
            Text = $"{digest.ProjectFolder} · {AgeText(digest.LastActivity)} · {match.MatchingMessages} message(s) match",
            FontSize = 11,
            Foreground = Brush("#8A8F98"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
        });

        // The newest matching messages first.
        foreach (var hit in match.Hits.Reverse())
        {
            var line = new TextBlock
            {
                Text = $"{hit.LineNumber,5}  {(hit.Kind == "you" ? "you" : "claude")}: {hit.Snippet}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Cursor = Cursors.Hand,
                Foreground = Brush(hit.Kind == "you" ? "#9CDCFE" : "#CCCCCC"),
                Margin = new Thickness(0, 2, 0, 2),
            };
            var number = hit.LineNumber;
            line.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke(digest, number);
            stack.Children.Add(line);
        }

        return new Border
        {
            Background = Brush("#252629"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack,
        };
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Foreground = Brush("#8A8F98"),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A short age for a result: "3d ago", "5h ago", "12m ago". "time unknown" when there is no time.</summary>
    private static string AgeText(DateTimeOffset? at)
    {
        if (at is not { } when) return "time unknown";
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d ago";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h ago";
        return $"{Math.Max(0, (int)span.TotalMinutes)}m ago";
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

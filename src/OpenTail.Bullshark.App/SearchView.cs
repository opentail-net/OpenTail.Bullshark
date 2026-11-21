using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The results of a history search (docs/plans/Done/DONE_history-search.md): one block per session, newest first, with its title, folder, age,
/// how many of its messages matched, and the newest matching messages. A click on a title or on a message opens the session's
/// transcript at that line. The colours follow the rail's theme.
/// </summary>
public sealed class SearchView : UserControl
{
    private enum Mode
    {
        Searching,
        Message,
        Results,
    }

    private readonly TextBlock _heading = new();
    private readonly StackPanel _list = new();

    /// <summary>The title of a result as the rail shows it, when the window gives one.</summary>
    private Func<SessionDigest, string>? _titleOf;

    /// <summary>The folder name of a result as the window gives it (the group's label, the same one the rail and Usage use). Null means FolderDisplay alone.</summary>
    private Func<SessionDigest, FolderLabel>? _folderOf;
    private RailPalette _palette = RailPalette.DarkPalette;
    private Mode _mode = Mode.Searching;
    private string _query = string.Empty;
    private string _message = string.Empty;
    private IReadOnlyList<SessionMatch> _matches = [];

    public SearchView()
    {
        var root = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        _heading.FontSize = 13;
        _heading.FontWeight = FontWeights.SemiBold;
        _heading.TextWrapping = TextWrapping.Wrap;
        _heading.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(_heading, Dock.Top);
        root.Children.Add(_heading);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = root;
        ApplyTheme(_palette);
    }

    /// <summary>Raised with a session and the line number of a title or message the owner clicked.</summary>
    public event Action<SessionDigest, int>? OpenRequested;

    /// <summary>Colours the view from the rail's palette, and redraws what is shown in it.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Background = RailPalette.Brush(palette.Background);
        Render();
    }

    public void ShowSearching(string query)
    {
        _mode = Mode.Searching;
        _query = query;
        Render();
    }

    public void ShowMessage(string query, string message)
    {
        _mode = Mode.Message;
        _query = query;
        _message = message;
        Render();
    }

    /// <summary>
    /// Shows the matching sessions. <paramref name="titleOf"/> gives each one's title as the rail shows it (the owner's name, when there is one);
    /// without it, the transcript's own title is shown.
    /// </summary>
    /// <param name="searched">How many sessions the search read (not counting subagents). With no hit it says so (firstrun batch B8); null says nothing about it.</param>
    public void ShowResults(string query, IReadOnlyList<SessionMatch> matches, Func<SessionDigest, string>? titleOf = null,
        Func<SessionDigest, FolderLabel>? folderOf = null, int? searched = null)
    {
        _mode = Mode.Results;
        _query = query;
        _matches = matches;
        _titleOf = titleOf;
        _folderOf = folderOf;
        _searched = searched;
        Render();
    }

    /// <summary>The sessions the last search read, for the empty result's line. Null when the caller did not say.</summary>
    private int? _searched;

    private void Render()
    {
        _list.Children.Clear();
        _heading.Foreground = RailPalette.Brush(_palette.Text);
        switch (_mode)
        {
            case Mode.Searching:
                _heading.Text = $"Searching for \"{_query}\"…";
                break;

            case Mode.Message:
                _heading.Text = $"\"{_query}\"";
                _list.Children.Add(Muted(_message));
                break;

            case Mode.Results:
                _heading.Text = _matches.Count == 0
                    ? $"No session says every word of \"{_query}\". {OpenTail.Bullshark.Core.Search.SearchedSentence.Of(_searched)}".TrimEnd()
                    : $"{_matches.Count} session(s) say every word of \"{_query}\", newest first" +
                      (_matches.Count == HistorySearch.MaxResults ? $" (the first {HistorySearch.MaxResults} are shown)" : string.Empty) + ".";
                foreach (var match in _matches) _list.Children.Add(Block(match));
                break;
        }
    }

    private UIElement Block(SessionMatch match)
    {
        var digest = match.Session;
        var stack = new StackPanel();
        var folder = _folderOf?.Invoke(digest) ?? FolderDisplay.Of(digest.ProjectFolder, [digest.Cwd]);
        var title = _titleOf?.Invoke(digest) ?? digest.Title;
        var newest = match.Hits.LastOrDefault()?.LineNumber ?? 1;

        // The session's head (its title and its folder line) takes the keyboard too: Enter opens it at the newest match, as a click does.
        var head = new StackPanel();
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Cursor = Cursors.Hand,
            Foreground = RailPalette.Brush(_palette.Text),
            ToolTip = "Open the transcript at its newest matching message",
        };
        titleText.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke(digest, newest);
        head.Children.Add(titleText);

        head.Children.Add(new TextBlock
        {
            // The folder is the group's name (FolderDisplay, as the rail shows it); its whole path is on the tooltip. A word found only in the owner's
            // name, tags or note matches with no message (D-L7).
            Text = match.MatchingMessages == 0
                ? $"{folder.Name} · {AgeText(digest.LastActivity)} · matched its name, tags or note"
                : $"{folder.Name} · {AgeText(digest.LastActivity)} · {match.MatchingMessages} message(s) match",
            ToolTip = FolderTip(folder, digest),
            FontSize = 11,
            Foreground = RailPalette.Brush(_palette.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
        });
        stack.Children.Add(KeyboardRow.Wrap(head, _palette, $"{title}, {folder.Name}. Open at the newest matching message.",
            () => OpenRequested?.Invoke(digest, newest)));

        // The newest matching messages first. The owner's messages are a blue that reads on both themes; Claude's take the text colour.
        foreach (var hit in match.Hits.Reverse())
        {
            var who = hit.Kind == "you" ? "you" : "claude";
            var line = new TextBlock
            {
                Text = $"{hit.LineNumber,5}  {who}: {hit.Snippet}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Cursor = Cursors.Hand,
                Foreground = RailPalette.Brush(hit.Kind == "you" ? (_palette.IsLight ? "#0B5CAD" : "#9CDCFE") : _palette.Text),
                Margin = new Thickness(0, 2, 0, 2),
            };
            var number = hit.LineNumber;
            line.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke(digest, number);
            stack.Children.Add(KeyboardRow.Wrap(line, _palette, $"Line {number}, {who}: {hit.Snippet}", () => OpenRequested?.Invoke(digest, number)));
        }

        return new Border
        {
            Background = RailPalette.Brush(_palette.Panel),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack,
        };
    }

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        Foreground = RailPalette.Brush(_palette.Muted),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>The folder's whole path for the tooltip, or a line that says the transcripts do not name it (as the rail's tooltip does).</summary>
    private static string FolderTip(FolderLabel folder, SessionDigest digest) =>
        folder.FullPath is null
            ? $"Folder: {digest.ProjectFolder}\nThe transcripts do not name the working folder."
            : $"Folder: {folder.FullPath}";

    /// <summary>A short age for a result: "3d ago", "5h ago", "12m ago". "time unknown" when there is no time.</summary>
    private static string AgeText(DateTimeOffset? at)
    {
        if (at is not { } when) return "time unknown";
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d ago";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h ago";
        return $"{Math.Max(0, (int)span.TotalMinutes)}m ago";
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// One result of a search of past Codex sessions, as the results view shows it: the title as the Codex row shows it, the folder's name and its full path
/// (null when the rollouts name none), the relative time, and whether the owner archived the session.
/// </summary>
internal sealed record CodexSearchRow(SessionSummary Session, string Title, string FolderName, string? FolderPath, string When, bool Archived);

/// <summary>
/// The results of a search of past Codex sessions (parity P6): one block per session, in the order the window gives (newest first). Each block has the
/// session's title, its folder, its relative time and "archived" when it is. A click on the title, or Enter on it, opens the session's Codex transcript
/// (it does not resume the session). The look follows <see cref="SearchView"/>, Claude's results view, which is not changed.
/// </summary>
internal sealed class CodexSearchView : UserControl
{
    private readonly TextBlock _heading = new();
    private readonly StackPanel _list = new();
    private RailPalette _palette = RailPalette.DarkPalette;
    private string _query = string.Empty;
    private IReadOnlyList<CodexSearchRow> _rows = [];

    public CodexSearchView()
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

    /// <summary>Raised with the session a click or Enter on a result names. The window opens its Codex transcript.</summary>
    public event Action<SessionSummary>? OpenRequested;

    /// <summary>Colours the view from the rail's palette, and redraws what is shown in it.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Background = RailPalette.Brush(palette.Background);
        Render();
    }

    /// <summary>Shows the results of a query. An empty list says that no Codex session matches the query.</summary>
    public void ShowResults(string query, IReadOnlyList<CodexSearchRow> rows)
    {
        _query = query;
        _rows = rows;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        _heading.Foreground = RailPalette.Brush(_palette.Text);
        _heading.Text = _rows.Count == 0
            ? $"No Codex session matches \"{_query}\"."
            : $"{_rows.Count} Codex session(s) match \"{_query}\", newest first.";
        foreach (var row in _rows) _list.Children.Add(Block(row));
    }

    private UIElement Block(CodexSearchRow row)
    {
        var session = row.Session;
        var head = new StackPanel();
        var titleText = new TextBlock
        {
            Text = row.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Cursor = Cursors.Hand,
            Foreground = RailPalette.Brush(_palette.Text),
            ToolTip = "Open the Codex transcript. This does not resume the session.",
        };
        titleText.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke(session);
        head.Children.Add(titleText);

        // The folder, the relative time, then "archived" in the muted text, as the Codex row's second line gives them.
        var parts = new[] { row.FolderName, row.When, row.Archived ? "archived" : null };
        head.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part))),
            ToolTip = row.FolderPath is null ? "The rollouts do not name the working folder." : $"Folder: {row.FolderPath}",
            FontSize = 11,
            Foreground = RailPalette.Brush(_palette.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        return new Border
        {
            Background = RailPalette.Brush(_palette.Panel),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = KeyboardRow.Wrap(head, _palette, $"{row.Title}, {row.FolderName}. Open the Codex transcript.", () => OpenRequested?.Invoke(session)),
        };
    }
}

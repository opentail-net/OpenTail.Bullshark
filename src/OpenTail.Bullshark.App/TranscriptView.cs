using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Read-only view of one past session, shown in the pane (S3). It loads page by page: the last page first, and earlier
/// pages on request. Resume asks first (the resume prompt shows what would be sent), then starts the session (D14, D-S11).
/// The transcript rendering is the History tab's, moved here unchanged.
/// </summary>
public sealed class TranscriptView : UserControl
{
    private const int PageSize = 200;

    private readonly TextBlock _historyTitle = new();
    private readonly TextBlock _historyStats = new();
    private readonly Button _earlier = new();
    private readonly StackPanel _transcript = new();
    private readonly ScrollViewer _transcriptScroll = new();
    private TranscriptPager? _pager;
    private SessionDigest? _digest;
    private readonly Button _resume = new();
    private readonly TextBlock _resumeHint = new();
    private int _firstShown;

    /// <summary>The line a search opened this view at, once it is shown. Null for the last page.</summary>
    private TextBlock? _focus;

    public TranscriptView()
    {
        Background = Brush("#1E1E1E");
        Foreground = Brush("#E6E6E6");
        Content = BuildContent();
    }

    /// <summary>
    /// Shows a session's headline numbers at once, and a page of its transcript: the last page, or with a focus line (a search result)
    /// the page around that line, with the line marked and brought into view. Nothing else is loaded.
    /// </summary>
    public async Task ShowAsync(SessionDigest digest, int? focusLine = null)
    {
        _digest = digest;
        _resume.IsEnabled = true;
        _resumeHint.Text = ResumeHint(digest);
        _historyTitle.Text = $"{digest.Title}\n{digest.SessionId}\n{digest.FilePath}";
        _historyStats.Text = StatsText(digest);

        var pager = await Task.Run(() => TranscriptPager.Open(digest.FilePath));
        _pager = pager;
        _firstShown = focusLine is { } wanted
            ? Math.Max(0, Math.Clamp(wanted - 1, 0, Math.Max(0, pager.LineCount - 1)) - PageSize / 2)
            : pager.LastPageStart(PageSize);
        _transcript.Children.Clear();
        _focus = null;
        foreach (var line in pager.ReadPage(_firstShown, PageSize))
        {
            var block = Render(line);
            if (focusLine == line.Number)
            {
                block.Background = Brush("#2D3E50");
                _focus = block;
            }
            _transcript.Children.Add(block);
        }
        _earlier.IsEnabled = _firstShown > 0;
        if (_focus is null) _transcriptScroll.ScrollToBottom();
        else Dispatcher.BeginInvoke(new Action(() => _focus?.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private UIElement BuildContent()
    {
        var right = new DockPanel { Margin = new Thickness(10) };

        var top = new TextBlock
        {
            Text = "Past session (read-only, local)",
            Foreground = Brush("#BBBBBB"),
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);

        _historyTitle.FontSize = 13;
        _historyTitle.FontWeight = FontWeights.SemiBold;
        _historyTitle.Foreground = Brush("#E6E6E6");
        _historyTitle.Text = "Choose a session in the list on the left.";
        _historyTitle.TextWrapping = TextWrapping.Wrap;
        _historyTitle.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_historyTitle, Dock.Top);
        right.Children.Add(_historyTitle);

        _historyStats.Foreground = Brush("#BBBBBB");
        _historyStats.FontFamily = new FontFamily("Consolas");
        _historyStats.FontSize = 12;
        _historyStats.TextWrapping = TextWrapping.Wrap;
        _historyStats.Margin = new Thickness(0, 4, 0, 6);
        DockPanel.SetDock(_historyStats, Dock.Top);
        right.Children.Add(_historyStats);

        _earlier.Content = "Load earlier";
        _earlier.Padding = new Thickness(10, 2, 10, 2);
        _earlier.HorizontalAlignment = HorizontalAlignment.Left;
        _earlier.Margin = new Thickness(0, 0, 0, 6);
        _earlier.IsEnabled = false;
        _earlier.Click += (_, _) => LoadEarlier();
        DockPanel.SetDock(_earlier, Dock.Top);
        right.Children.Add(_earlier);

        // The one way to resume sits at the bottom, where a reply would be typed. It shows what is recommended and opens the choices.
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        _resume.Content = "Resume…";
        _resume.IsEnabled = false;
        _resume.Padding = new Thickness(14, 4, 14, 4);
        _resume.Margin = new Thickness(12, 0, 0, 0);
        _resume.ToolTip = "Opens the ways to continue this session. Nothing runs until you confirm there.";
        _resume.Click += (_, _) => RaiseResume();
        DockPanel.SetDock(_resume, Dock.Right);
        bottom.Children.Add(_resume);
        _resumeHint.Foreground = Brush("#BBBBBB");
        _resumeHint.FontSize = 12;
        _resumeHint.TextWrapping = TextWrapping.Wrap;
        _resumeHint.VerticalAlignment = VerticalAlignment.Center;
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

    private static string StatsText(SessionDigest d)
    {
        var tools = d.ToolNames.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {kv.Value}");
        var files = d.FilesWritten.Take(5);
        return $"turns {d.Turns}  ·  messages {d.AssistantMessages}  ·  tools {d.ToolCalls} ({string.Join(", ", tools)})\n" +
               $"files written {d.FilesWritten.Count}: {string.Join(", ", files)}{(d.FilesWritten.Count > 5 ? ", …" : string.Empty)}\n" +
               $"context at last turn {d.ContextTokensAtLastTurn:N0} tokens  ·  this session: input {d.Usage.Input:N0}, output {d.Usage.Output:N0}, " +
               $"cache read {d.Usage.CacheRead:N0}, cache write {d.Usage.CacheCreation:N0}";
    }

    private static TextBlock Render(TranscriptLine line)
    {
        var (color, mono) = line.Kind switch
        {
            "you" => ("#9CDCFE", false),
            "claude" => ("#E6E6E6", false),
            "tool" => ("#C586C0", true),
            "result" => ("#808080", true),
            "turn" => ("#6A9955", true),
            _ => ("#CCCCCC", false),
        };
        return new TextBlock
        {
            Text = $"{line.Number,5}  {line.Text}",
            Foreground = Brush(color),
            FontFamily = mono ? new FontFamily("Consolas") : new FontFamily("Segoe UI"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
        };
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

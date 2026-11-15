using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;

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
    private readonly Button _resumeFull = new();
    private readonly Button _resumeSummary = new();
    private int _firstShown;

    public TranscriptView()
    {
        Background = Brush("#1E1E1E");
        Foreground = Brush("#E6E6E6");
        Content = BuildContent();
    }

    /// <summary>Shows a session's headline numbers at once, and its last page of transcript. Nothing else is loaded.</summary>
    public async Task ShowAsync(SessionDigest digest)
    {
        _digest = digest;
        _resumeFull.IsEnabled = true;
        _resumeSummary.IsEnabled = true;
        _historyTitle.Text = $"{digest.Title}\n{digest.SessionId}\n{digest.FilePath}";
        _historyStats.Text = StatsText(digest);

        var pager = await Task.Run(() => TranscriptPager.Open(digest.FilePath));
        _pager = pager;
        _firstShown = pager.LastPageStart(PageSize);
        _transcript.Children.Clear();
        foreach (var line in pager.ReadPage(_firstShown, PageSize)) _transcript.Children.Add(Render(line));
        _earlier.IsEnabled = _firstShown > 0;
        _transcriptScroll.ScrollToBottom();
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

        var resume = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _resumeFull.Content = "Resume in full…";
        _resumeFull.IsEnabled = false;
        _resumeFull.Padding = new Thickness(10, 2, 10, 2);
        _resumeFull.Margin = new Thickness(0, 0, 8, 4);
        _resumeFull.ToolTip = "Resumes the whole conversation as context. The prompt shows its size and cost before anything runs.";
        _resumeFull.Click += (_, _) => RaiseResume(ResumeMode.Full);
        resume.Children.Add(_resumeFull);
        _resumeSummary.Content = "Resume from summary…";
        _resumeSummary.IsEnabled = false;
        _resumeSummary.Padding = new Thickness(10, 2, 10, 2);
        _resumeSummary.Margin = new Thickness(0, 0, 0, 4);
        _resumeSummary.ToolTip = "Starts a new session from a summary (minimal, normal or complete), or from the last turns only. Nothing runs before you confirm.";
        _resumeSummary.Click += (_, _) => RaiseResume(ResumeMode.Summary);
        resume.Children.Add(_resumeSummary);
        DockPanel.SetDock(resume, Dock.Top);
        right.Children.Add(resume);

        _earlier.Content = "Load earlier";
        _earlier.Padding = new Thickness(10, 2, 10, 2);
        _earlier.HorizontalAlignment = HorizontalAlignment.Left;
        _earlier.Margin = new Thickness(0, 0, 0, 6);
        _earlier.IsEnabled = false;
        _earlier.Click += (_, _) => LoadEarlier();
        DockPanel.SetDock(_earlier, Dock.Top);
        right.Children.Add(_earlier);

        _transcript.Margin = new Thickness(0, 0, 12, 0);
        _transcriptScroll.Content = _transcript;
        _transcriptScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        right.Children.Add(_transcriptScroll);

        return right;
    }

    /// <summary>Raised when the owner asks to resume the session shown, with the way it was asked for preselected.</summary>
    public event Action<SessionDigest, ResumeMode>? ResumeRequested;

    private void RaiseResume(ResumeMode startWith)
    {
        if (_digest is not null) ResumeRequested?.Invoke(_digest, startWith);
    }

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

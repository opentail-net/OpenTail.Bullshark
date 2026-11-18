using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The "While you were away" page (backlog D1): per session of the current runs what happened in the period, the supervisor's events in
/// order, the limits hit, and the sessions that were quiet. It only draws what the window gives it; the digest was built on a pool thread.
/// Nothing on the page is sent to a session.
/// </summary>
public sealed class AwayView : UserControl
{
    private const string Footer =
        "Counts, times and names from the transcripts and the supervisor's log on this computer. Other machines and claude.ai chat are not counted. " +
        "The last reply is cut to 300 characters. Nothing here is sent to a session.";

    private RailPalette _palette = RailPalette.DarkPalette;

    private readonly TextBlock _caption = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) };
    private readonly StackPanel _body = new();

    private readonly TextBlock _footer = new()
    {
        Text = Footer,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
    };

    /// <summary>"Copy as Markdown" (batch B2): copies the digest on the page. It does not dismiss the page. Off until a digest is shown.</summary>
    private readonly Button _copy = new()
    {
        Content = UiIcon.Label(UiIcon.Copy, "Copy as Markdown"),
        Padding = new Thickness(12, 4, 12, 4),
        Margin = new Thickness(8, 0, 0, 0),
        IsEnabled = false,
    };

    private readonly TextBlock _copyStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>The digest on the page, kept so that Copy as Markdown copies what is shown. Null when the page shows a problem.</summary>
    private AwayDigest? _digest;

    public AwayView()
    {
        var gotIt = new Button { Content = UiIcon.Label(UiIcon.Check, "Got it"), Padding = new Thickness(12, 4, 12, 4) };
        gotIt.Click += (_, _) => DismissRequested?.Invoke();
        _copy.Click += (_, _) => CopyMarkdown();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        actions.Children.Add(gotIt);
        actions.Children.Add(_copy);

        var root = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        // No page title here: the Insights tab's sub-tab row names the page (2026-11-18).
        root.Children.Add(_caption);
        root.Children.Add(actions);
        root.Children.Add(_copyStatus);
        root.Children.Add(_body);
        root.Children.Add(_footer);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>
    /// Copies the digest on the page as Markdown. The page stays open. Nothing is sent to a session; a busy clipboard is said in the status line,
    /// and the owner presses the button again.
    /// </summary>
    private void CopyMarkdown()
    {
        if (_digest is not { } digest) return;
        var text = AwayExport.Markdown(digest, digest.Since, digest.Now);
        try
        {
            Clipboard.SetText(text);
            _copyStatus.Text = "The digest is on the clipboard as Markdown. Nothing was sent to a session.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _copyStatus.Text = $"The clipboard is busy, so the digest was not copied ({ex.Message}). Press the button again.";
        }
    }

    /// <summary>The Got it button was pressed. The window records that the period was seen, and closes the page.</summary>
    public event Action? DismissRequested;

    /// <summary>Takes the rail's palette, as the other pages do: the text and the muted lines follow the theme.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Foreground = RailPalette.Brush(palette.Text);
        _footer.Foreground = RailPalette.Brush(palette.Muted);
        _copyStatus.Foreground = RailPalette.Brush(palette.Muted);
    }

    /// <summary>Draws the period: the sessions with activity, the supervisor's events, the limit hits, then the quiet sessions by name.</summary>
    public void Show(AwayDigest digest)
    {
        _digest = digest;
        _copy.IsEnabled = true;
        _copyStatus.Text = string.Empty;
        _body.Children.Clear();
        _caption.Text = $"From {LocalTime(digest.Since)} to {LocalTime(digest.Now)}.";

        if (!digest.HasNews) _body.Children.Add(Plain("Nothing happened in this period.", muted: true));

        if (digest.Rows.Count > 0)
        {
            _body.Children.Add(Heading("Sessions with activity, newest first"));
            foreach (var row in digest.Rows) AddRow(row);
        }

        if (digest.Events.Count > 0)
        {
            _body.Children.Add(Heading("Supervisor, oldest first"));
            foreach (var evt in digest.Events)
                _body.Children.Add(Mono($"{LocalTime(evt.At)}  {evt.Session}  {evt.Event}  {evt.Detail}".TrimEnd()));
        }

        if (digest.LimitHits.Count > 0)
        {
            _body.Children.Add(Heading("Limits reached, oldest first"));
            foreach (var hit in digest.LimitHits)
                _body.Children.Add(Mono($"{LocalTime(hit.Time)}  {LimitStatus.LabelOf(hit.Type)} limit, resets {LocalTime(hit.ResetsAt)}"));
        }

        if (digest.QuietSessions.Count > 0)
        {
            var names = digest.QuietSessions.Select(quiet => quiet.NeedsYou ? $"{quiet.Name} (needs you)" : quiet.Name);
            _body.Children.Add(Plain($"No activity in this period: {string.Join(", ", names)}.", muted: true, top: 12));
        }
    }

    /// <summary>Shows a line in place of the page's content, for a digest that could not be built.</summary>
    public void ShowProblem(string text)
    {
        _digest = null;
        _copy.IsEnabled = false;
        _copyStatus.Text = string.Empty;
        _caption.Text = text;
        _body.Children.Clear();
    }

    /// <summary>One session: its name, role and state, then its last activity, its files and its failed calls, then its last reply.</summary>
    private void AddRow(AwayRow row)
    {
        var mark = row.NeedsYou ? "  ·  needs you now" : string.Empty;
        _body.Children.Add(new TextBlock
        {
            Text = $"{row.Name}  ·  {row.Role.ToString().ToLowerInvariant()}  ·  {row.State}{mark}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

        var activity = row.LastActivity is { } at ? $"last activity {LocalTime(at)}" : "no transcript yet";
        var files = row.FilesWritten == 0
            ? "no files written"
            : $"{row.FilesWritten} {(row.FilesWritten == 1 ? "file" : "files")} written in the whole session: " +
              string.Join(", ", row.FirstFiles) +
              (row.FilesWritten > row.FirstFiles.Count ? $" and {row.FilesWritten - row.FirstFiles.Count} more" : string.Empty);
        var failed = $"{row.FailedToolCalls} failed tool {(row.FailedToolCalls == 1 ? "call" : "calls")}";
        _body.Children.Add(Mono($"{activity}  ·  {files}  ·  {failed}"));

        _body.Children.Add(Plain(row.LastText is null ? "No reply text in the transcript." : $"\"{row.LastText}\"", muted: true, left: 12));
    }

    private TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 2),
    };

    private TextBlock Mono(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
    };

    private TextBlock Plain(string text, bool muted, double top = 0, double left = 0)
    {
        var line = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(left, top, 0, 2),
        };
        if (muted) line.Foreground = RailPalette.Brush(_palette.Muted);
        return line;
    }

    private static string LocalTime(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

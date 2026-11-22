using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The read-only transcript of one session of a provider (parity P7, decisions T-D2 and T-D5). A header with the session's title, the message count and a Copy
/// button; the scope line (messages only); the messages in order, each with its label, its local time and its text (selectable and wrapped); and the cap
/// line at the end when the session is longer than the cap. It reads nothing itself: the window reads the session off the UI thread and gives it the
/// messages. Its colours follow the rail's theme. Claude's transcript view is a different control and is not changed.
/// </summary>
public sealed class ProviderTranscriptView : UserControl
{
    private readonly TextBlock _title = new()
    {
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private readonly TextBlock _count = new() { FontSize = 12, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };

    private readonly Button _copy = new() { Content = "Copy", Padding = new Thickness(12, 3, 12, 3), VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock _copyNote = new() { FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock _scope = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
        Text = ProviderTranscriptText.ScopeLine,
    };

    /// <summary>What shows in place of the messages: the reading line, the reason it could not be read, or "no messages". Null when messages show.</summary>
    private readonly TextBlock _notice = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    private readonly RichTextBox _body = new()
    {
        IsReadOnly = true,
        IsReadOnlyCaretVisible = true,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private readonly TextBlock _capNote = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    private RailPalette _palette = RailPalette.DarkPalette;
    private string _titleText = string.Empty;
    private string _countText = string.Empty;
    private string? _capText;
    private string? _noticeText;
    private IReadOnlyList<TranscriptLine> _lines = [];

    public ProviderTranscriptView()
    {
        _copy.Click += Copy_Click;

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(_count);
        header.Children.Add(_copy);
        header.Children.Add(_copyNote);

        var messages = new Grid();
        messages.Children.Add(_notice);
        messages.Children.Add(_body);

        var root = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        for (int row = 0; row < 5; row++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = row == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }
        AddRow(root, _title, 0);
        AddRow(root, header, 1);
        AddRow(root, _scope, 2);
        AddRow(root, messages, 3);
        AddRow(root, _capNote, 4);
        Content = root;
        ApplyTheme(_palette);
    }

    /// <summary>Colours the view from the rail's palette, and draws it again with those colours.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        _palette = palette;
        Render();
    }

    /// <summary>The tab shows while the session is read (T-D4).</summary>
    public void ShowReading(string title)
    {
        _titleText = title;
        _lines = [];
        _countText = string.Empty;
        _capText = null;
        _noticeText = "Reading the session…";
        _copyNote.Text = string.Empty;
        Render();
    }

    /// <summary>
    /// Shows the messages. <paramref name="shown"/> is the list the view holds (already capped, T-D5); <paramref name="total"/> is the session's
    /// full message count, which the header and the cap line report.
    /// </summary>
    public void ShowMessages(string title, IReadOnlyList<TranscriptLine> shown, int total)
    {
        _titleText = title;
        _lines = shown;
        _countText = ProviderTranscriptText.MessageCount(total);
        _capText = ProviderTranscriptText.CapNote(total, shown.Count);
        _noticeText = total == 0 ? "This session has no messages." : null;
        _copyNote.Text = string.Empty;
        Render();
    }

    /// <summary>A session that could not be read: the reason is shown in the tab, in place of the messages (T-D4).</summary>
    public void ShowProblem(string title, string reason)
    {
        _titleText = title;
        _lines = [];
        _countText = string.Empty;
        _capText = null;
        _noticeText = reason;
        _copyNote.Text = string.Empty;
        Render();
    }

    private void Render()
    {
        var text = RailPalette.Brush(_palette.Text);
        var muted = RailPalette.Brush(_palette.Muted);
        Background = RailPalette.Brush(_palette.Background);
        _title.Text = _titleText;
        _title.Foreground = text;
        _count.Text = _countText;
        _count.Foreground = muted;
        _copyNote.Foreground = muted;
        _copy.IsEnabled = _noticeText is null && _lines.Count > 0;
        _scope.Foreground = muted;
        _notice.Text = _noticeText ?? string.Empty;
        _notice.Foreground = text;
        _notice.Visibility = _noticeText is null ? Visibility.Collapsed : Visibility.Visible;
        _body.Visibility = _noticeText is null ? Visibility.Visible : Visibility.Collapsed;
        _body.Background = RailPalette.Brush(_palette.Background);
        _body.Foreground = text;
        _body.Document = BuildDocument();
        _capNote.Text = _capText ?? string.Empty;
        _capNote.Foreground = muted;
        _capNote.Visibility = _capText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The messages as one document: a paragraph per message, its label (the owner's in the find blue of the search view, the assistant's in the text colour),
    /// its local time, and its text with each line break kept.
    /// </summary>
    private FlowDocument BuildDocument()
    {
        var text = RailPalette.Brush(_palette.Text);
        var muted = RailPalette.Brush(_palette.Muted);
        var owner = RailPalette.Brush(_palette.IsLight ? "#0B5CAD" : "#9CDCFE");
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Background = RailPalette.Brush(_palette.Background),
            Foreground = text,
        };
        foreach (var line in _lines)
        {
            var owned = line.Kind == "you";
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
            paragraph.Inlines.Add(new Run(line.Kind) { FontWeight = FontWeights.SemiBold, Foreground = owned ? owner : text });
            var time = ProviderTranscriptText.TimeLabel(line.Time);
            if (time.Length > 0) paragraph.Inlines.Add(new Run("  " + time) { FontSize = 11, Foreground = muted });
            paragraph.Inlines.Add(new LineBreak());
            var parts = line.Text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run(parts[i]) { Foreground = text });
            }
            document.Blocks.Add(paragraph);
        }
        return document;
    }

    /// <summary>Copies the messages the view holds as plain text (T-D2). The clipboard may be busy: the note says so, and nothing else changes.</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_lines.Count == 0) return;
        try
        {
            Clipboard.SetText(ProviderTranscriptText.PlainText(_lines));
            _copyNote.Text = $"Copied {ProviderTranscriptText.MessageCount(_lines.Count)}.";
        }
        catch (ExternalException)
        {
            _copyNote.Text = "Not copied: the clipboard is busy. Try again.";
        }
    }

    private static void AddRow(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}

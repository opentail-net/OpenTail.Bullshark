using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>The effort report on the transcript page: its pending line, the loading and the paths. Moved out of TranscriptView.cs without a change to the code (2026-11-23).</summary>
public sealed partial class TranscriptView
{
    private void ShowEffortPending() =>
        _effort.Content = new TextBlock { Text = "Reading the transcript…", Foreground = Brush(_palette.Muted), Margin = new Thickness(0, 4, 0, 4) };

    /// <summary>
    /// Reads the effort of the transcript on a pool thread, then draws it. A read that fails is logged and shown as a line in the panel.
    /// A result for a transcript that is no longer shown is dropped.
    /// </summary>
    private async Task LoadEffortAsync(string path)
    {
        EffortReport report;
        try
        {
            report = await Task.Run(() => EffortReport.ReadFile(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"effort not read: {ex.GetType().Name}");
            if (_effortPath == path)
                _effort.Content = new TextBlock { Text = $"The transcript cannot be read: {ex.Message}", TextWrapping = TextWrapping.Wrap, Foreground = Brush(_palette.Muted) };
            return;
        }
        if (_effortPath == path) ShowEffort(report);
    }

    /// <summary>The panel: failed calls by tool, files read again, commands run again, and the turns that gave no output.</summary>
    private void ShowEffort(EffortReport report)
    {
        _effortReport = report;
        var panel = new StackPanel();
        panel.Children.Add(EffortCaption($"Failed tool calls: {report.FailedCalls:N0}"));
        foreach (var item in report.FailedByTool)
            panel.Children.Add(EffortJump($"{item.Label}: {item.Count:N0} failed", item.Line, null));

        // The same call failing again and again (features batch G3). The line is the first failed result, so a click shows where it began.
        panel.Children.Add(EffortCaption($"Same call failed in a row: {report.FailingLoops.Count:N0}"));
        foreach (var loop in report.FailingLoops)
            panel.Children.Add(EffortJump(
                $"Same call failed {loop.Count:N0} times in a row: {loop.Tool} (lines {loop.FirstLine:N0}-{loop.LastLine:N0})",
                loop.FirstLine,
                $"{EffortReport.LoopThreshold} or more failures of one call in a row, with no success of that call in between"));

        panel.Children.Add(EffortCaption($"Files read more than {EffortReport.RereadThreshold} times: {report.RereadFiles.Count:N0}"));
        foreach (var item in report.RereadFiles)
            panel.Children.Add(EffortPathJump(item.Label, $": read {item.Count:N0} times", item.Line));

        panel.Children.Add(EffortCaption($"Commands run more than {EffortReport.RepeatThreshold} times: {report.RepeatedCommands.Count:N0}"));
        foreach (var item in report.RepeatedCommands)
            panel.Children.Add(EffortJump($"{item.Label}: run {item.Count:N0} times", item.Line, null));

        panel.Children.Add(EffortCaption(
            $"Turns with no file written and no text answer: {report.TurnsWithoutOutput.Count:N0} of {report.Turns:N0}, " +
            $"{Tokens(report.TokensWithoutOutput)} tokens"));
        foreach (var turn in report.TurnsWithoutOutput)
            panel.Children.Add(EffortJump(
                $"{(turn.Prompt.Length == 0 ? "(before the first prompt)" : turn.Prompt)} · {Tokens(turn.Tokens)} tokens",
                turn.StartLine, null));

        var scroll = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
        // A file path is trimmed to the width the panel has, and again whenever that width changes (round 6 item 10). The fit waits until the
        // width has stopped changing for 150 ms, so a drag of the splitter fits once (round 8 item 10).
        scroll.SizeChanged += (_, _) => ScheduleEffortFit(scroll);
        _effort.Content = scroll;
    }

    /// <summary>What a file path in the panel is made of: the whole path (for the tooltip), and the text after it, which is never trimmed.</summary>
    private sealed record EffortPathText(string Full, string Suffix);

    /// <summary>
    /// One file in the panel. Its path is trimmed to the width when the panel is laid out: the file name always shows, and as much of the
    /// folder as fits, from the start trimmed (<c>…\folder\name.ext</c>). The full path is in the tooltip (round 6 item 10). The row is the
    /// keyboard row of the item (its text is the <see cref="TextBlock"/> inside it).
    /// </summary>
    private UIElement EffortPathJump(string full, string suffix, int line)
    {
        var row = EffortJump(full + suffix, line, full);
        row.Tag = new EffortPathText(full, suffix);
        return row;
    }

    /// <summary>The panel whose paths are fitted next, and the timer that fits them once the width has settled (round 8 item 10).</summary>
    private ScrollViewer? _effortFitScroll;
    private DispatcherTimer? _effortFitTimer;

    /// <summary>
    /// Fits the panel's paths 150 ms after its last size change: each change restarts the timer, so the measuring runs once a resize has settled,
    /// not on every step of it (round 8 item 10).
    /// </summary>
    private void ScheduleEffortFit(ScrollViewer scroll)
    {
        _effortFitScroll = scroll;
        if (_effortFitTimer is null)
        {
            _effortFitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _effortFitTimer.Tick += (_, _) =>
            {
                _effortFitTimer!.Stop();
                if (_effortFitScroll is { } current) FitEffortPaths(current);
            };
        }
        _effortFitTimer.Stop();
        _effortFitTimer.Start();
    }

    /// <summary>Trims the file paths of the panel to the width its scroll area has. Runs on the UI thread, after the panel is laid out.</summary>
    private void FitEffortPaths(ScrollViewer scroll)
    {
        if (scroll.Content is not StackPanel panel) return;
        // The width is the scroll area's without the scrollbar, whether or not one is shown: the text must not change with the scrollbar, which
        // the text's own height decides. The item is indented by its left margin of 10, and the text keeps a little room at its right edge.
        var width = scroll.ActualWidth - SystemParameters.VerticalScrollBarWidth - 10 - 4;
        var rows = panel.Children.OfType<Border>().Where(row => row.Tag is EffortPathText).ToList();
        var blocks = rows.Select(row => (TextBlock)row.Child).ToList();
        var items = rows.Select(row => (EffortPathText)row.Tag!).Select(text => (text.Full, text.Suffix)).ToList();
        // Only the first EffortReport.MaxFittedPaths are measured; the rest are shown whole, with the full path in the tooltip (round 8 item 10).
        var texts = EffortReport.FitPaths(items, (index, candidate) => TextWidth(candidate, blocks[index]) <= width);
        for (var i = 0; i < blocks.Count; i++) blocks[i].Text = texts[i];
    }

    /// <summary>The width of a line of text in the font of <paramref name="like"/>, at its display's pixel density.</summary>
    private double TextWidth(string text, TextBlock like)
    {
        var typeface = new Typeface(like.FontFamily, like.FontStyle, like.FontWeight, like.FontStretch);
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, like.FontSize, like.Foreground, VisualTreeHelper.GetDpi(like).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private TextBlock EffortCaption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(_palette.Text),
        Margin = new Thickness(0, 6, 0, 2),
    };

    /// <summary>
    /// One item of the panel. A click shows the transcript at its first line; so does Enter or Space on the item, which takes the keyboard in the
    /// order the panel lays it out (keyboard batch T2). The item is a keyboard row around its text.
    /// </summary>
    private Border EffortJump(string text, int line, string? tooltip)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = tooltip ?? $"Show line {line}",
            Foreground = Brush(_palette.Muted),
            Cursor = Cursors.Hand,
        };
        void Show()
        {
            if (_digest is { } digest) _ = ShowAsync(digest, line, _shownTitle);
        }
        block.MouseLeftButtonUp += (_, _) => Show();
        var row = KeyboardRow.Wrap(block, _palette, $"{text}, line {line}", Show);
        row.Margin = new Thickness(10, 1, 0, 1);
        return row;
    }
}

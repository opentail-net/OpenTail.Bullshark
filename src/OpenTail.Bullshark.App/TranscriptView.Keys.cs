using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The keys and the clipboard of the transcript view (keyboard batch T3 and find and jump batch T5). The view answers to its keys only while it has the
/// keyboard, and not while a text box in it has it. <c>[</c> and <c>]</c> go to the previous and next prompt, <c>{</c> and <c>}</c> to a failure,
/// <c>Alt+[</c> and <c>Alt+]</c> to a file write, <c>Ctrl+[</c> and <c>Ctrl+]</c> to a compaction. Ctrl+C copies the focused line. Nothing is sent to a
/// session: the clipboard is the only destination, and no transcript text is logged.
/// </summary>
public sealed partial class TranscriptView
{
    /// <summary>The full result of a line, shown above the transcript until it is closed (copy and full-result batch, T3).</summary>
    private readonly Border _fullPanel = new()
    {
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 0, 0, 6),
        Visibility = Visibility.Collapsed,
    };

    private readonly TextBlock _fullTitle = new() { FontWeight = FontWeights.SemiBold, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    private readonly TextBox _fullBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MaxHeight = 260,
        FontFamily = TerminalFont.Family,
        FontSize = TerminalFont.Size,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBlock _fullNote = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>The menu of a line, built when it opens, for the line under the pointer (or the focused one).</summary>
    private readonly ContextMenu _lineMenu = new();

    /// <summary>The keys that move through the marks: the kind, and whether forward. Null for any other key.</summary>
    private static (MarkKind Kind, bool Forward)? JumpFor(Key key, ModifierKeys modifiers) => (key, modifiers) switch
    {
        (Key.OemOpenBrackets, ModifierKeys.None) => (MarkKind.Prompt, false),
        (Key.OemCloseBrackets, ModifierKeys.None) => (MarkKind.Prompt, true),
        (Key.OemOpenBrackets, ModifierKeys.Shift) => (MarkKind.Failure, false),
        (Key.OemCloseBrackets, ModifierKeys.Shift) => (MarkKind.Failure, true),
        (Key.OemOpenBrackets, ModifierKeys.Alt) => (MarkKind.Write, false),
        (Key.OemCloseBrackets, ModifierKeys.Alt) => (MarkKind.Write, true),
        (Key.OemOpenBrackets, ModifierKeys.Control) => (MarkKind.Compaction, false),
        (Key.OemCloseBrackets, ModifierKeys.Control) => (MarkKind.Compaction, true),
        _ => null,
    };

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt combinations arrive as System keys, with the key itself in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenFindFromKeyboard();
            return;
        }

        // A text box in the view (the find box, the full result) keeps its own keys, so typing [ in it is typing.
        if (Keyboard.FocusedElement is TextBoxBase) return;

        if (key == Key.Escape && _findBarOpen)
        {
            e.Handled = true;
            CloseFindBar();
            return;
        }

        if (key == Key.C && modifiers == ModifierKeys.Control && Keyboard.FocusedElement is TextBlock { Tag: TranscriptLine focused })
        {
            e.Handled = true;
            _ = CopyLineAsync(focused);
            return;
        }

        if (JumpFor(key, modifiers) is { } jump)
        {
            e.Handled = true;
            _ = JumpAsync(jump.Kind, jump.Forward);
        }
    }

    /// <summary>
    /// Goes to the next or previous mark of a kind from the current line. Nothing moves when no mark lies that way, or when the marks of this file are
    /// not read yet (they come with the turn facts, a moment after the file is shown).
    /// </summary>
    private async Task JumpAsync(MarkKind kind, bool forward)
    {
        if (_digest is not { } digest || _marksPath != digest.FilePath) return;
        var lines = _marks.Of(kind);
        var current = CurrentLine();
        var target = forward ? TranscriptMarks.Next(lines, current) : TranscriptMarks.Previous(lines, current);
        if (target is { } line) await GoToLineAsync(line);
    }

    /// <summary>
    /// The line a jump starts from: the focused line, else the first line on screen. A jump from a line never lands on that same line.
    /// </summary>
    private int CurrentLine()
    {
        if (Keyboard.FocusedElement is TextBlock { Tag: TranscriptLine focused }) return focused.Number;
        TranscriptLine? last = null;
        foreach (var block in _transcript.Children.OfType<TextBlock>())
        {
            if (block.Tag is not TranscriptLine line) continue;
            last = line;
            // A block that is not under the scroll area in the visual tree (the view is not shown yet, or its template is not applied) has no place on
            // the screen to read: TransformToAncestor throws for it. Such a block is skipped, and the first line on screen is the one given.
            if (!_transcriptScroll.IsAncestorOf(block)) continue;
            var top = block.TransformToAncestor(_transcriptScroll).Transform(new Point(0, 0)).Y;
            if (top >= 0) return line.Number;
        }
        return last?.Number ?? _firstShown + 1;
    }

    /// <summary>
    /// Shows a line: it is focused and brought into view when it is on the page, else the page around it is read (the find state stays, as it is the
    /// same file).
    /// </summary>
    private async Task GoToLineAsync(int line)
    {
        if (_digest is not { } digest) return;
        if (BlockFor(line) is { } block)
        {
            SetFocusBlock(block);
            block.BringIntoView();
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await ShowAsync(digest, line, _shownTitle);
        // Timing only: the line number and the time the page took to draw (the jump's speed is checked from this line).
        AppLog.Write($"jump: line {line} drawn in {clock.ElapsedMilliseconds} ms");
    }

    private TextBlock? BlockFor(int line) =>
        _transcript.Children.OfType<TextBlock>().FirstOrDefault(block => block.Tag is TranscriptLine shown && shown.Number == line);

    /// <summary>The focused line: the one a search, a jump or a click went to. It keeps a background while it is the focus.</summary>
    private void SetFocusBlock(TextBlock block)
    {
        var previous = _focus;
        _focus = block;
        if (previous is not null && !ReferenceEquals(previous, block)) RefreshBackground(previous);
        RefreshBackground(block);
    }

    /// <summary>A line's background: the keyboard's selection while it has the keyboard, else the focus colour when it is the focus, else none.</summary>
    private void RefreshBackground(TextBlock block)
    {
        block.Background = block.IsKeyboardFocused
            ? Brush(_palette.Selection)
            : ReferenceEquals(block, _focus) ? Brush(_palette.IsLight ? "#CFE0F5" : "#2D3E50") : null;
    }

    /// <summary>
    /// The menu of a line. Built when the pointer or the keyboard opens it, for the line under it: "Copy this message" for a line that is not a result,
    /// "Copy command" for a Bash call, "Show full result" for a tool result (its preview is cut, so the full text is read again).
    /// </summary>
    private ContextMenu BuildLineMenu()
    {
        _transcript.ContextMenuOpening += OnLineMenuOpening;
        return _lineMenu;
    }

    private void OnLineMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var line = LineUnder(e.OriginalSource as DependencyObject);
        if (line is null)
        {
            e.Handled = true;
            return;
        }

        _lineMenu.Items.Clear();
        if (line.Kind == "result")
            _lineMenu.Items.Add(MenuItemFor("Show full result", () => _ = ShowFullResultAsync(line)));
        else
            _lineMenu.Items.Add(MenuItemFor("Copy this message", () => _ = CopyLineAsync(line)));
        if (line.Kind == "tool" && line.Text.StartsWith("▸ Bash", StringComparison.Ordinal))
            _lineMenu.Items.Add(MenuItemFor("Copy command", () => _ = CopyCommandAsync(line)));
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The transcript line a visual belongs to: its block, or null when the visual is not a line of the transcript.</summary>
    private static TranscriptLine? LineUnder(DependencyObject? start)
    {
        for (var current = start; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : null)
        {
            if (current is TextBlock { Tag: TranscriptLine line }) return line;
        }
        return null;
    }

    /// <summary>
    /// Copies a line: a message as written, or a tool result whole (read again from the file, capped). A busy clipboard is logged and the copy is not made.
    /// </summary>
    private async Task CopyLineAsync(TranscriptLine line)
    {
        var text = line.Kind == "result" ? (await ReadFullAsync(line))?.Text : TranscriptCopy.Text(line);
        if (text is not null) CopyText(text);
    }

    /// <summary>Copies the command of a Bash call line, read again from its record (the line shows only the start of it).</summary>
    private async Task CopyCommandAsync(TranscriptLine line)
    {
        var pager = _pager;
        if (pager is null) return;
        string? raw;
        try
        {
            raw = await Task.Run(() => pager.ReadRaw(line.Number));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"command not read: {ex.Message}");
            return;
        }
        if (raw is not null && TranscriptCopy.Command(raw) is { } command) CopyText(command);
        else AppLog.Write($"no command on line {line.Number}");
    }

    /// <summary>The whole text of a line's record, read again from the file, or null when it cannot be read.</summary>
    private async Task<TranscriptCopy.FullText?> ReadFullAsync(TranscriptLine line)
    {
        var pager = _pager;
        if (pager is null) return null;
        try
        {
            var raw = await Task.Run(() => pager.ReadRaw(line.Number));
            return raw is null ? null : TranscriptCopy.FullResult(raw);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"full result not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Shows a tool result whole in a read-only box above the transcript, with its length and the cap's note (T3).</summary>
    private async Task ShowFullResultAsync(TranscriptLine line)
    {
        var full = await ReadFullAsync(line);
        if (full is null) return;
        _fullTitle.Text = $"Full result, line {line.Number:N0} · {full.Length:N0} characters";
        _fullNote.Text = full.Truncated
            ? $"Shown: the first {TranscriptCopy.FullResultCap:N0} characters. The rest is in the transcript file."
            : "Shown in full.";
        _fullBox.Text = full.Text;
        _fullBox.ScrollToHome();
        _fullPanel.Visibility = Visibility.Visible;
    }

    /// <summary>The full-result panel: its title, a Copy button for the box's text, Close, the box and the note.</summary>
    private UIElement BuildFullResult()
    {
        var copy = new Button { Content = "Copy", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        copy.Click += (_, _) => CopyText(_fullBox.Text);
        var close = new Button { Content = "Close", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        close.Click += (_, _) =>
        {
            _fullPanel.Visibility = Visibility.Collapsed;
            Focus();
        };
        AutomationProperties.SetName(copy, "Copy the full result");
        AutomationProperties.SetName(close, "Close the full result");
        AutomationProperties.SetName(_fullBox, "Full result, read-only");

        var header = new DockPanel();
        DockPanel.SetDock(copy, Dock.Right);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(close);
        header.Children.Add(_fullTitle);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_fullBox);
        stack.Children.Add(_fullNote);
        _fullPanel.Child = stack;
        DockPanel.SetDock(_fullPanel, Dock.Top);
        return _fullPanel;
    }

    /// <summary>Colours the full-result panel from the palette.</summary>
    private void ApplyFullTheme(RailPalette palette)
    {
        _fullPanel.Background = Brush(palette.Panel);
        _fullPanel.BorderBrush = Brush(palette.Border);
        _fullTitle.Foreground = Brush(palette.Text);
        _fullNote.Foreground = Brush(palette.Muted);
    }

    /// <summary>Puts the text on the owner's clipboard. A busy clipboard is logged and the copy is not made. The text is never logged.</summary>
    private static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            AppLog.Write($"copy not made: {ex.GetType().Name}");
            return false;
        }
    }
}

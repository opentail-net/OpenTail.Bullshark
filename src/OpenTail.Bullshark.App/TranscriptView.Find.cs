using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Find in the shown transcript (find and jump batch, T4). Ctrl+F opens a bar at the top of the view; the query is searched in the whole file on a pool
/// thread, cancelled when the text changes, kept per file (its key: path, length, write time) and per query, and at most
/// <see cref="TranscriptFind.MaxHits"/> lines. Enter goes to the next hit, Shift+Enter to the previous, Escape closes the bar. The hits are highlighted in
/// the lines on screen, and the current one is the focused line. Nothing is logged but the failure's message: no transcript text.
/// </summary>
public sealed partial class TranscriptView
{
    private readonly DockPanel _findBar = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBox _findBox = new() { MinWidth = 160 };
    private readonly TextBlock _findCount = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), MinWidth = 96 };

    /// <summary>The hint line under the header: the keys the view answers to (jump batch, T5).</summary>
    private readonly TextBlock _hint = new()
    {
        Text = "[ ] prompts   { } failures   Alt+[ ] writes   Ctrl+[ ] compactions   Ctrl+F find",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Waits for a pause in the typing before the search starts, so each keystroke does not start one.</summary>
    private readonly DispatcherTimer _findDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private bool _findBarOpen;

    /// <summary>True while the box text is set by the view, so the text change does not start a search.</summary>
    private bool _suppressFindText;

    /// <summary>The file the find state belongs to. A jump on the same file keeps the state; another file starts it again.</summary>
    private string? _findPath;

    /// <summary>The file key the cached results were read for. A changed file drops them.</summary>
    private TranscriptFileKey? _findKey;

    /// <summary>The results of the queries read for <see cref="_findKey"/>, by trimmed query.</summary>
    private readonly Dictionary<string, FindResult> _findCache = new(StringComparer.Ordinal);

    /// <summary>The search running now, or null. Replaced or cancelled by a new text.</summary>
    private CancellationTokenSource? _findCancel;

    /// <summary>The query whose hits are in <see cref="_findHits"/> and highlighted in the lines. Empty when none.</summary>
    private string _findApplied = string.Empty;

    /// <summary>The hit lines of <see cref="_findApplied"/>, or null while it runs (or when there is no query).</summary>
    private IReadOnlyList<int>? _findHits;

    private bool _findCapped;

    /// <summary>The current hit, as an index into <see cref="_findHits"/>, or -1.</summary>
    private int _findIndex = -1;

    private UIElement BuildFindBar()
    {
        var close = new Button { Content = "Close", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        close.Click += (_, _) => CloseFindBar();
        var next = new Button { Content = "Next", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        next.Click += (_, _) => StepFind(forward: true);
        var previous = new Button { Content = "Previous", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        previous.Click += (_, _) => StepFind(forward: false);
        AutomationProperties.SetName(close, "Close the find bar");
        AutomationProperties.SetName(next, "Next match");
        AutomationProperties.SetName(previous, "Previous match");
        AutomationProperties.SetName(_findBox, "Find in this session");

        _findBox.TextChanged += (_, _) =>
        {
            if (!_suppressFindText) FindTextChanged();
        };
        _findBox.KeyDown += (_, e) => FindBoxKeyDown(e);
        _findDelay.Tick += (_, _) =>
        {
            _findDelay.Stop();
            _ = StartFindAsync(_findBox.Text, jump: true);
        };

        foreach (var element in new UIElement[] { close, next, previous, _findCount }) DockPanel.SetDock(element, Dock.Right);
        _findBar.Children.Add(close);
        _findBar.Children.Add(next);
        _findBar.Children.Add(previous);
        _findBar.Children.Add(_findCount);
        _findBar.Children.Add(_findBox);
        DockPanel.SetDock(_findBar, Dock.Top);
        return _findBar;
    }

    /// <summary>Shows the find bar. A query given (a search result passes its own) replaces the box text and starts the search again on this file.</summary>
    private void ShowFindBar(string? query = null)
    {
        _findBarOpen = true;
        _findBar.Visibility = Visibility.Visible;
        if (query is not null)
        {
            CancelFind();
            _findHits = null;
            _findIndex = -1;
            SetFindText(query);
        }
        UpdateFindCount();
    }

    /// <summary>
    /// Opens the find bar with a query on the shown file, and goes to its first hit as typing does (the test option --find, for the screenshots and the
    /// timing of a jump on a real transcript).
    /// </summary>
    public void OpenFind(string query)
    {
        ShowFindBar(query);
        if (_digest is not null) _ = StartFindAsync(query, jump: true);
    }

    /// <summary>Ctrl+F: opens the bar with the keyboard in its box, and searches the text already there.</summary>
    private void OpenFindFromKeyboard()
    {
        ShowFindBar();
        _findBox.Focus();
        _findBox.SelectAll();
        if (_findHits is null && _findCancel is null && _findBox.Text.Trim().Length > 0) _ = StartFindAsync(_findBox.Text, jump: false);
    }

    /// <summary>Closes the bar: the search stops, the highlight goes, and the keyboard returns to the transcript. The cached results stay for a reopen.</summary>
    private void CloseFindBar()
    {
        _findBarOpen = false;
        _findBar.Visibility = Visibility.Collapsed;
        _findDelay.Stop();
        CancelFind();
        _findApplied = string.Empty;
        _findHits = null;
        _findIndex = -1;
        RefreshAllBlocks();
        Focus();
    }

    private void SetFindText(string text)
    {
        _suppressFindText = true;
        try
        {
            _findBox.Text = text;
        }
        finally
        {
            _suppressFindText = false;
        }
    }

    /// <summary>A new file: its find starts again (the cache is per file key, so it is not kept across a change).</summary>
    private void ResetFindForFile(string path)
    {
        CancelFind();
        _findPath = path;
        _findApplied = string.Empty;
        _findHits = null;
        _findIndex = -1;
        _findCapped = false;
    }

    /// <summary>The text changed: the running search is cancelled, and the next one starts after a short pause.</summary>
    private void FindTextChanged()
    {
        CancelFind();
        _findHits = null;
        _findIndex = -1;
        UpdateFindCount();
        _findDelay.Stop();
        _findDelay.Start();
    }

    private void CancelFind()
    {
        _findCancel?.Cancel();
        _findCancel = null;
    }

    /// <summary>
    /// Finds the query in the shown file. The file is read on a pool thread; a result is kept for the file key and the query. A result that arrives after
    /// the text changed, or for another file, is dropped. With <paramref name="jump"/>, the view goes to the current hit.
    /// </summary>
    private async Task StartFindAsync(string text, bool jump)
    {
        CancelFind();
        var query = text.Trim();
        if (_digest is not { } digest)
        {
            UpdateFindCount();
            return;
        }
        if (query.Length == 0)
        {
            _findApplied = string.Empty;
            _findHits = null;
            _findIndex = -1;
            RefreshAllBlocks();
            UpdateFindCount();
            return;
        }

        var path = digest.FilePath;
        var cancel = new CancellationTokenSource();
        _findCancel = cancel;
        TranscriptFileKey key;
        try
        {
            key = TranscriptFileKey.Of(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"find not run: {ex.Message}");
            _findCancel = null;
            _findCount.Text = "The transcript cannot be read.";
            return;
        }
        if (_findKey != key)
        {
            _findCache.Clear();
            _findKey = key;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (!_findCache.TryGetValue(query, out var result))
        {
            UpdateFindCount();
            try
            {
                result = await Task.Run(() => TranscriptFind.Run(TranscriptFile.ReadLines(path), query, cancel.Token), cancel.Token);
                // Timing only: the length of the query and the count, never the text (the find's speed is checked from this line).
                AppLog.Write($"find: {result.Lines.Count} hit lines{(result.Capped ? " (capped)" : string.Empty)} for a query of {query.Length} characters in {clock.ElapsedMilliseconds} ms");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"find not run: {ex.Message}");
                if (ReferenceEquals(_findCancel, cancel))
                {
                    _findCancel = null;
                    _findCount.Text = "The transcript cannot be read.";
                }
                return;
            }
            if (cancel.IsCancellationRequested) return;
            _findCache[query] = result;
        }

        // A newer text, or another file shown since, owns the find now.
        if (!ReferenceEquals(_findCancel, cancel) || _digest?.FilePath != path) return;
        _findCancel = null;
        _findApplied = query;
        _findHits = result.Lines;
        _findCapped = result.Capped;
        _findIndex = PickHitIndex();
        UpdateFindCount();
        RefreshAllBlocks();
        if (jump && _findIndex >= 0) await GoToHitAsync();
    }

    /// <summary>The first hit at or after the focused line (or the first line on screen), or the first hit when none is after it.</summary>
    private int PickHitIndex()
    {
        if (_findHits is not { Count: > 0 } hits) return -1;
        var from = _focus?.Tag is TranscriptLine focused ? focused.Number : CurrentLine();
        for (var i = 0; i < hits.Count; i++)
        {
            if (hits[i] >= from) return i;
        }
        return 0;
    }

    /// <summary>Goes to the next or the previous hit, wrapping at the ends.</summary>
    private void StepFind(bool forward)
    {
        if (_findHits is not { Count: > 0 } hits) return;
        _findIndex = forward ? (_findIndex + 1) % hits.Count : (_findIndex - 1 + hits.Count) % hits.Count;
        UpdateFindCount();
        _ = GoToHitAsync();
    }

    private Task GoToHitAsync() => _findHits is { } hits && _findIndex >= 0 && _findIndex < hits.Count ? GoToLineAsync(hits[_findIndex]) : Task.CompletedTask;

    private void FindBoxKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                if (_findDelay.IsEnabled)
                {
                    // The text was typed a moment ago: the search runs now, and goes to the first hit.
                    _findDelay.Stop();
                    _ = StartFindAsync(_findBox.Text, jump: true);
                }
                else
                {
                    StepFind(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                }
                break;

            case Key.Escape:
                e.Handled = true;
                CloseFindBar();
                break;
        }
    }

    /// <summary>The count beside the box: "3 of 17", "2,000 or more", "No matches", "Searching…", or nothing for no query.</summary>
    private void UpdateFindCount()
    {
        _findCount.Text = _digest is null || _findBox.Text.Trim().Length == 0
            ? string.Empty
            : _findHits is null
                ? "Searching…"
                : _findHits.Count == 0
                    ? "No matches"
                    : $"{_findIndex + 1:N0} of {_findHits.Count:N0}" + (_findCapped ? " or more" : string.Empty);
    }

    /// <summary>Draws the highlight again in every line on screen, from the query applied to the file.</summary>
    private void RefreshAllBlocks()
    {
        foreach (var block in _transcript.Children.OfType<TextBlock>())
        {
            if (block.Tag is TranscriptLine line) ApplyLine(block, line);
        }
    }

    private void ApplyPanelTheme(RailPalette palette)
    {
        _hint.Foreground = Brush(palette.Muted);
        _findCount.Foreground = Brush(palette.Muted);
        ApplyFullTheme(palette);
    }
}

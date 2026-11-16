using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The history search in the window (docs/plans/history-search.md). Typing in the box above the rail starts a search 400 ms after the
/// last key; Enter starts it at once; Escape clears the box. The results open in their own tab, newest first. Clearing the box closes
/// the tab. A result opens its session's transcript at the line that matched, as a history row does.
/// </summary>
public partial class SessionWindow
{
    private const string SearchKey = "search";
    private readonly SearchView _search = new() { Visibility = Visibility.Hidden };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private CancellationTokenSource? _searchCancel;

    private void InitSearch()
    {
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            StartSearch(SearchBox.Text);
        };
        _search.OpenRequested += (digest, line) => _ = OpenTranscriptAtAsync(digest, line);
    }

    /// <summary>Typing waits for a pause. A cleared box closes the results tab.</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchDelay.Stop();
        if (SearchBox.Text.Trim().Length == 0)
        {
            CloseSearchTab();
            return;
        }
        _searchDelay.Start();
    }

    /// <summary>Enter starts the search at once. Escape clears the box.</summary>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _searchDelay.Stop();
            StartSearch(SearchBox.Text);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SearchBox.Clear();
        }
    }

    private void StartSearch(string text)
    {
        var query = text.Trim();
        if (query.Length == 0) return;

        if (!HistorySearch.IsUsable(query))
        {
            SetStatus("Each word of a search needs three letters or more.");
            if (_tabs.Any(t => t.Kind == TabKind.Search))
                _search.ShowMessage(query, "Each word needs three letters or more. Shorter words match almost every message.");
            return;
        }
        if (_history is null)
        {
            SetStatus("The history is still being read. Search again in a moment.");
            return;
        }

        _searchCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _searchCancel = cancel;

        var tab = TabFor(TabKind.Search, SearchKey, () => string.Empty);
        tab.Title = "Search: " + ShortenForTab(query, 28);
        SetActive(tab);
        ShowOnly(_search);
        _search.ShowSearching(query);
        _ = RunSearchAsync(query, _history.ToList(), cancel);
    }

    /// <summary>The search runs off the UI thread. A newer search, or closing the tab, cancels it, and its result is then not shown.</summary>
    private async Task RunSearchAsync(string query, List<SessionDigest> sessions, CancellationTokenSource cancel)
    {
        try
        {
            var results = await Task.Run(() => HistorySearch.Run(sessions, query, cancellationToken: cancel.Token), cancel.Token);
            if (cancel.IsCancellationRequested) return;

            _search.ShowResults(query, results);
            SetStatus($"{results.Count} session(s) say every word of \"{query}\". Newest first." +
                      (results.Count == HistorySearch.MaxResults ? $" The first {HistorySearch.MaxResults} are shown." : string.Empty));
        }
        catch (OperationCanceledException)
        {
            // A newer search or closing the tab replaced this one.
        }
        catch (Exception ex)
        {
            AppLog.Write($"search failed: {ex.Message}");
            SetStatus($"Search failed: {ex.Message}");
        }
    }

    /// <summary>Closes the results tab and stops its search, if one is open or running.</summary>
    private void CloseSearchTab()
    {
        _searchCancel?.Cancel();
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.Search);
        if (tab is not null) CloseTab(tab);
    }

    /// <summary>A result opens its session's transcript at the matching line, in the transcript tab.</summary>
    private async Task OpenTranscriptAtAsync(SessionDigest digest, int line)
    {
        var tab = TabFor(TabKind.Transcript, digest.SessionId, () => $"Session {ShortId(digest.SessionId)}");
        SetActive(tab);
        ShowOnly(_transcript);
        await _transcript.ShowAsync(digest, line);
    }

    private static string ShortenForTab(string text, int length) =>
        text.Length <= length ? text : text[..length] + "…";
}

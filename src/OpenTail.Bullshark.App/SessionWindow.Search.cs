using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The history search in the window (docs/plans/history-search.md). Typing in the box at the foot of the rail starts a search 400 ms after
/// the last key; Enter starts it at once; Escape clears the box. While a search runs the list stays as it is, and an hourglass shows. When
/// it ends, the history list shows only the sessions that matched, each with a count of its matching messages, and the results tab gives
/// their matching messages. Live sessions are not filtered. Clearing the box, or closing the results tab, shows the whole list again.
/// </summary>
public partial class SessionWindow
{
    private const string SearchKey = "search";
    private readonly SearchView _search = new() { Visibility = Visibility.Hidden };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private CancellationTokenSource? _searchCancel;

    /// <summary>The sessions the last search found, by session id. Null when the history is not filtered.</summary>
    private Dictionary<string, SessionMatch>? _searchMatches;

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
            // Too short to search: the whole list is shown again, and the results tab says why.
            SetStatus("Each word of a search needs three letters or more. The whole list is shown.");
            if (_tabs.Any(t => t.Kind == TabKind.Search))
                _search.ShowMessage(query, "Each word needs three letters or more. Shorter words match almost every message.");
            ClearSearchFilter();
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

        // The list stays as it is while the search runs. The hourglass shows that it is running.
        SetBusy(true);
        SetStatus($"Searching for \"{query}\"…");
        _ = RunSearchAsync(query, _history.ToList(), cancel);
    }

    /// <summary>The search runs off the UI thread. A newer search, or closing the tab, cancels it, and its result is then not shown.</summary>
    private async Task RunSearchAsync(string query, List<SessionDigest> sessions, CancellationTokenSource cancel)
    {
        try
        {
            // The marks are read from this snapshot: the search runs on a pool thread while the rail may replace them (D-L7).
            var marks = _marks;
            var results = await Task.Run(() => HistorySearch.Run(sessions, query, cancellationToken: cancel.Token,
                extraText: digest => SearchableText(marks, digest.SessionId)), cancel.Token);
            if (cancel.IsCancellationRequested) return;

            var matches = new Dictionary<string, SessionMatch>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in results) matches[match.Session.SessionId] = match;
            _searchMatches = matches;
            SetBusy(false);

            _search.ShowResults(query, results);
            RefreshRail();
            var total = sessions.Count(s => !s.IsSubagent);
            SetStatus($"{results.Count} of {total} sessions say every word of \"{query}\". Newest first." +
                      (results.Count == HistorySearch.MaxResults ? $" The first {HistorySearch.MaxResults} are shown." : string.Empty));
        }
        catch (OperationCanceledException)
        {
            // A newer search or closing the tab replaced this one.
        }
        catch (Exception ex)
        {
            SetBusy(false);
            AppLog.Write($"search failed: {ex.Message}");
            SetStatus($"Search failed: {ex.Message}");
        }
    }

    /// <summary>Stops a running search and shows the whole history again.</summary>
    private void ClearSearchFilter()
    {
        _searchCancel?.Cancel();
        SetBusy(false);
        if (_searchMatches is null) return;

        _searchMatches = null;
        RefreshRail();
    }

    /// <summary>Closes the results tab. Closing it shows the whole history again.</summary>
    private void CloseSearchTab()
    {
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.Search);
        if (tab is not null) CloseTab(tab);
        else ClearSearchFilter();
    }

    /// <summary>A result opens its session's transcript at the matching line, in the transcript tab. The session's row is selected in the rail too.</summary>
    private async Task OpenTranscriptAtAsync(SessionDigest digest, int line)
    {
        var tab = TabFor(TabKind.Transcript, digest.SessionId, () => $"Session {ShortId(digest.SessionId)}");
        SetActive(tab);
        ShowOnly(_transcript);
        SelectHistoryRow(digest);
        await _transcript.ShowAsync(digest, line, DisplayTitle(digest));
        ShowLineageFor(digest);
    }

    /// <summary>
    /// Selects a history row in the rail without the selection opening the row's transcript: that would load it again from the top and lose
    /// the line the search found. Does nothing when the row is not in the rail (for example, under a folder that is collapsed).
    /// </summary>
    private void SelectHistoryRow(SessionDigest digest)
    {
        var item = FindItem(Rail.Items, new RailTag(RailKind.History, digest.SessionId));
        if (item is null) return;

        _rebuilding = true;
        try
        {
            item.IsSelected = true;
        }
        finally
        {
            _rebuilding = false;
        }
        _selected = (RailTag)item.Tag;
        item.BringIntoView();
    }

    /// <summary>True when a history session is shown: every one, unless a search filters the history.</summary>
    private bool IsShownInHistory(SessionDigest digest) =>
        _searchMatches is null || _searchMatches.ContainsKey(digest.SessionId);

    /// <summary>The match of a session in the last search, or null when it did not match or there is no search.</summary>
    private SessionMatch? MatchOf(string sessionId) =>
        _searchMatches is not null && _searchMatches.TryGetValue(sessionId, out var match) ? match : null;

    /// <summary>The hourglass at the right of the search box, shown while a search runs.</summary>
    private void SetBusy(bool busy) => SearchBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

    private static string ShortenForTab(string text, int length) =>
        text.Length <= length ? text : text[..length] + "…";
}

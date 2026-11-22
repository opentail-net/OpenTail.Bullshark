using System.Windows;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The search of past Codex sessions in the window (parity P6, first step, decisions S6-D1 to S6-D6). Under Codex the box at the foot of the rail searches
/// the Codex history by title, name, tags and note, in memory: no rollout is read and no message text is searched. The results open in a results tab; a
/// click or Enter on one opens its Codex transcript, and does not resume it. Claude's search (<see cref="SessionWindow.StartSearch"/>) is not changed: the
/// box calls this only while Codex is the window's provider.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The placeholder of the box under Codex (S6-D5). Claude's placeholder is in the XAML and is not changed.</summary>
    private const string CodexSearchHintText = "Titles, names, tags and notes. Message text comes later.";

    /// <summary>The key of the Codex results tab. One such tab is open at a time.</summary>
    private const string CodexSearchKey = "codex-search";

    /// <summary>The results of a Codex search, shown in the Codex results tab (<see cref="TabKind.CodexSearchResults"/>).</summary>
    private readonly CodexSearchView _codexSearch = new() { Visibility = Visibility.Hidden };

    /// <summary>True once the <c>--codex-search</c> screenshot has set the box, so it is set once.</summary>
    private bool _codexSearchShotSet;

    /// <summary>Puts the Codex results view in the pane and connects it: a result opens its Codex transcript (S6-D4).</summary>
    private void InitCodexSearch()
    {
        Pane.Children.Add(_codexSearch);
        _codexSearch.OpenRequested += session => OpenCodexTranscript(session);
    }

    /// <summary>
    /// Searches the Codex history for the box's text (S6-D1, S6-D2). The sessions whose title, name, tags or note hold every word of the query are
    /// shown in the Codex results tab, newest first. Enter and the pause after typing both come here, as they reach Claude's search. The rail is not
    /// changed, and nothing is written.
    /// </summary>
    private void StartCodexSearch(string text)
    {
        var query = text.Trim();
        if (query.Length == 0) return;
        if (!_codexRead)
        {
            SetStatus(_codexFailed
                ? "The Codex history could not be read, so there is nothing to search. Reload it to try again."
                : "The Codex history is still being read. Search again in a moment.");
            return;
        }

        var rows = CodexSearchRows(query);
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.CodexSearchResults)
            ?? AddTab(TabKind.CodexSearchResults, CodexSearchKey, string.Empty, ProviderId.Codex);
        tab.Title = "Codex search: " + ShortenForTab(query, 28);
        SetActive(tab);
        ShowOnly(_codexSearch);
        _codexSearch.ShowResults(query, rows);
        // The log holds the counts only: the query, the titles, the names, the tags and the notes stay out of it (S6-D1).
        AppLog.Write($"codex search: {rows.Count} of {_codexSessions.Count} sessions match");
        SetStatus(rows.Count == 0
            ? $"No Codex session matches \"{query}\"."
            : $"{rows.Count} of {_codexSessions.Count} Codex sessions match \"{query}\". Newest first.");
    }

    /// <summary>
    /// The Codex sessions a query matches, newest first, as the results view's rows (S6-D1). Archived and pinned sessions are searched too (S6-D2). Each
    /// row's title, folder and time are those of the Codex row. Subagent sessions are searched as the rail lists them.
    /// </summary>
    private List<CodexSearchRow> CodexSearchRows(string query) =>
        _codexSessions
            .Select(session => (session, mark: MarkOf(ProviderId.Codex, session.Key.SessionId)))
            .Where(pair => CodexSearch.Matches(query, pair.session.Title, pair.mark?.Name, pair.mark?.Tags, pair.mark?.Note))
            .OrderByDescending(pair => pair.session.LastActivity ?? DateTimeOffset.MinValue)
            .Select(pair => new CodexSearchRow(
                pair.session,
                CodexRowTitle(pair.mark?.Name, pair.session),
                string.IsNullOrEmpty(pair.session.Folder) ? "No folder named" : LeafOf(pair.session.Folder),
                string.IsNullOrEmpty(pair.session.Folder) ? null : pair.session.Folder,
                RelativeTime(pair.session.LastActivity, _behaviour.State.ClampClockBackwards),
                pair.mark?.Archived == true))
            .ToList();

    /// <summary>Closes the Codex results tab, when the box is cleared under Codex. Claude's results tab is left as it is.</summary>
    private void CloseCodexSearchTab()
    {
        if (_tabs.FirstOrDefault(t => t.Kind == TabKind.CodexSearchResults) is { } tab) CloseTab(tab);
    }

    /// <summary>
    /// The screenshot option (<c>--codex-search</c>, test only): once the Codex history has loaded, the box is set to its text, so the search runs and its
    /// results show (parity P6). Nothing else sets it, and it is set once.
    /// </summary>
    private void OpenCodexSearchForShot()
    {
        var text = _options.CodexSearchText;
        if (text is null || _codexSearchShotSet) return;
        _codexSearchShotSet = true;
        SearchBox.Text = text;
    }
}

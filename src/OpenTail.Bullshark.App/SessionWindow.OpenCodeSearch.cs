using System.Windows;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The search of past opencode sessions in the window (OC2, copied from the Copilot search partial, parity P6). Under opencode the box at the foot of the rail
/// searches the opencode history by title, name, tags and note, in memory: no message text is searched and no row is written. The results open in a results
/// tab; a click or Enter on one opens its opencode transcript, and does not resume it. The other providers' search is not changed.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The placeholder of the box under opencode. It is the Copilot search's wording: the same fields are searched.</summary>
    private const string OpenCodeSearchHintText = "Titles, names, tags and notes. Message text comes later.";

    /// <summary>The key of the opencode results tab. One such tab is open at a time.</summary>
    private const string OpenCodeSearchKey = "opencode-search";

    /// <summary>The results of an opencode search, shown in the opencode results tab (<see cref="TabKind.CopilotSearchResults"/>, see the note there).</summary>
    private readonly ProviderSearchView _openCodeSearch = new ProviderSearchView("opencode") { Visibility = Visibility.Hidden };

    /// <summary>Puts the opencode results view in the pane and connects it: a result opens its opencode transcript.</summary>
    private void InitOpenCodeSearch()
    {
        Pane.Children.Add(_openCodeSearch);
        _openCodeSearch.OpenRequested += session => OpenCodeTranscriptTab(session);
    }

    /// <summary>
    /// Searches the opencode history for the box's text. The sessions whose title, name, tags or note hold every word of the query are shown in the opencode
    /// results tab, newest first. The rail is not changed, and nothing is written.
    /// </summary>
    private void StartOpenCodeSearch(string text)
    {
        var query = text.Trim();
        if (query.Length == 0) return;
        if (!_openCodeRead)
        {
            SetStatus(OpenCodeRailText.SearchNotRead(_openCodeFailed));
            return;
        }

        var rows = OpenCodeSearchRows(query);
        // The tab is found by its provider as well as its kind: a Copilot results tab is not reused for opencode.
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.CopilotSearchResults && t.Provider == ProviderId.OpenCode)
            ?? AddTab(TabKind.CopilotSearchResults, OpenCodeSearchKey, string.Empty, ProviderId.OpenCode);
        tab.Title = "opencode search: " + ShortenForTab(query, 28);
        SetActive(tab);
        ShowOnly(_openCodeSearch);
        _openCodeSearch.ShowResults(query, rows);
        // The log holds the counts only: the query, the titles, the names, the tags and the notes stay out of it.
        AppLog.Write($"opencode search: {rows.Count} of {_openCodeSessions.Count} sessions match");
        SetStatus(rows.Count == 0
            ? OpenCodeRailText.SearchNoMatch(query)
            : OpenCodeRailText.SearchMatches(rows.Count, _openCodeSessions.Count, query));
    }

    /// <summary>
    /// The opencode sessions a query matches, newest first, as the results view's rows. Archived and pinned sessions are searched too.
    /// </summary>
    private List<ProviderSearchRow> OpenCodeSearchRows(string query) =>
        _openCodeSessions
            .Select(session => (session, mark: OpenCodeMarkOf(session)))
            .Where(pair => ProviderSearch.Matches(query, pair.session.Title, pair.mark?.Name, pair.mark?.Tags, pair.mark?.Note))
            .OrderByDescending(pair => pair.session.LastActivity ?? DateTimeOffset.MinValue)
            .Select(pair => new ProviderSearchRow(
                pair.session,
                OpenCodeRowTitle(pair.mark?.Name, pair.session),
                string.IsNullOrEmpty(pair.session.Folder) ? "No folder named" : LeafOf(pair.session.Folder),
                string.IsNullOrEmpty(pair.session.Folder) ? null : pair.session.Folder,
                RelativeTime(pair.session.LastActivity, _behaviour.State.ClampClockBackwards),
                pair.mark?.Archived == true))
            .ToList();

    /// <summary>Closes the opencode results tab, when the box is cleared under opencode. The other providers' results tabs are left as they are.</summary>
    private void CloseOpenCodeSearchTab()
    {
        if (_tabs.FirstOrDefault(t => t.Kind == TabKind.CopilotSearchResults && t.Provider == ProviderId.OpenCode) is { } tab) CloseTab(tab);
    }
}

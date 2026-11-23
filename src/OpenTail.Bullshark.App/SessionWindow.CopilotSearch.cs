using OpenTail.Bullshark.Core.Copilot;
using System.Windows;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The search of past Copilot sessions in the window (G2.3, copied from the Codex search partial, parity P6). Under Copilot the box at the foot of the rail
/// searches the Copilot history by title, name, tags and note, in memory: no session file is read and no message text is searched. The results open in a
/// results tab; a click or Enter on one opens its Copilot transcript, and does not resume it. Claude's and Codex's search are not changed.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The placeholder of the box under Copilot. Claude's placeholder is in the XAML and is not changed.</summary>
    private const string CopilotSearchHintText = "Titles, names, tags and notes. Message text comes later.";

    /// <summary>The key of the Copilot results tab. One such tab is open at a time.</summary>
    private const string CopilotSearchKey = "copilot-search";

    /// <summary>The results of a Copilot search, shown in the Copilot results tab (<see cref="TabKind.CopilotSearchResults"/>).</summary>
    private readonly ProviderSearchView _copilotSearch = new ProviderSearchView("Copilot") { Visibility = Visibility.Hidden };

    /// <summary>The message texts the Copilot search has read (PP-D4), by session. Read once per activity time.</summary>
    private readonly MessageTextCache _copilotMessages = new();

    /// <summary>Counts the Copilot searches, so a message read that ends for an older query does not replace the newer results (PP-D4).</summary>
    private int _copilotSearchVersion;

    /// <summary>Puts the Copilot results view in the pane and connects it: a result opens its Copilot transcript.</summary>
    private void InitCopilotSearch()
    {
        Pane.Children.Add(_copilotSearch);
        _copilotSearch.OpenRequested += session => OpenCopilotTranscript(session);
    }

    /// <summary>
    /// Searches the Copilot history for the box's text. The sessions whose title, name, tags or note hold every word of the query are shown in the Copilot
    /// results tab, newest first. The rail is not changed, and nothing is written.
    /// </summary>
    private void StartCopilotSearch(string text)
    {
        var query = text.Trim();
        if (query.Length == 0) return;
        if (!_copilotRead)
        {
            SetStatus(_copilotFailed
                ? "The Copilot history could not be read, so there is nothing to search. Reload it to try again."
                : "The Copilot history is still being read. Search again in a moment.");
            return;
        }

        var version = ++_copilotSearchVersion;
        var known = _copilotMessages.Known(_copilotSessions);
        var rows = CopilotSearchRows(query, known);
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.CopilotSearchResults)
            ?? AddTab(TabKind.CopilotSearchResults, CopilotSearchKey, string.Empty, ProviderId.Copilot);
        tab.Title = "Copilot search: " + ShortenForTab(query, 28);
        SetActive(tab);
        ShowOnly(_copilotSearch);
        _copilotSearch.ShowResults(query, rows);
        ReportCopilotSearch(query, rows.Count);
        // Sessions whose messages are not read yet are read now; the results are then shown again with their message matches (PP-D4).
        if (known.Count < _copilotSessions.Count) _ = CompleteCopilotSearchAsync(query, version);
    }

    /// <summary>Shows the message matches of a Copilot search once every session's messages are read, when the query is still the newest one (PP-D4).</summary>
    private async Task CompleteCopilotSearchAsync(string query, int version)
    {
        var texts = await _copilotMessages.LoadAllAsync(_copilotSessions, _copilot ??= new CopilotProvider(), CancellationToken.None);
        if (version != _copilotSearchVersion || !_tabs.Any(t => t.Kind == TabKind.CopilotSearchResults)) return;
        var rows = CopilotSearchRows(query, texts);
        _copilotSearch.ShowResults(query, rows);
        ReportCopilotSearch(query, rows.Count);
    }

    /// <summary>The log holds the counts only; the status line names the query.</summary>
    private void ReportCopilotSearch(string query, int matches)
    {
        AppLog.Write($"copilot search: {matches} of {_copilotSessions.Count} sessions match");
        SetStatus(matches == 0
            ? $"No Copilot session matches \"{query}\"."
            : $"{matches} of {_copilotSessions.Count} Copilot sessions match \"{query}\". Newest first.");
    }

    /// <summary>
    /// The Copilot sessions a query matches, newest first, as the results view's rows. Archived and pinned sessions are searched too.
    /// </summary>
    private List<ProviderSearchRow> CopilotSearchRows(string query, IReadOnlyDictionary<string, IReadOnlyList<string>> texts) =>
        _copilotSessions
            .Select(session => (session, mark: MarkOf(ProviderId.Copilot, session.Key.SessionId)))
            .Where(pair => ProviderSearch.Matches(query, pair.session.Title, pair.mark?.Name, pair.mark?.Tags, pair.mark?.Note,
                texts.TryGetValue(pair.session.Key.SessionId, out var messages) ? messages : null))
            .OrderByDescending(pair => pair.session.LastActivity ?? DateTimeOffset.MinValue)
            .Select(pair => new ProviderSearchRow(
                pair.session,
                CopilotRowTitle(pair.mark?.Name, pair.session),
                string.IsNullOrEmpty(pair.session.Folder) ? "No folder named" : LeafOf(pair.session.Folder),
                string.IsNullOrEmpty(pair.session.Folder) ? null : pair.session.Folder,
                RelativeTime(pair.session.LastActivity, _behaviour.State.ClampClockBackwards),
                pair.mark?.Archived == true))
            .ToList();

    /// <summary>Closes the Copilot results tab, when the box is cleared under Copilot. Claude's and Codex's results tabs are left as they are.</summary>
    private void CloseCopilotSearchTab()
    {
        if (_tabs.FirstOrDefault(t => t.Kind == TabKind.CopilotSearchResults) is { } tab) CloseTab(tab);
    }
}

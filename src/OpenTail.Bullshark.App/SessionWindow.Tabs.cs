using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The tab strip across the top (owner, 2026-11-15; D-S39). One tab per live session, one setup tab, and one tab per transcript.
/// "+" is always the last item, as in Windows Terminal. The tabs are kept quiet: the active tab takes the content's colour, the
/// others sit on the strip, and the text is small. Clicking a tab selects its rail item, and selecting a rail item opens or shows
/// its tab, so the two always agree. The ✕ on a session tab asks first: hide the tab, or stop the session as well (ClosePrompt).
/// </summary>
public partial class SessionWindow
{
    private enum TabKind
    {
        Session,
        Setup,
        Transcript,
        Search,
    }

    private sealed class OpenTab(TabKind kind, string key, string title)
    {
        public TabKind Kind { get; } = kind;

        /// <summary>The session id for session and transcript tabs; "setup" for the setup tab.</summary>
        public string Key { get; } = key;

        public string Title { get; set; } = title;

        /// <summary>The session's state, for the small dot. Empty for the setup and transcript tabs.</summary>
        public string State { get; set; } = string.Empty;
    }

    private readonly List<OpenTab> _tabs = new();

    /// <summary>Sessions whose tab the owner closed. Their tab stays closed until the owner selects the session in the rail.</summary>
    private readonly HashSet<string> _hiddenSessions = new(StringComparer.Ordinal);
    private OpenTab? _activeTab;

    /// <summary>Called when the rail selection changes: a session or a transcript gets its tab, and a folder shows the setup tab.</summary>
    private void NoteRailSelection(RailTag tag)
    {
        switch (tag.Kind)
        {
            case RailKind.Session:
                _hiddenSessions.Remove(tag.Key);
                SetActive(TabFor(TabKind.Session, tag.Key, () => TitleOfSession(tag.Key)));
                break;

            case RailKind.History:
                SetActive(TabFor(TabKind.Transcript, tag.Key, () => $"Session {ShortId(tag.Key)}"));
                break;

            case RailKind.Folder:
                OpenSetupTab(tag.Detail);
                break;
        }
    }

    /// <summary>The "+" tab: the setup, reusing the one setup tab.</summary>
    private void NewTab_Click(object sender, MouseButtonEventArgs e) => OpenSetupTab(null);

    private void OpenSetupTab(string? folder)
    {
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.Setup) ?? AddTab(TabKind.Setup, "setup", "New session");
        if (folder is not null && Directory.Exists(folder)) _setup.ShowFolder(folder);
        SetActive(tab);
        ShowOnly(_setup);
    }

    private OpenTab TabFor(TabKind kind, string key, Func<string> title) =>
        _tabs.FirstOrDefault(t => t.Kind == kind && t.Key == key) ?? AddTab(kind, key, title());

    private OpenTab AddTab(TabKind kind, string key, string title)
    {
        var tab = new OpenTab(kind, key, title);
        _tabs.Add(tab);
        return tab;
    }

    private void SetActive(OpenTab tab)
    {
        _activeTab = tab;
        RefreshTabStrip();
    }

    /// <summary>A tab click shows the tab, and selects its rail item so the two stay in step.</summary>
    private async void ActivateTab(OpenTab tab)
    {
        SetActive(tab);
        switch (tab.Kind)
        {
            case TabKind.Session:
                var railItem = FindItem(Rail.Items, new RailTag(RailKind.Session, tab.Key));
                if (railItem is not null)
                    railItem.IsSelected = true;
                else if (_terminals.TryGetValue(tab.Key, out var terminal))
                    ShowOnly(terminal);
                break;

            case TabKind.Transcript:
                var digest = _history?.FirstOrDefault(d => d.SessionId == tab.Key);
                if (digest is not null) await ShowRailItemAsync(new RailTag(RailKind.History, tab.Key, digest));
                break;

            case TabKind.Setup:
                ShowOnly(_setup);
                break;

            case TabKind.Search:
                ShowOnly(_search);
                break;
        }
    }

    /// <summary>
    /// Closes a tab. A session tab only goes: the session keeps running and stays in the rail. If the closed tab was showing,
    /// the neighbouring tab is shown; with no tab left, the setup tab opens.
    /// </summary>
    private void CloseTab(OpenTab tab)
    {
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (tab.Kind == TabKind.Session) _hiddenSessions.Add(tab.Key);
        if (tab.Kind == TabKind.Search) ClearSearchFilter();
        if (!ReferenceEquals(_activeTab, tab))
        {
            RefreshTabStrip();
            return;
        }

        if (_tabs.Count == 0)
        {
            OpenSetupTab(null);
            return;
        }
        ActivateTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
    }

    /// <summary>
    /// The ✕ on a session tab asks what to do: stop the session as well, or only hide the tab. The other tabs close at once.
    /// </summary>
    private void CloseOrAsk(OpenTab tab)
    {
        if (tab.Kind != TabKind.Session)
        {
            CloseTab(tab);
            return;
        }

        var name = TitleOfSession(tab.Key);
        var prompt = new ClosePrompt(name, Palette) { Owner = this };
        prompt.ShowDialog();
        switch (prompt.Choice)
        {
            case ClosePromptChoice.CloseTab:
                CloseTab(tab);
                break;

            case ClosePromptChoice.CloseSessionAndTab:
                _ = CloseSessionAndTabAsync(tab, name);
                break;
        }
    }

    /// <summary>
    /// Stops a session for good and closes its tab. Its spec leaves runs.json and its record leaves state.json before the stop,
    /// so no pass starts it again. claude stop keeps the conversation, so attaching to it later resumes it. If the stop fails,
    /// the spec and the record come back and the tab stays, so the session never vanishes while it still runs.
    /// </summary>
    private async Task CloseSessionAndTabAsync(OpenTab tab, string name)
    {
        var sessionId = tab.Key;

        // Whether the session still runs is read now, not from the last pass. If the sessions cannot be read, nothing is changed:
        // the session may be running, and only a listing says so.
        try
        {
            _agents = await _cli.ListAgentsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"session not closed: the sessions could not be read: {ex.Message}");
            SetStatus($"Not closed: {name} is unchanged, because the sessions could not be read. {ex.Message}");
            return;
        }

        var listed = _agents.FirstOrDefault(a => a.SessionId == sessionId);
        var needsStop = listed is not null && listed.EffectiveState != "done";

        // What the close takes out is kept, so a failed stop can put it back.
        var runsBefore = ReadRuns();
        var affected = runsBefore.Where(run => run.Specs.Any(spec => spec.Name == name)).Select(run => run.RunId)
            .ToHashSet(StringComparer.Ordinal);
        var originals = runsBefore.Where(run => affected.Contains(run.RunId)).ToList();
        var recordBefore = ReadRecords().TryGetValue(name, out var found) ? found : null;

        try
        {
            _runStore.Update(runs => RunStore.WithoutSession(runs, name));
            _store.Forget(name);
            if (needsStop)
            {
                var stop = await _cli.StopAsync(ClaudeCli.ShortIdOf(sessionId));
                if (stop.ExitCode != 0)
                    throw new ClaudeCliException(string.IsNullOrWhiteSpace(stop.StdErr) ? "claude stop failed" : stop.StdErr.Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RestoreAfterFailedClose(affected, originals, name, recordBefore);
            AppLog.Write($"session not closed: {ex.Message}");
            SetStatus($"Not closed: {name} is still running. {ex.Message}");
            RefreshRail();
            return;
        }

        DetachTerminal(sessionId);
        CloseTab(tab);
        RefreshRail();
        SetStatus($"Stopped {name}. Its conversation is kept; attach to it to resume.");
    }

    /// <summary>Puts back what a failed close took out: the runs that held the session's spec, and its record.</summary>
    private void RestoreAfterFailedClose(HashSet<string> affected, List<RunRecord> originals, string name, SessionRecord? recordBefore)
    {
        try
        {
            if (affected.Count > 0)
                _runStore.Update(runs => runs.Where(run => !affected.Contains(run.RunId)).Concat(originals).ToList());
            if (recordBefore is not null)
                _store.Update(records => records[name] = recordBefore);
        }
        catch (Exception ex) when (ex is StoreUnreadableException or TimeoutException or IOException)
        {
            AppLog.Write($"restore after the failed close not written: {ex.Message}");
            SetStatus($"{name} may be missing from the rail: see the log. {ex.Message}");
        }
    }

    /// <summary>Takes a session's terminal out of the pane and detaches it, the way the window's close detaches every terminal.</summary>
    private void DetachTerminal(string sessionId)
    {
        if (!_terminals.Remove(sessionId, out var terminal)) return;
        if (ReferenceEquals(_shown, terminal)) ShowOnly(_setup);
        Pane.Children.Remove(terminal);
        try
        {
            var term = terminal.DisconnectConPTYTerm();
            term?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write($"detach failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// One tab for each recorded session, so every live session is visible. A tab whose session is gone is removed.
    /// Called on every refresh of the rail.
    /// </summary>
    private void SyncSessionTabs()
    {
        var records = ReadRecords().Values.Where(r => !string.IsNullOrEmpty(r.SessionId)).ToList();
        foreach (var record in records.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            if (_hiddenSessions.Contains(record.SessionId)) continue;
            if (!_tabs.Any(t => t.Kind == TabKind.Session && t.Key == record.SessionId))
                AddTab(TabKind.Session, record.SessionId, record.Name);
        }

        foreach (var tab in _tabs.Where(t => t.Kind == TabKind.Session).ToList())
        {
            var record = records.FirstOrDefault(r => r.SessionId == tab.Key);
            if (record is null)
            {
                _tabs.Remove(tab);
                if (ReferenceEquals(_activeTab, tab)) _activeTab = null;
                continue;
            }
            tab.Title = record.Name;
            tab.State = _agents.FirstOrDefault(a => a.SessionId == tab.Key)?.EffectiveState ?? "not listed";
        }

        if (_tabs.Count == 0) OpenSetupTab(null);
        RefreshTabStrip();
    }

    /// <summary>
    /// Draws the strip: the tabs in order, then "+" as the last item. The active tab takes the content's background; the others
    /// are transparent on the strip. Text and dots are small and muted, so the strip does not compete with the work.
    /// </summary>
    private void RefreshTabStrip()
    {
        TabStrip.Children.Clear();
        var palette = Palette;
        foreach (var tab in _tabs)
        {
            var isActive = ReferenceEquals(tab, _activeTab);
            var content = new StackPanel { Orientation = Orientation.Horizontal };

            var dot = StateDot(tab.State);
            if (dot is not null) content.Children.Add(dot);

            content.Children.Add(new TextBlock
            {
                Text = tab.Title,
                MaxWidth = 160,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = RailPalette.Brush(isActive ? palette.Text : palette.Muted),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var close = new TextBlock
            {
                Text = "✕",
                FontSize = 9,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = RailPalette.Brush(palette.Muted),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tab.Kind == TabKind.Session ? "Closes the tab, or stops the session too" : "Closes the tab.",
            };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                CloseOrAsk(tab);
            };
            content.Children.Add(close);

            var border = new Border
            {
                Background = isActive ? RailPalette.Brush(palette.Background) : Brushes.Transparent,
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Padding = new Thickness(9, 4, 7, 4),
                Margin = new Thickness(0, 0, 2, 0),
                Cursor = Cursors.Hand,
                Child = content,
            };
            border.MouseLeftButtonUp += (_, _) => ActivateTab(tab);
            TabStrip.Children.Add(border);
        }

        TabStrip.Children.Add(PlusTab(palette));
    }

    /// <summary>The "+" at the end of the strip: a quiet mark that opens the setup.</summary>
    private Border PlusTab(RailPalette palette)
    {
        var plus = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            CornerRadius = new CornerRadius(4),
            ToolTip = "New session: opens the setup",
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 14,
                Foreground = RailPalette.Brush(palette.Muted),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        plus.MouseLeftButtonUp += NewTab_Click;
        return plus;
    }

    /// <summary>The state dot: small, and muted green when working, muted amber when blocked, grey otherwise.</summary>
    private static TextBlock? StateDot(string state)
    {
        if (string.IsNullOrEmpty(state)) return null;
        var colour = state switch
        {
            "working" => "#5BA86B",
            "blocked" => "#C9A227",
            _ => "#7C828C",
        };
        return new TextBlock
        {
            Text = "●",
            FontSize = 7,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = RailPalette.Brush(colour),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private string TitleOfSession(string sessionId) =>
        ReadRecords().Values.FirstOrDefault(r => r.SessionId == sessionId)?.Name ?? $"Session {ShortId(sessionId)}";

    private static string ShortId(string sessionId) => sessionId[..Math.Min(8, sessionId.Length)];
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tabs;

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

        /// <summary>A live session's changes (item 2 of the additions plan). Key: the session id.</summary>
        Changes,

        /// <summary>The storage page (gems item 6). One tab, with the key "storage".</summary>
        Storage,

        /// <summary>The run timeline (additions item 5). One tab, with the key "timeline".</summary>
        Timeline,
    }

    private sealed class OpenTab(TabKind kind, string key, string title)
    {
        public TabKind Kind { get; } = kind;

        /// <summary>The session id for session and transcript tabs; "setup" for the setup tab.</summary>
        public string Key { get; } = key;

        public string Title { get; set; } = title;

        /// <summary>The session's state, for the small dot. Empty for the setup and transcript tabs.</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>What the listing says the session waits for (round 7 item 3), or null. Shown in the tab's tooltip.</summary>
        public string? Waiting { get; set; }
    }

    /// <summary>The tab's tooltip: its title, and what the session waits for when the listing says so (round 7 item 3).</summary>
    private static string TabTooltip(OpenTab tab) =>
        tab.State == DaemonStatus.StaleState ? $"{tab.Title}\n{DaemonStatus.DisplayState(tab.State)}"
        : tab.Waiting is { } waiting ? $"{tab.Title}\nwaiting for {waiting}"
        : tab.Title;

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
                SetActive(TabFor(TabKind.Transcript, tag.Key, () => tag.Digest is { } digest ? DisplayTitle(digest) : $"Session {ShortId(tag.Key)}"));
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

    /// <summary>Makes a tab the active one of its pane: the right pane for a session that was moved there, else the left.</summary>
    private void SetActive(OpenTab tab)
    {
        if (IsRight(tab)) _activeTabRight = tab;
        else _activeTab = tab;
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
                if (railItem is not null) railItem.IsSelected = true;
                // Shown here too: selecting a rail item that is already selected raises no event, and the pane would keep
                // whatever it showed (for example the setup).
                if (_terminals.TryGetValue(tab.Key, out var terminal)) ShowOnly(terminal);
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

            case TabKind.Changes:
                await ShowChangesAsync(tab.Key);
                break;

            case TabKind.Storage:
                ShowOnly(_storage);
                _ = RefreshStorageAsync();
                break;

            case TabKind.Timeline:
                ShowOnly(_timeline);
                _ = RefreshTimelineAsync();
                break;
        }
    }

    /// <summary>
    /// Closes a tab. A session tab only goes: the session keeps running and stays in the rail. If the closed tab was showing,
    /// the neighbouring tab is shown; with no tab left, the setup tab opens.
    /// </summary>
    private void CloseTab(OpenTab tab)
    {
        // A tab of the right pane goes back to the left first, hidden, so its terminal is never left in a pane that closes.
        if (IsRight(tab)) MoveToLeft(tab, activate: false);

        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (tab.Kind == TabKind.Session) _hiddenSessions.Add(tab.Key);
        if (tab.Kind == TabKind.Search) ClearSearchFilter();
        if (!ReferenceEquals(_activeTab, tab))
        {
            RefreshTabStrip();
            return;
        }

        // The neighbour in the same (left) pane takes over. With none there, the setup opens, so the left pane is never empty.
        var left = _tabs.Where(t => !IsRight(t)).ToList();
        if (left.Count == 0)
        {
            OpenSetupTab(null);
            return;
        }
        var after = _tabs.Skip(Math.Min(index, _tabs.Count)).FirstOrDefault(t => !IsRight(t));
        ActivateTab(after ?? left[^1]);
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
            _agents = _daemon.IdleListing(await _cli.ListAgentsAsync());
        }
        catch (Exception ex)
        {
            AppLog.Write($"session not closed: the sessions could not be read: {ex.Message}");
            SetStatus($"Not closed: {name} is unchanged, because the sessions could not be read. {ex.Message}");
            return;
        }

        var listed = _agents.FirstOrDefault(a => a.SessionId == sessionId);
        // An idle (done) session is not stopped here, and a stale one is not alive to stop (AgentInfo.NeedsStopOnClose, round 9 item 6).
        // Whether closing a done session should stop it too is the owner's call (review question 6).
        var needsStop = listed is not null && listed.NeedsStopOnClose;

        // What the close takes out is kept, so a failed stop can put it back.
        var runsBefore = ReadRuns();
        var affected = runsBefore.Where(run => run.Specs.Any(spec => spec.Name == name)).Select(run => run.RunId)
            .ToHashSet(StringComparer.Ordinal);
        var originals = runsBefore.Where(run => affected.Contains(run.RunId)).ToList();
        var recordBefore = ReadRecords().TryGetValue(name, out var found) ? found : null;

        // What the session leaves on its run (round 9 item 1): its first start, and the output tokens its transcript holds now, before the
        // close. Without them the run's clock and spend would restart from its next session. A transcript that exists but cannot be read stops
        // the close, since its tokens would be lost from the spend; a session with no transcript has written none.
        var closedStart = recordBefore is { } before && before.SessionId == sessionId ? before.StartedAt : (DateTimeOffset?)null;
        long closedOutput;
        try
        {
            var known = _transcriptPaths.TryGetValue(sessionId, out var path) ? path : null;
            var projectsRoot = _options.ProjectsRoot;
            closedOutput = await Task.Run(() => RunSpend.OutputOfTranscript(sessionId, known ?? TranscriptReader.FindTranscript(projectsRoot, sessionId)));
        }
        // Any failure but cancellation stops the close and keeps the status (round 12 B2): InvalidOperationException was missed here before.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"session not closed: its transcript could not be read: {ex.GetType().Name}: {ex.Message}");
            SetStatus($"Not closed: {name} is unchanged, because its transcript could not be read. {ex.Message}");
            return;
        }

        try
        {
            _runStore.Update(runs => RunStore.WithoutSession(runs, name, closedStart, closedOutput));
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
        SetStatus(needsStop
            ? $"Stopped {name}. Its conversation is kept; attach to it to resume."
            : $"Closed {name}. Nothing was stopped (it was idle or not running). It is no longer supervised; its conversation is kept.");
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
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
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
        if (ReferenceEquals(_shownRight, terminal)) _shownRight = null;
        if (ReferenceEquals(_focusTerminal, terminal)) _focusTerminal = null;
        (terminal.Parent as Panel)?.Children.Remove(terminal);
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
    private void SyncSessionTabs(IReadOnlyDictionary<string, SessionRecord> known)
    {
        var records = known.Values.Where(r => !string.IsNullOrEmpty(r.SessionId)).ToList();
        // New tabs open in the order the owner last left them (T1); a session with no saved place goes after those, by name.
        foreach (var record in TabOrder.Arrange(records.OrderBy(r => r.Name, StringComparer.Ordinal), r => r.SessionId, _savedTabOrder))
        {
            if (_hiddenSessions.Contains(record.SessionId)) continue;
            if (!_tabs.Any(t => t.Kind == TabKind.Session && t.Key == record.SessionId))
            {
                AddTab(TabKind.Session, record.SessionId, record.Name);
                // A session that was at the right when the window last closed opens there again (T2).
                if (_savedRight.Contains(record.SessionId) && _terminals.TryGetValue(record.SessionId, out var terminal))
                    PlaceRight(record.SessionId, terminal);
            }
        }

        foreach (var tab in _tabs.Where(t => t.Kind == TabKind.Session).ToList())
        {
            var record = records.FirstOrDefault(r => r.SessionId == tab.Key);
            if (record is null)
            {
                _tabs.Remove(tab);
                if (ReferenceEquals(_activeTab, tab)) _activeTab = null;
                if (ReferenceEquals(_activeTabRight, tab)) _activeTabRight = null;
                _rightSessions.Remove(tab.Key);
                continue;
            }
            tab.Title = record.Name;
            var listed = _agents.FirstOrDefault(a => a.SessionId == tab.Key);
            tab.State = listed?.EffectiveState ?? "not listed";
            tab.Waiting = listed?.Waiting;
        }

        if (SplitOpen || _rightSessions.Count > 0) SettleRightPane();
        if (!_tabs.Any(t => !IsRight(t))) OpenSetupTab(null);
        RefreshTabStrip();
    }

    /// <summary>
    /// Draws the strip: the tabs in order, then "+" as the last item. The active tab takes the content's background; the others
    /// are transparent on the strip. Text and dots are small and muted, so the strip does not compete with the work.
    /// </summary>
    private void RefreshTabStrip()
    {
        RefreshTabTitles();
        var palette = Palette;
        DrawStrip(TabStrip, _tabs.Where(t => !IsRight(t)), _activeTab, palette);
        TabStrip.Children.Add(PlusTab(palette));
        // The right pane's strip: its session tabs only, and no "+", because a new session always opens at the left.
        DrawStrip(TabStripB, _tabs.Where(IsRight), _activeTabRight, palette);
    }

    private void DrawStrip(StackPanel strip, IEnumerable<OpenTab> tabs, OpenTab? active, RailPalette palette)
    {
        strip.Children.Clear();
        foreach (var tab in tabs.ToList())
        {
            var isActive = ReferenceEquals(tab, active);
            var content = new StackPanel { Orientation = Orientation.Horizontal };

            // The mark at the start of the title: what the session is doing, or that it is closed.
            if (StateGlyph(tab) is { } mark)
            {
                content.Children.Add(new TextBlock
                {
                    Text = mark.Glyph,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = mark.Meaning,
                });
            }

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
                Tag = CloseMark,
            };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                CloseOrAsk(tab);
            };
            content.Children.Add(close);

            // A session's colour is a bar along the top of its tab (D-L11). Tabs without one keep no edge.
            var edge = tab.Kind is TabKind.Session or TabKind.Transcript ? TabEdgeBrush(tab.Key) : null;
            var border = new Border
            {
                // The edge keeps its 2 px whether or not the tab has a colour, so coloured tabs are not taller (D-L11).
                BorderBrush = edge ?? Brushes.Transparent,
                BorderThickness = new Thickness(0, 2, 0, 0),
                Background = isActive ? RailPalette.Brush(palette.Background) : Brushes.Transparent,
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Padding = new Thickness(9, 4, 7, 4),
                Margin = new Thickness(0, 0, 2, 0),
                Cursor = Cursors.Hand,
                Child = content,
                // The drag finds its tab by this, and the tab being dragged is shown faded.
                Tag = tab,
                Opacity = _dragging && ReferenceEquals(tab, _dragTab) ? 0.55 : 1,
                ToolTip = TabTooltip(tab),
            };
            border.MouseLeftButtonUp += (_, _) => ActivateTab(tab);
            if (tab.Kind == TabKind.Session) border.ContextMenu = PaneMenu(tab);
            strip.Children.Add(border);
        }
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
    /// <summary>
    /// The mark at the start of a tab's title. A running session is 🚀 when busy and 💤 when idle; a session waiting for input is 🙋;
    /// a session that is not running, or a past session, is ⏹️. Setup and search tabs have none, and a session tab has none until its
    /// state is known.
    /// </summary>
    private static (string Glyph, string Meaning)? StateGlyph(OpenTab tab)
    {
        if (tab.Kind == TabKind.Transcript) return ("⏹️", "Closed: a past session, not running");
        if (tab.Kind != TabKind.Session || string.IsNullOrEmpty(tab.State)) return null;
        return tab.State switch
        {
            "working" or "busy" => ("🚀", "Running and busy"),
            "done" => ("💤", "Running and idle"),
            "blocked" => ("🙋", "Waiting for input: a permission, a question, or a reply from another session"),
            _ => ("⏹️", "Closed: not running"),
        };
    }

    private string TitleOfSession(string sessionId) => TitleOfSessionNow(sessionId, ReadRecords().Values);

    private static string ShortId(string sessionId) => sessionId[..Math.Min(8, sessionId.Length)];
}

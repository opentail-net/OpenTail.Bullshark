using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Tabs;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The tab strip across the top (owner, 2026-11-15; D-S39). One tab per live session, one setup tab, and one tab per transcript.
/// "+" is always the last item, as in Windows Terminal. The tabs are kept quiet: the active tab takes the content's colour, the
/// others sit on the strip, and the text is small. Clicking a tab selects its rail item, and selecting a rail item opens or shows
/// its tab, so the two always agree. The ✕ on a session tab asks first: hide the tab, or stop the session as well (ClosePrompt).
/// </summary>
public partial class SessionWindow
{
    internal enum TabKind
    {
        Session,
        Setup,
        Transcript,
        Search,

        /// <summary>A live session's changes (item 2 of the additions plan). Key: the session id.</summary>
        Changes,

        /// <summary>
        /// Insights: Away, Usage, Timeline and Storage as sub-tabs (2026-11-18). One tab, with the key "insights". The sub-tab is the page's own.
        /// </summary>
        Insights,

        /// <summary>Settings: General, Naming and Claude (2026-11-18). One tab, with the key "settings".</summary>
        Settings,

        /// <summary>
        /// A past Codex session's messages, read-only (parity P7, T-D3). Key: the session id, with the Codex provider. Its view is in
        /// <see cref="_codexTranscripts"/>. Claude's <see cref="Transcript"/> tab is a different kind and is not used for Codex.
        /// </summary>
        CodexTranscript,

        /// <summary>
        /// The results of a search of past Codex sessions (parity P6). One tab, with the key "codex-search" and the Codex provider. Its view is
        /// <see cref="_codexSearch"/>. Claude's <see cref="Search"/> tab is a different kind and is not used for Codex.
        /// </summary>
        CodexSearchResults,
    }

    internal sealed class OpenTab(TabKind kind, string key, string title, ProviderId? provider = null)
    {
        public TabKind Kind { get; } = kind;

        /// <summary>The session id for session and transcript tabs; "setup" for the setup tab.</summary>
        public string Key { get; } = key;

        /// <summary>
        /// The provider that opened the tab (stage A4). It is kept when the window's provider changes: an open tab stays with its own provider.
        /// Claude when none is given.
        /// </summary>
        public ProviderId Provider { get; } = provider ?? ProviderId.Claude;

        public string Title { get; set; } = title;

        /// <summary>The session's state, for the small dot. Empty for the setup and transcript tabs.</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>What the listing says the session waits for (round 7 item 3), or null. Shown in the tab's tooltip.</summary>
        public string? Waiting { get; set; }

        /// <summary>
        /// True once the owner has closed this Codex tab (R6). The process end that the close causes is then expected, and changes nothing.
        /// </summary>
        public bool ClosedByOwner { get; set; }

        /// <summary>True once this Codex tab's process has ended on its own (R6). The title then carries the ended suffix.</summary>
        public bool Ended { get; set; }

        /// <summary>The resume ladder step that opened this Codex tab (R7), or null for a tab that was not a resume. Logged with an unexpected end.</summary>
        public ResumeStep? LadderStep { get; set; }
    }

    /// <summary>The tab's tooltip: its title, and what the session waits for when the listing says so (round 7 item 3).</summary>
    private static string TabTooltip(OpenTab tab) =>
        tab.State == DaemonStatus.StaleState ? $"{tab.Title}\n{DaemonStatus.DisplayState(tab.State)}"
        : tab.Waiting is { } waiting ? $"{tab.Title}\nwaiting for {waiting}"
        : tab.Title;

    private readonly List<OpenTab> _tabs = new();

    /// <summary>Sessions whose tab the owner closed. Their tab stays closed until the owner selects the session in the rail.</summary>
    private readonly HashSet<SessionKey> _hiddenSessions = new();
    private OpenTab? _activeTab;

    /// <summary>Called when the rail selection changes: a session or a transcript gets its tab, and a folder shows the setup tab.</summary>
    private void NoteRailSelection(RailTag tag)
    {
        switch (tag.Kind)
        {
            case RailKind.Session:
                _hiddenSessions.Remove(KeyOf(tag.Key));
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
    private void NewTab_Click(object sender, MouseButtonEventArgs e)
    {
        // Under Codex the "+" starts a Codex session (the rail's own action); the Claude run setup is for Claude.
        if (IsCodexActive) NewCodexSession();
        else OpenSetupTab(null);
    }

    private void OpenSetupTab(string? folder)
    {
        var tab = _tabs.FirstOrDefault(t => t.Kind == TabKind.Setup) ?? AddTab(TabKind.Setup, "setup", "New session");
        if (folder is not null && Directory.Exists(folder)) _setup.ShowFolder(folder);
        SetActive(tab);
        ShowOnly(_setup);
    }

    /// <summary>The tab of a rail row, made when there is none. A rail row of Claude's is passed with Claude (the default), so a tab is never a Codex one by mistake.</summary>
    private OpenTab TabFor(TabKind kind, string key, Func<string> title, ProviderId? provider = null) =>
        _tabs.FirstOrDefault(t => t.Kind == kind && t.Provider == (provider ?? ProviderId.Claude) && SameId(t.Key, key))
        ?? AddTab(kind, key, title(), provider ?? ProviderId.Claude);

    /// <summary>
    /// Adds a tab. Its provider is the one given, else the window's active provider (the setup and the other views, which are not a session's).
    /// </summary>
    private OpenTab AddTab(TabKind kind, string key, string title, ProviderId? provider = null)
    {
        var tab = new OpenTab(kind, key, title, provider ?? _activeProvider.Id);
        _tabs.Add(tab);
        return tab;
    }

    /// <summary>Makes a tab the active one of its pane: the right pane for a session that was moved there, else the left.</summary>
    private void SetActive(OpenTab tab)
    {
        if (IsRight(tab)) _activeTabRight = tab;
        else _activeTab = tab;
        // The owner opening a session's tab acknowledges its wait (escalation D3). Nothing else acknowledges it. A Claude session only: a Codex tab
        // has no Claude record to acknowledge.
        if (tab.Kind == TabKind.Session && tab.Provider == ProviderId.Claude) AcknowledgeSession(tab.Key);
        RefreshTabStrip();
    }

    /// <summary>A tab click shows the tab, and selects its rail item so the two stay in step.</summary>
    private async void ActivateTab(OpenTab tab)
    {
        SetActive(tab);
        switch (tab.Kind)
        {
            case TabKind.Session:
                // A Codex tab has no rail row of its own (the Codex rows are history rows), so only its terminal is shown.
                if (tab.Provider == ProviderId.Claude)
                {
                    var railItem = FindItem(Rail.Items, new RailTag(RailKind.Session, tab.Key));
                    if (railItem is not null) railItem.IsSelected = true;
                }
                // Shown here too: selecting a rail item that is already selected raises no event, and the pane would keep
                // whatever it showed (for example the setup).
                if (_terminals.TryGetValue(KeyOf(tab), out var terminal)) ShowOnly(terminal);
                break;

            case TabKind.Transcript:
                var digest = _history?.FirstOrDefault(d => SameId(d.SessionId, tab.Key));
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

            case TabKind.Insights:
                // Shows the sub-tab it was on, and reads that page again (InsightsView.SubTabShown).
                ShowOnly(_insights);
                _insights.Show(_insights.Current);
                break;

            case TabKind.Settings:
                ShowOnly(_settings);
                RefreshSettingsPage();
                break;

            case TabKind.CodexTranscript:
                if (_codexTranscripts.TryGetValue(KeyOf(tab), out var codexView)) ShowOnly(codexView);
                break;

            case TabKind.CodexSearchResults:
                ShowOnly(_codexSearch);
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

        // The neighbour rule is Core's (perf and testability batch, Q6 b), worked out on the strip as it stood before the close.
        var index = _tabs.IndexOf(tab);
        var next = TabPolicy.ActiveAfterClose(_tabs.ToList(), index, IsRight);
        _tabs.Remove(tab);
        if (tab.Kind == TabKind.Session && tab.Provider == ProviderId.Claude) _hiddenSessions.Add(KeyOf(tab));
        if (tab.Kind == TabKind.CodexTranscript) RemoveCodexTranscript(KeyOf(tab));
        if (tab.Kind == TabKind.Search) ClearSearchFilter();
        if (!ReferenceEquals(_activeTab, tab))
        {
            RefreshTabStrip();
            return;
        }

        // The neighbour in the same (left) pane takes over. With none there, the setup opens, so the left pane is never empty.
        if (next is not { } at)
        {
            OpenSetupTab(null);
            return;
        }
        ActivateTab(_tabs[at]);
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
        // A Codex tab is the window's own terminal: there is no Claude session to stop, so its close only closes the terminal (C3).
        if (tab.Provider != ProviderId.Claude)
        {
            CloseCodexTab(tab);
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
        // A pass that is running holds the specs it read before this close, so it could resume the session the owner has just closed, and its
        // merge would then drop the record (F6). The close waits for that pass to end, and no pass starts until the close is done.
        _sessionsClosing++;
        try
        {
            while (_passRunning) await Task.Delay(100);
            await CloseSessionCoreAsync(tab, name);
        }
        finally
        {
            _sessionsClosing--;
        }
    }

    private async Task CloseSessionCoreAsync(OpenTab tab, string name)
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

        var listed = _agents.FirstOrDefault(a => SameId(a.SessionId, sessionId));
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
        var closedStart = recordBefore is { } before && SameId(before.SessionId, sessionId) ? before.StartedAt : (DateTimeOffset?)null;
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
            RestoreAfterFailedClose(originals, name, closedStart, closedOutput, recordBefore);
            AppLog.Write($"session not closed: {ex.Message}");
            SetStatus($"Not closed: {name} is still running. {ex.Message}");
            RefreshRail();
            return;
        }

        DetachTerminal(KeyOf(sessionId));
        CloseTab(tab);
        RefreshRail();
        SetStatus(needsStop
            ? $"Stopped {name}. Its conversation is kept; attach to it to resume."
            : $"Closed {name}. Nothing was stopped (it was idle or not running). It is no longer supervised; its conversation is kept.");
    }

    /// <summary>
    /// Puts back what a failed close took out: this session's spec, in the run it belonged to, and its record (reliability batch R6). Only this
    /// session is put back. The runs are read as they are now, so a session another close took out meanwhile stays out, and the other sessions of
    /// the run are not put back as they were before.
    /// </summary>
    private void RestoreAfterFailedClose(
        List<RunRecord> originals,
        string name,
        DateTimeOffset? closedStart,
        long closedOutput,
        SessionRecord? recordBefore)
    {
        try
        {
            if (originals.Count > 0)
                _runStore.Update(runs => RunStore.WithSessionRestored(runs, originals, name, closedStart, closedOutput));
            if (recordBefore is { } previous)
            {
                // Put back only if nothing newer was stored since the record was read before the close (R5): a newer one is kept.
                _store.Update(records =>
                {
                    if (!_store.PutFrom(records, name, previous, previous.Version))
                        AppLog.Write("restore after the failed close ignored: the stored record is newer");
                });
            }
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"restore after the failed close not written: {ex.Message}");
            SetStatus($"{name} may be missing from the rail: see the log. {ex.Message}");
        }
    }

    /// <summary>
    /// Closes a Codex tab: the tab goes, and its terminal is closed with it. The Codex conversation is kept by Codex and can be resumed from the
    /// Codex rows. Nothing is asked, as there is no Claude session behind the tab.
    /// </summary>
    private void CloseCodexTab(OpenTab tab)
    {
        var key = KeyOf(tab);
        // Marked before the terminal is detached: the process end that the detach causes is then expected (R6), and changes nothing.
        tab.ClosedByOwner = true;
        CloseTab(tab);
        DetachTerminal(key);
        SetStatus($"{tab.Title} is closed. Codex keeps the conversation: resume it from the Codex rows.");
    }

    /// <summary>Takes a terminal out of the pane and detaches it, the way the window's close detaches every terminal.</summary>
    private void DetachTerminal(SessionKey key)
    {
        if (!_terminals.Remove(key, out var terminal)) return;
        if (ReferenceEquals(_shown, terminal)) ShowOnly(_setup);
        if (ReferenceEquals(_shownRight, terminal)) _shownRight = null;
        if (ReferenceEquals(_focusTerminal, terminal)) _focusTerminal = null;
        (terminal.Parent as Panel)?.Children.Remove(terminal);
        DetachConnection(terminal);
    }

    /// <summary>
    /// Disconnects a terminal from its session's process and disposes the connection. When the process has already ended, the library's
    /// disconnect throws (its pipe is closed: "Cannot access a closed file", four times per close in the screenshot runs, 2026-11-18). Then
    /// nothing is left to detach, so it is not logged as a failure, and the connection is still disposed so its handles are released. A
    /// failure while the process is still running is logged, as before.
    /// </summary>
    private static void DetachConnection(ExtendedTerminalControl terminal)
    {
        // Read before the disconnect, which may throw; the connection is what the disposal needs.
        var connection = terminal.ConPTYTerm;
        bool ended;
        try
        {
            ended = connection?.Process is { HasExited: true };
        }
        catch (InvalidOperationException)
        {
            ended = false; // the process object is not usable: treat it as running, so a failure is still logged
        }
        try
        {
            var term = terminal.DisconnectConPTYTerm();
            term?.Dispose();
        }
        catch (Exception ex) when (ended && ex is ObjectDisposedException or IOException or InvalidOperationException)
        {
            try
            {
                connection?.Dispose();
            }
            catch (Exception inner) when (inner is ObjectDisposedException or IOException or InvalidOperationException)
            {
                // Closed with the process already: nothing more to release.
            }
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
        // The records are Claude's, so only Claude's session tabs are matched and removed here. A Codex tab is the window's own terminal and stays.
        foreach (var record in TabOrder.Arrange(records.OrderBy(r => r.Name, StringComparer.Ordinal), r => r.SessionId, _savedTabOrder))
        {
            if (_hiddenSessions.Contains(KeyOf(record.SessionId))) continue;
            if (!_tabs.Any(t => t.Kind == TabKind.Session && t.Provider == ProviderId.Claude && SameId(t.Key, record.SessionId)))
            {
                AddTab(TabKind.Session, record.SessionId, record.Name, ProviderId.Claude);
                // A session that was at the right when the window last closed opens there again (T2).
                if (_savedRight.Contains(record.SessionId) && _terminals.TryGetValue(KeyOf(record.SessionId), out var terminal))
                    PlaceRight(KeyOf(record.SessionId), terminal);
            }
        }

        foreach (var tab in TabLayoutFile.ClaudeSessionTabs(_tabs).ToList())
        {
            var record = records.FirstOrDefault(r => SameId(r.SessionId, tab.Key));
            if (record is null)
            {
                _tabs.Remove(tab);
                if (ReferenceEquals(_activeTab, tab)) _activeTab = null;
                if (ReferenceEquals(_activeTabRight, tab)) _activeTabRight = null;
                _rightSessions.Remove(KeyOf(tab));
                continue;
            }
            tab.Title = record.Name;
            var listed = _agents.FirstOrDefault(a => SameId(a.SessionId, tab.Key));
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
            // The colour is the tab's provider's mark (D1): Claude's for a Claude tab, the codex: entry for a Codex tab.
            var edge = tab.Kind is (TabKind.Session or TabKind.Transcript) ? TabEdgeBrush(tab.Provider, tab.Key) : null;
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

    private static string ShortId(string sessionId) => ClaudeCli.ShortIdOf(sessionId);

    /// <summary>
    /// Whether two session ids are the same session (round 12 B7). One rule for every id compare in the window: case is ignored, as the
    /// other lookups by id do. A case-sensitive compare could miss a session.
    /// </summary>
    private static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
